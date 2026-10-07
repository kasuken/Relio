using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class ContactLabelSuggestionsTests
{
    [Theory]
    [InlineData(ContactMethodKind.Email, "Personal,Work")]
    [InlineData(ContactMethodKind.Phone, "Mobile,Home,Work")]
    [InlineData(ContactMethodKind.Address, "Home,Work")]
    [InlineData(ContactMethodKind.Social, "Instagram,LinkedIn,Mastodon,Bluesky,Facebook")]
    [InlineData(ContactMethodKind.Other, "")]
    public void Blank_text_offers_every_suggestion_for_the_kind(ContactMethodKind kind, string expected)
    {
        var expectedList = expected.Length == 0 ? [] : expected.Split(',');

        ContactLabelSuggestions.For(kind, null).Should().Equal(expectedList);
        ContactLabelSuggestions.For(kind, "  ").Should().Equal(expectedList);
    }

    [Fact]
    public void Typed_text_narrows_by_prefix_ignoring_case()
    {
        ContactLabelSuggestions.For(ContactMethodKind.Phone, "mo").Should().Equal("Mobile");
        ContactLabelSuggestions.For(ContactMethodKind.Phone, " H").Should().Equal("Home");
        ContactLabelSuggestions.For(ContactMethodKind.Social, "b").Should().Equal("Bluesky");
        ContactLabelSuggestions.For(ContactMethodKind.Email, "xyz").Should().BeEmpty();
    }
}
