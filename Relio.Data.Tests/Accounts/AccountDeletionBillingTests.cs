using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Accounts;
using Relio.Application.Billing;
using Relio.Data.Accounts;
using Relio.Data.Billing;
using Relio.Data.Identity;
using Relio.Data.Tests.Billing;
using Relio.Data.Tests.People;
using Relio.Domain;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Accounts;

/// <summary>
/// Erasure with hosted billing: the subscription is cancelled at the provider before anything is
/// deleted, a failed cancellation deletes nothing, and the subscription row goes with the account.
/// </summary>
public sealed class AccountDeletionBillingTests
{
    [Fact]
    public async Task Deleting_a_paying_account_cancels_its_subscription_first_and_removes_the_billing_row()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "paying@example.com");
        await AddSubscriptionAsync(dbContext, user.Id, "cus_paying");
        var provider = new FakeBillingProvider();

        var result = await CreateService(dbContext, user.Id, provider)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        provider.CancelledCustomers.Should().Equal("cus_paying");
        (await dbContext.UserSubscriptions.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await dbContext.Users.AsNoTracking().AnyAsync(item => item.Id == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_cancellation_deletes_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "cancel-fails@example.com");
        await AddSubscriptionAsync(dbContext, user.Id, "cus_unreachable");
        dbContext.People.Add(new Person { OwnerId = user.Id, FirstName = "Kept" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var provider = new FakeBillingProvider { CancellationResult = new SubscriptionCancellationResult(false, "down") };

        var result = await CreateService(dbContext, user.Id, provider)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.BillingCancellationFailed);
        (await dbContext.Users.AsNoTracking().AnyAsync(item => item.Id == user.Id)).Should().BeTrue();
        (await dbContext.People.AsNoTracking().CountAsync(item => item.OwnerId == user.Id)).Should().Be(1);
        (await dbContext.UserSubscriptions.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_wrong_password_never_reaches_the_billing_provider()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "wrong-password@example.com");
        await AddSubscriptionAsync(dbContext, user.Id, "cus_wrong_password");
        var provider = new FakeBillingProvider();

        var result = await CreateService(dbContext, user.Id, provider)
            .DeleteAsync(new AccountDeletionRequest("not the password", Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.CurrentPasswordIncorrect);
        provider.CancelledCustomers.Should().BeEmpty();
    }

    [Fact]
    public async Task An_account_that_never_paid_is_deleted_without_calling_the_provider()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "free@example.com");
        var provider = new FakeBillingProvider();

        var result = await CreateService(dbContext, user.Id, provider)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        provider.CancelledCustomers.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_accounts_subscription_is_untouched()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "leaving@example.com");
        var other = await CreateUserAsync(dbContext, "staying@example.com");
        await AddSubscriptionAsync(dbContext, user.Id, "cus_leaving");
        await AddSubscriptionAsync(dbContext, other.Id, "cus_staying");
        var provider = new FakeBillingProvider();

        await CreateService(dbContext, user.Id, provider)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        provider.CancelledCustomers.Should().Equal("cus_leaving");
        (await dbContext.UserSubscriptions.AsNoTracking().SingleAsync()).UserId.Should().Be(other.Id);
    }

    private static async Task AddSubscriptionAsync(RelioDbContext dbContext, string userId, string customerId)
    {
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            UserId = userId,
            Tier = PlanTier.Pro,
            BillingProviderCustomerId = customerId,
            BillingProviderSubscriptionId = $"sub_{customerId}",
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static AccountDeletionService CreateService(RelioDbContext dbContext, string userId, IBillingProvider provider) =>
        new(
            dbContext,
            new FakeCurrentUser(userId),
            new PasswordHasher<RelioUser>(),
            NullLogger<AccountDeletionService>.Instance,
            provider);
}
