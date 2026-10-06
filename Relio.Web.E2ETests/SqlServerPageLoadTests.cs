using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data;
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
            // /people/new: PersonForm loads the relationship types.
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
            await Expect(page.GetByTestId("person-form")).ToBeVisibleAsync();
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
