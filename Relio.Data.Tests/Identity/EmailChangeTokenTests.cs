using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Identity;
using Relio.Data.Tests.Seeding;

namespace Relio.Data.Tests.Identity;

/// <summary>
/// Pins down the ASP.NET Core Identity behaviour issue #18's email change relies on (see
/// "Account settings" in AGENTS.md), against a real <see cref="UserManager{TUser}"/>: a change-email
/// token is bound to the address it was generated for, is single use, and rotates the security
/// stamp - and <see cref="RelioUser.PendingEmail"/> survives a round trip through the database.
/// </summary>
public class EmailChangeTokenTests
{
    private const string Password = "Correct-Horse-Battery-9";

    [Fact]
    public async Task A_token_for_one_pending_email_cannot_confirm_a_different_email()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "old@example.com");

        var tokenForFirst = await userManager.GenerateChangeEmailTokenAsync(user, "first@example.com");

        // A newer request for a different address: the older token must not work for it.
        var result = await userManager.ChangeEmailAsync(user, "second@example.com", tokenForFirst);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "InvalidToken");
        (await userManager.FindByIdAsync(user.Id))!.Email.Should().Be("old@example.com");
    }

    [Fact]
    public async Task ChangeEmailAsync_confirms_the_new_email_and_rotates_the_security_stamp()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "old@example.com");
        var stampBefore = await userManager.GetSecurityStampAsync(user);
        var token = await userManager.GenerateChangeEmailTokenAsync(user, "new@example.com");

        var result = await userManager.ChangeEmailAsync(user, "new@example.com", token);

        result.Succeeded.Should().BeTrue();
        var reloaded = (await userManager.FindByIdAsync(user.Id))!;
        reloaded.Email.Should().Be("new@example.com");
        reloaded.EmailConfirmed.Should().BeTrue();
        (await userManager.GetSecurityStampAsync(reloaded)).Should().NotBe(stampBefore);
    }

    [Fact]
    public async Task A_used_change_email_token_cannot_be_used_again()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "old@example.com");
        var token = await userManager.GenerateChangeEmailTokenAsync(user, "new@example.com");
        (await userManager.ChangeEmailAsync(user, "new@example.com", token)).Succeeded.Should().BeTrue();

        // Replaying the same link: same address, same token - but the security stamp it was bound to is gone.
        var replay = await userManager.ChangeEmailAsync(user, "new@example.com", token);

        replay.Succeeded.Should().BeFalse();
        replay.Errors.Should().Contain(e => e.Code == "InvalidToken");
    }

    [Fact]
    public async Task PendingEmail_round_trips_through_RelioDbContext()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "old@example.com");

        user.PendingEmail = "new@example.com";
        (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();

        (await userManager.FindByIdAsync(user.Id))!.PendingEmail.Should().Be("new@example.com");
    }

    private static async Task<RelioUser> CreateUserAsync(UserManager<RelioUser> userManager, string email)
    {
        var user = new RelioUser { UserName = email, Email = email };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user;
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System);
    }
}
