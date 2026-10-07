using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data;
using Relio.Domain;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

/// <summary>
/// Loads the pages whose components fetch data side by side against a real SQL Server, in both the
/// prerender (a plain HTTP request) and the interactive circuit that takes over. The EF Core InMemory
/// provider every other test uses completes its "async" work synchronously, so it can never show two
/// components overlapping on the circuit's one <see cref="RelioDbContext"/> ("A second operation was
/// started on this context instance before a previous operation completed"); see "One database
/// operation at a time" in AGENTS.md.
/// </summary>
/// <remarks>
/// Skipped unless <c>ConnectionStrings__Relio</c> names a SQL Server (CI always sets it). Runs in its own
/// <see cref="VariantApp"/> on a throwaway database, which the app creates by applying its migrations at
/// startup (the test host runs in Development) and the demo seeder fills; the database is dropped at the end.
/// </remarks>
[Collection(RelioAppCollection.Name)]
public class SqlServerPageLoadTests(RelioAppFixture fixture)
{
    [SqlServerFact]
    public async Task Pages_with_several_data_loading_sections_render_against_SQL_Server()
    {
        var database = new SqlConnectionStringBuilder(SqlServerTestEnvironment.ServerConnectionString)
        {
            InitialCatalog = $"Relio_E2E_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var app = VariantApp.Create(fixture, new Dictionary<string, string?>
        {
            ["Database__Provider"] = "SqlServer",
            ["ConnectionStrings__Relio"] = database,
            // /admin/users then shows both the account list and the invitation panel.
            ["Registration__Mode"] = "InviteOnly",
        });

        try
        {
            var page = await app.NewPageAsync();
            await RelioAppFixture.SignInAsDemoAsync(page);

            // /settings: display name, time zone and sign-in security each load in OnInitializedAsync.
            var response = await page.GotoAsync("/settings");
            response!.Status.Should().Be(200, "the prerender must not throw");
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
            await Expect(page.GetByTestId("settings-current-email")).ToHaveTextAsync("demo@relio.local");
            await Expect(page.GetByTestId("settings-2fa-state")).ToHaveTextAsync("Off");
            await AssertCircuitSurvivesAsync(page);
            // /admin/users in InviteOnly mode: AccountList and InvitationPanel load side by side.
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/admin/users");
            await Expect(page.GetByTestId("admin-accounts")).ToContainTextAsync("demo@relio.local");
            await Expect(page.GetByTestId("admin-invite-submit")).ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);
            // The two label sub-pages (issue #25): one data consumer each, loading counts in one query.
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/relationship-types");
            await Expect(page.GetByTestId("relationship-type-list")).ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/tags");
            await Expect(page.GetByTestId("tag-add-form")).ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);
            // /people/new: PersonForm loads the relationship types.
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
            await Expect(page.GetByTestId("person-form")).ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);
            // /people/{id}/merge (issue #28): both profiles, the candidates and today's date, one after
            // the other, in the first step and then in the comparison.
            Guid[] demoPeople;
            string demoUserId;
            using (var scope = app.Factory.CreateRealScope())
            {
                var demo = await scope.ServiceProvider
                    .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Relio.Data.Identity.RelioUser>>()
                    .FindByEmailAsync(Relio.Data.Seeding.DemoDataSeeder.DemoEmail);
                demoUserId = demo!.Id;
                var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
                demoPeople = await dbContext.People
                    .Where(p => p.OwnerId == demo!.Id)
                    .OrderBy(p => p.FirstName)
                    .Select(p => p.Id)
                    .Take(2)
                    .ToArrayAsync();

                dbContext.Notes.Add(new Note
                {
                    OwnerId = demoUserId,
                    PersonId = demoPeople[0],
                    Text = "A note for the SQL Server page-load test.",
                    IsPinned = true,
                });
                var interaction = new Interaction
                {
                    OwnerId = demoUserId,
                    OccurredOn = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(-1),
                    Kind = InteractionKind.Meeting,
                    Description = "A shared moment for the SQL Server page-load test.",
                };
                dbContext.Interactions.Add(interaction);
                dbContext.InteractionParticipants.Add(new InteractionParticipant
                {
                    OwnerId = demoUserId,
                    Interaction = interaction,
                    PersonId = demoPeople[0],
                });
                await dbContext.SaveChangesAsync();
            }

            // /people/{id}: PinnedNotes and the mixed timeline both load alongside the profile on
            // the same circuit DbContext, including the real SQL Server query paths.
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{demoPeople[0]}");
            await Expect(page.GetByTestId("pinned-note-text")).ToHaveTextAsync("A note for the SQL Server page-load test.");
            await Expect(page.GetByTestId("timeline-entries")
                .GetByText("A shared moment for the SQL Server page-load test.", new() { Exact = true }))
                .ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{demoPeople[0]}/merge");
            await Expect(page.GetByTestId("merge-picker-field")).ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{demoPeople[0]}/merge?with={demoPeople[1]}");
            await Expect(page.GetByTestId("merge-preview")).ToBeVisibleAsync();
            await AssertCircuitSurvivesAsync(page);
        }
        finally
        {
            using var scope = app.Factory.CreateRealScope();
            await scope.ServiceProvider.GetRequiredService<RelioDbContext>().Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertCircuitSurvivesAsync(IPage page)
    {
        // A component that throws while the circuit initializes it ends the circuit and shows
        // #blazor-error-ui. The prerendered markup is identical either way, so give the circuit's own
        // first loads time to finish (they take milliseconds) before asserting nothing went wrong.
        await page.WaitForTimeoutAsync(1500);
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }
}
