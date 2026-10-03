using Relio.Application.Security;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>Test double for <see cref="ICurrentUser"/>, matching <c>Relio.Data.Tests</c>'s.</summary>
internal sealed class FakeCurrentUser(string? userId) : ICurrentUser
{
    public bool IsAuthenticated => userId is not null;

    public string? UserId => userId;
}
