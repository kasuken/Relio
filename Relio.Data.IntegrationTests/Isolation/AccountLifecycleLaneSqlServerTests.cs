using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Accounts;
using Relio.Application.Security;
using Relio.Data.Accounts;
using Relio.Data.Concurrency;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class AccountLifecycleLaneSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Account_erasure_and_session_status_reads_are_serialized_on_one_context_lane()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await using var dbContext = fixture.CreateDbContext(
            new SlowReaderInterceptor(TimeSpan.FromMilliseconds(100)));
        var currentUser = new FakeCurrentUser(harness.OwnerA.Id);
        var deletion = new AccountDeletionService(
            dbContext,
            currentUser,
            new PasswordHasher<RelioUser>(),
            NullLogger<AccountDeletionService>.Instance);
        var accountStatus = new AccountSessionStatusService(dbContext, currentUser);
        var deletionProxy = DatabaseLaneProxy<IAccountDeletionService>.Create(deletion, dbContext.Lane);
        var statusProxy = DatabaseLaneProxy<IAccountSessionStatusService>.Create(accountStatus, dbContext.Lane);

        var deletionTask = deletionProxy.DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: true));
        var statusTask = statusProxy.GetCurrentStatusAsync();
        await Task.WhenAll(deletionTask, statusTask);

        (await deletionTask).Status.Should().Be(AccountDeletionStatus.Deleted);
        (await statusTask).Should().BeOneOf(AccountSessionStatus.Active, AccountSessionStatus.Missing);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await using var verify = fixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }
}
