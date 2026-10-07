using Relio.Application.Accounts;
using Relio.Application.Security;
using Relio.Data.Accounts;
using Relio.Data.Tests.Administration;
using Relio.Data.Tests.People;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Accounts;

public sealed class AccountSessionStatusServiceTests
{
    [Fact]
    public async Task GetCurrentStatusAsync_reads_the_fresh_status_of_only_the_current_account()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var activeUser = await CreateUserAsync(dbContext, "active-session@example.com");
        var disabledUser = await CreateUserAsync(dbContext, "disabled-session@example.com");
        disabledUser.IsDisabled = true;
        await dbContext.SaveChangesAsync();

        var activeService = new AccountSessionStatusService(dbContext, new FakeCurrentUser(activeUser.Id));
        var disabledService = new AccountSessionStatusService(dbContext, new FakeCurrentUser(disabledUser.Id));
        var missingService = new AccountSessionStatusService(dbContext, new FakeCurrentUser("missing-account"));

        (await activeService.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Active);
        (await disabledService.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Disabled);
        (await missingService.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Missing);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_requires_an_authenticated_current_user()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = new AccountSessionStatusService(dbContext, new FakeCurrentUser(null));

        await service.Invoking(item => item.GetCurrentStatusAsync())
            .Should().ThrowAsync<UnauthenticatedUserException>();
    }
}
