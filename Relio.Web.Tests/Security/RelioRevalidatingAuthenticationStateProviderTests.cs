using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

/// <summary>
/// Covers <see cref="RelioRevalidatingAuthenticationStateProvider.SecurityStampsMatch"/>, the one
/// piece of issue #16's circuit-revalidation logic that is a pure function - everything else
/// needs a live circuit and a database, and is covered end to end by
/// <c>Relio.Web.E2ETests</c> instead.
/// </summary>
public class RelioRevalidatingAuthenticationStateProviderTests
{
    [Fact]
    public void Matching_stamps_are_valid()
    {
        RelioRevalidatingAuthenticationStateProvider.SecurityStampsMatch("stamp-1", "stamp-1").Should().BeTrue();
    }

    [Fact]
    public void A_changed_stamp_is_not_valid()
    {
        RelioRevalidatingAuthenticationStateProvider.SecurityStampsMatch("stamp-1", "stamp-2").Should().BeFalse();
    }

    [Fact]
    public void A_missing_principal_stamp_is_not_valid()
    {
        RelioRevalidatingAuthenticationStateProvider.SecurityStampsMatch(null, "stamp-1").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_user_stamp_is_never_valid_even_if_the_principal_also_has_none(string? userStamp)
    {
        RelioRevalidatingAuthenticationStateProvider.SecurityStampsMatch(userStamp, userStamp).Should().BeFalse();
    }
}
