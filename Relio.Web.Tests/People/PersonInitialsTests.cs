using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PersonInitialsTests
{
    [Theory]
    [InlineData("Ada", "Lovelace", "AL")]
    [InlineData("ada", "lovelace", "AL")]
    [InlineData("Grace", null, "G")]
    [InlineData("Grace", "", "G")]
    [InlineData("  Grace ", "  Hopper ", "GH")]
    [InlineData("Élodie", "Öztürk", "ÉÖ")]
    [InlineData("élodie", "martin", "ÉM")]
    [InlineData("", "Hopper", "H")]
    [InlineData(null, null, "")]
    public void Builds_the_monogram_from_the_first_letter_of_each_name(string? first, string? last, string expected) =>
        PersonInitials.For(first, last).Should().Be(expected);

    [Fact]
    public void Never_splits_a_character_that_takes_two_UTF16_units()
    {
        // One emoji is two chars; taking just the first char would leave half a surrogate pair.
        var initials = PersonInitials.For("\U0001F600 Smile", "Face");

        initials.Should().Be("\U0001F600F");
    }

    [Fact]
    public void Keeps_a_letter_and_its_combining_accent_together()
    {
        // "e" followed by a combining acute accent is one text element.
        var initials = PersonInitials.For("élodie", null);

        initials.Should().Be("É");
    }

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("ada lovelace", "AL")]
    [InlineData("Grace", "G")]
    [InlineData("  Grace   Hopper  ", "GH")]
    [InlineData("Élodie Öztürk", "ÉÖ")]
    [InlineData("Mary Jane Watson", "MW")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Builds_the_monogram_from_display_name(string? displayName, string expected) =>
        PersonInitials.ForDisplayName(displayName).Should().Be(expected);
}
