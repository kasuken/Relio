using Microsoft.EntityFrameworkCore;
using Relio.Application.Administration;
using Relio.Data.Administration;
using Relio.Data.Configurations;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Administration;

/// <summary>
/// Proves what only a real SQL Server can for issue #19: the migrations seed the Administrator
/// role, <c>IsDisabled</c> and the invitations table exist with the right shape, and the unique
/// indexes the first-account and invitation rules lean on are enforced by the database itself.
/// </summary>
/// <remarks>
/// The database is shared by every test in the run (see <see cref="SqlServerDatabaseFixture"/>), so
/// these tests only assert schema facts and never "there is exactly one account" - that rule is
/// proven by <c>Relio.Data.Tests</c> against an isolated database.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class AdministrationSchemaSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Migrations_seed_the_Administrator_role()
    {
        await using var dbContext = fixture.CreateDbContext();

        var role = await dbContext.Roles.AsNoTracking().SingleAsync(r => r.Name == RelioRoles.Administrator);

        role.Id.Should().Be(AdministratorRoleConfiguration.AdministratorRoleId);
        role.NormalizedName.Should().Be("ADMINISTRATOR");
        role.ConcurrencyStamp.Should().Be(AdministratorRoleConfiguration.AdministratorRoleConcurrencyStamp);
    }

    [SqlServerFact]
    public async Task IsDisabled_defaults_to_false_and_round_trips()
    {
        var id = Guid.NewGuid().ToString();
        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Users.Add(NewUser(id, $"{id}@example.com"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var user = await dbContext.Users.SingleAsync(u => u.Id == id);
            user.IsDisabled.Should().BeFalse();

            user.IsDisabled = true;
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        (await readContext.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.IsDisabled).SingleAsync())
            .Should().BeTrue();
    }

    [SqlServerFact]
    public async Task An_invitation_round_trips_with_its_hash()
    {
        var token = InvitationTokens.Generate();
        var invitation = NewInvitation(token);
        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.RegistrationInvitations.Add(invitation);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var stored = await readContext.RegistrationInvitations.AsNoTracking().SingleAsync(i => i.Id == invitation.Id);

        stored.TokenHash.Should().Be(InvitationTokens.Hash(token)).And.HaveLength(64);
        stored.Email.Should().Be(invitation.Email);
        stored.ExpiresAtUtc.Should().BeCloseTo(invitation.ExpiresAtUtc, TimeSpan.FromSeconds(1));
    }

    [SqlServerFact]
    public async Task Invitation_token_hash_is_unique()
    {
        var token = InvitationTokens.Generate();
        await using var dbContext = fixture.CreateDbContext();
        dbContext.RegistrationInvitations.Add(NewInvitation(token));
        await dbContext.SaveChangesAsync();

        dbContext.RegistrationInvitations.Add(NewInvitation(token));
        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task Normalized_user_name_is_unique()
    {
        var name = $"{Guid.NewGuid():N}@example.com";
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(NewUser(Guid.NewGuid().ToString(), name));
        await dbContext.SaveChangesAsync();

        dbContext.Users.Add(NewUser(Guid.NewGuid().ToString(), name));
        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static RelioUser NewUser(string id, string email) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
    };

    private static RegistrationInvitation NewInvitation(string token)
    {
        var email = $"invited-{Guid.NewGuid():N}@example.com";
        return new RegistrationInvitation
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            TokenHash = InvitationTokens.Hash(token),
            CreatedByUserId = Guid.NewGuid().ToString(),
            CreatedAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(7),
        };
    }
}
