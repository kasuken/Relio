using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Accounts;
using Relio.Application.Security;
using Relio.Data.Accounts;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class AccountSessionStatusSqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Anonymous_call_is_rejected_before_identity_status_is_read()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await using var anonymousScope = harness.As(null);
        var service = new AccountSessionStatusService(anonymousScope.DbContext, anonymousScope.CurrentUser);

        var exception = await Assert.ThrowsAsync<UnauthenticatedUserException>(
            () => service.GetCurrentStatusAsync());

        exception.Should().NotBeNull();
        await using var verify = fixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeTrue();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Status_is_fresh_owner_scoped_and_reports_a_deleted_real_identity_account_as_missing()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await using var ownerAScope = harness.AsOwnerA();
        await using var ownerBScope = harness.AsOwnerB();
        var ownerAStatus = new AccountSessionStatusService(ownerAScope.DbContext, ownerAScope.CurrentUser);
        var ownerBStatus = new AccountSessionStatusService(ownerBScope.DbContext, ownerBScope.CurrentUser);

        ownerAScope.DbContext.Attach(new RelioUser { Id = harness.OwnerA.Id });
        await SetDisabledAsync(harness.OwnerB.Id, isDisabled: true);

        (await ownerAStatus.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Active);
        (await ownerBStatus.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Disabled);

        await SetDisabledAsync(harness.OwnerA.Id, isDisabled: true);
        (await ownerAStatus.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Disabled);

        await SetDisabledAsync(harness.OwnerA.Id, isDisabled: false);
        var deletion = new AccountDeletionService(
            ownerAScope.DbContext,
            ownerAScope.CurrentUser,
            new PasswordHasher<RelioUser>(),
            NullLogger<AccountDeletionService>.Instance);
        var deletionResult = await deletion.DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: true));

        deletionResult.Status.Should().Be(AccountDeletionStatus.Deleted);
        (await ownerAStatus.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Missing);
        (await ownerBStatus.GetCurrentStatusAsync()).Should().Be(AccountSessionStatus.Disabled);
    }

    private async Task SetDisabledAsync(string userId, bool isDisabled)
    {
        await using var dbContext = fixture.CreateDbContext();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId);
        user.IsDisabled = isDisabled;
        await dbContext.SaveChangesAsync();
    }
}
