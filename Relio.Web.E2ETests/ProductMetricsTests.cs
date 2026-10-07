using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Relio.Application.Metrics;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class ProductMetricsTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Default_off_collects_no_activity_for_a_fresh_user()
    {
        var email = NewEmail("metrics-off");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        using var scope = fixture.App.CreateRealScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<UserManager<RelioUser>>().FindByEmailAsync(email);
        user.Should().NotBeNull();
        services.GetRequiredService<IOptionsMonitor<ProductMetricsOptions>>()
            .CurrentValue.Enabled.Should().BeFalse();
        (await services.GetRequiredService<RelioDbContext>().Set<ProductActivity>()
            .AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == user!.Id))
            .Should().BeFalse();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Explicitly_enabled_metrics_record_minimal_activity_and_render_aggregates_only()
    {
        await using var variant = VariantApp.Create(
            fixture,
            new Dictionary<string, string?>
            {
                ["HostedFeatures__ProductMetrics__Enabled"] = "true",
            },
            seedDemoData: false);
        var page = await variant.NewPageAsync();
        var email = NewEmail("metrics-on");
        await RegisterAsync(page, email, StrongPassword);

        string userId;
        using (var scope = variant.Factory.CreateRealScope())
        {
            var services = scope.ServiceProvider;
            services.GetRequiredService<IOptionsMonitor<ProductMetricsOptions>>()
                .CurrentValue.Enabled.Should().BeTrue();
            var user = await services.GetRequiredService<UserManager<RelioUser>>().FindByEmailAsync(email);
            user.Should().NotBeNull();
            userId = user!.Id;

            var dbContext = services.GetRequiredService<RelioDbContext>();
            var person = new Person
            {
                OwnerId = userId,
                FirstName = "PRIVATE-METRICS-PERSON-NAME",
                Details = "PRIVATE-METRICS-PERSON-DETAILS",
                IsArchived = true,
            };
            dbContext.People.Add(person);
            dbContext.Notes.Add(new Note
            {
                OwnerId = userId,
                Person = person,
                Text = "PRIVATE-METRICS-NOTE",
            });
            dbContext.Interactions.Add(new Interaction
            {
                OwnerId = userId,
                OccurredOn = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime),
                Description = "PRIVATE-METRICS-INTERACTION",
            });
            dbContext.Interactions.Add(new Interaction
            {
                OwnerId = userId,
                OccurredOn = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(1),
                Description = "PRIVATE-METRICS-INTERACTION-TOMORROW",
            });
            dbContext.Reminders.Add(new Reminder
            {
                OwnerId = userId,
                Person = person,
                Title = "PRIVATE-METRICS-REMINDER",
                DueDate = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime),
                IsCompleted = true,
            });
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();
        }

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/admin/metrics");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Product metrics", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='metrics-live-accounts']")).ToContainTextAsync("1");
        await Expect(page.Locator("[data-testid='metrics-people']")).ToContainTextAsync("1");
        var interactionsText = await page.Locator("[data-testid='metrics-interactions']").InnerTextAsync();
        Regex.IsMatch(interactionsText, @"\b[12]\b").Should().BeTrue();
        await Expect(page.Locator("[data-testid='metrics-reminders']")).ToContainTextAsync("1");
        await Expect(page.Locator("[data-testid='metrics-retention-no-data']"))
            .ToHaveTextAsync("No completed cohorts yet");
        await Expect(page.Locator("body")).Not.ToContainTextAsync("PRIVATE-METRICS-PERSON-NAME");
        await Expect(page.Locator("body")).Not.ToContainTextAsync("PRIVATE-METRICS-PERSON-DETAILS");
        await Expect(page.Locator("body")).Not.ToContainTextAsync("PRIVATE-METRICS-NOTE");
        await Expect(page.Locator("body")).Not.ToContainTextAsync("PRIVATE-METRICS-INTERACTION");
        await Expect(page.Locator("body")).Not.ToContainTextAsync("PRIVATE-METRICS-REMINDER");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(0);

        using (var scope = variant.Factory.CreateRealScope())
        {
            var activities = await scope.ServiceProvider.GetRequiredService<RelioDbContext>()
                .Set<ProductActivity>()
                .AsNoTracking()
                .Where(activity => activity.OwnerId == userId)
                .ToListAsync();
            activities.Should().ContainSingle();
            var activity = activities[0];
            activity.CohortStartedOnUtc.Should().Be(DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime));
            activity.LastActiveOnUtc.Should().Be(activity.CohortStartedOnUtc);
            activity.ReturnedInDays30To59.Should().BeFalse();
            activity.RetentionExpiresAtUtc.Should()
                .Be(ProductActivityCohortRules.RetentionExpiresAtUtc(activity.CohortStartedOnUtc));
        }
    }
}
