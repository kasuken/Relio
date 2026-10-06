using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Data.People;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Proves cross-user isolation holds through the real, wired-up app (real
/// <see cref="RelioDbContext"/>, real ASP.NET Core Identity users) rather than only at the unit
/// level (see <c>Relio.Data.Tests.People.PeopleServiceOwnershipTests</c>) - the demo account
/// (seeded with ~8 people) against a brand-new user registered through the actual browser flow.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class CrossUserIsolationTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Two_users_each_see_only_their_own_people_through_the_real_service()
    {
        var newUserEmail = $"isolation-{Guid.NewGuid():N}@example.com";

        var page = await fixture.NewPageAsync();
        await page.GotoAsync("/Account/Register");
        await page.Locator("[data-testid='register-email']").FillAsync(newUserEmail);
        await page.Locator("[data-testid='register-password']").FillAsync("Str0ng-Passw0rd!");
        await page.Locator("[data-testid='register-confirm-password']").FillAsync("Str0ng-Passw0rd!");
        await page.Locator("[data-testid='register-submit']").ClickAsync();
        await page.Locator("[data-testid='register-confirmation-continue']").WaitForAsync();
        await RelioAppFixture.ClosePageAsync(page);

        using var scope = fixture.App.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();

        var demoUser = await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail);
        var newUser = await userManager.FindByEmailAsync(newUserEmail);
        demoUser.Should().NotBeNull();
        newUser.Should().NotBeNull();

        // The real IPeopleService implementation, just constructed with a test-controlled
        // ICurrentUser instead of the circuit-backed one - there is no HTTP/circuit context to
        // resolve AuthenticationStateCurrentUser from outside a browser request.
        var demoPeopleService = new PeopleService(dbContext, new FixedCurrentUser(demoUser!.Id), timeProvider);
        var newUserPeopleService = new PeopleService(dbContext, new FixedCurrentUser(newUser!.Id), timeProvider);

        var demoPeopleBefore = await demoPeopleService.ListAsync();
        demoPeopleBefore.Should().NotBeEmpty("DemoDataSeeder seeds sample people for the demo account");

        var newUsersPerson = await newUserPeopleService.CreateAsync(
            new CreatePersonRequest("Isolation", "Test", null));

        var demoPeopleAfter = await demoPeopleService.ListAsync();
        var newUsersPeople = await newUserPeopleService.ListAsync();

        demoPeopleAfter.Should().HaveCount(demoPeopleBefore.Count, "the new user's person must not leak into the demo account's list");
        demoPeopleAfter.Should().NotContain(p => p.Id == newUsersPerson.Id);
        newUsersPeople.Should().ContainSingle(p => p.Id == newUsersPerson.Id);

        var demoReadingNewUsersPerson = await demoPeopleService.GetAsync(newUsersPerson.Id);
        demoReadingNewUsersPerson.Should().BeNull("GetAsync must not resolve another user's person");
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
