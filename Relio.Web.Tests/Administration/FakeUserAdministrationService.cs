using Relio.Application.Administration;

namespace Relio.Web.Tests.Administration;

/// <summary>In-memory <see cref="IUserAdministrationService"/> that records what the components ask of it.</summary>
internal sealed class FakeUserAdministrationService : IUserAdministrationService
{
    public List<AccountSummary> Accounts { get; } = [];

    public List<InvitationSummary> Invitations { get; } = [];

    public List<string> Disabled { get; } = [];

    public List<string> Enabled { get; } = [];

    public List<string> InvitedEmails { get; } = [];

    public string NextToken { get; set; } = "token-1234";

    public CreateInvitationStatus NextCreateStatus { get; set; } = CreateInvitationStatus.Created;

    public DateTime Now { get; set; } = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    public Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccountSummary>>(Accounts.ToList());

    public Task<AccountChangeResult> DisableAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        Disabled.Add(userId);
        return Task.FromResult(AccountChangeResult.Succeeded);
    }

    public Task<AccountChangeResult> EnableAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        Enabled.Add(userId);
        return Task.FromResult(AccountChangeResult.Succeeded);
    }

    public Task<IReadOnlyList<InvitationSummary>> ListPendingInvitationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvitationSummary>>(Invitations.ToList());

    public Task<CreateInvitationResult> CreateInvitationAsync(string email, CancellationToken cancellationToken = default)
    {
        InvitedEmails.Add(email);
        if (NextCreateStatus != CreateInvitationStatus.Created)
        {
            return Task.FromResult(new CreateInvitationResult(NextCreateStatus, null));
        }

        var invitation = new CreatedInvitation(Guid.NewGuid(), email, NextToken, Now.AddDays(7));
        Invitations.Add(new InvitationSummary(invitation.Id, email, Now, invitation.ExpiresAtUtc));
        return Task.FromResult(new CreateInvitationResult(CreateInvitationStatus.Created, invitation));
    }

    public Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Invitations.RemoveAll(i => i.Id == invitationId) > 0);
}
