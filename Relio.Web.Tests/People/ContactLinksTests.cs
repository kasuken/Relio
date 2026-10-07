using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

/// <summary>
/// <see cref="ContactLinks"/> is the only place an href is made from a contact method. Blazor does
/// not sanitise attribute values, so these tests are what stop a stored value turning into a
/// <c>javascript:</c> link.
/// </summary>
public class ContactLinksTests
{
    [Theory]
    [InlineData("ada@example.com", "mailto:ada@example.com")]
    [InlineData("  Ada.Lovelace+news@Mail.Example.org ", "mailto:Ada.Lovelace+news@Mail.Example.org")]
    public void An_email_gets_a_mailto_link(string value, string expected)
    {
        ContactLinks.HrefFor(ContactMethodKind.Email, value, value.Trim().ToLowerInvariant()).Should().Be(expected);
    }

    [Theory]
    [InlineData("ada@example.com?subject=hi")]
    [InlineData("ada@example.com,grace@example.com")]
    [InlineData("ada@example.com&bcc=eve@example.com")]
    [InlineData("ada@example.com\"onclick=\"x")]
    [InlineData("Ada <ada@example.com>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void An_email_with_query_or_odd_characters_gets_no_link(string value)
    {
        ContactLinks.HrefFor(ContactMethodKind.Email, value, value.ToLowerInvariant()).Should().BeNull();
    }

    [Theory]
    [InlineData("+44 (7700) 900-123", "+447700900123", "tel:+447700900123")]
    [InlineData("020 7946 0958", "02079460958", "tel:02079460958")]
    [InlineData("123", "123", "tel:123")]
    public void A_phone_gets_a_tel_link_with_the_normalized_number(string value, string normalized, string expected)
    {
        ContactLinks.HrefFor(ContactMethodKind.Phone, value, normalized).Should().Be(expected);
    }

    [Theory]
    [InlineData("12", "12")]
    [InlineData("", "")]
    [InlineData("+", "+")]
    [InlineData("javascript:alert(1)", "javascript:alert(1)")]
    [InlineData("1;ext=2", "1;ext=2")]
    public void A_phone_without_a_dialable_number_gets_no_link(string value, string normalized)
    {
        ContactLinks.HrefFor(ContactMethodKind.Phone, value, normalized).Should().BeNull();
    }

    [Theory]
    [InlineData(ContactMethodKind.Address)]
    [InlineData(ContactMethodKind.Social)]
    [InlineData(ContactMethodKind.Other)]
    public void Address_social_and_other_never_get_a_link(ContactMethodKind kind)
    {
        foreach (var value in new[] { "javascript:alert(1)", "https://example.org/ada", "ada@example.com", "+447700900123" })
        {
            ContactLinks.HrefFor(kind, value, value).Should().BeNull();
        }
    }
}
