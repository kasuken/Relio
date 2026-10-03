using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

public class AuthenticationStateCurrentUserTests
{
    [Fact]
    public void IsAuthenticated_and_UserId_reflect_an_authenticated_principal()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-123")],
            authenticationType: "Test");
        var currentUser = new AuthenticationStateCurrentUser(new FixedAuthenticationStateProvider(identity));

        currentUser.IsAuthenticated.Should().BeTrue();
        currentUser.UserId.Should().Be("user-123");
    }

    [Fact]
    public void IsAuthenticated_is_false_and_UserId_is_null_for_an_anonymous_principal()
    {
        var anonymousIdentity = new ClaimsIdentity(); // no authenticationType => not authenticated
        var currentUser = new AuthenticationStateCurrentUser(new FixedAuthenticationStateProvider(anonymousIdentity));

        currentUser.IsAuthenticated.Should().BeFalse();
        currentUser.UserId.Should().BeNull();
    }

    /// <summary>
    /// Stands in for Blazor Server's own <c>ServerAuthenticationStateProvider</c>, which likewise
    /// returns an already-completed <see cref="Task{TResult}"/> for the circuit's lifetime - see
    /// <see cref="AuthenticationStateCurrentUser"/>'s remarks on why blocking on it is safe.
    /// </summary>
    private sealed class FixedAuthenticationStateProvider(ClaimsIdentity identity) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
