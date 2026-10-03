using Relio.Application.Security;

namespace Relio.Data.Tests.People;

/// <summary>Test double for <see cref="ICurrentUser"/>.</summary>
internal sealed class FakeCurrentUser(string? userId) : ICurrentUser
{
    public bool IsAuthenticated => userId is not null;

    public string? UserId => userId;
}
