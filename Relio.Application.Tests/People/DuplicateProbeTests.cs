using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

/// <summary>The keys a duplicate check is made by (issue #27).</summary>
public class DuplicateProbeTests
{
    private static ContactMethodInput Email(string value) => new(null, ContactMethodKind.Email, null, value);

    private static ContactMethodInput Phone(string value) => new(null, ContactMethodKind.Phone, null, value);

    private static DuplicateProbe Probe(string first, string? last = null, string? nickname = null, params ContactMethodInput[] contacts) =>
        DuplicateProbe.Create(new PossibleDuplicateQuery { FirstName = first, LastName = last, Nickname = nickname, ContactMethods = contacts });

    [Fact]
    public void Key_changes_when_a_name_email_or_phone_changes_but_not_for_case_or_spacing()
    {
        var baseline = Probe("John", "Smith", null, Email("john@example.com"), Phone("+44 7700 900123"));

        Probe("  JOHN ", "smith", null, Email(" John@Example.com "), Phone("+44 7700-900123")).Key.Should().Be(baseline.Key);
        Probe("Jon", "Smith", null, Email("john@example.com"), Phone("+44 7700 900123")).Key.Should().NotBe(baseline.Key);
        Probe("John", "Smith", null, Email("other@example.com"), Phone("+44 7700 900123")).Key.Should().NotBe(baseline.Key);
        Probe("John", "Smith", null, Email("john@example.com"), Phone("+44 7700 900124")).Key.Should().NotBe(baseline.Key);
        Probe("John", "Smith", "Johnny", Email("john@example.com"), Phone("+44 7700 900123")).Key.Should().NotBe(baseline.Key);
    }

    [Fact]
    public void Key_does_not_depend_on_the_order_of_contact_methods()
    {
        Probe("A", null, null, Email("a@x.com"), Email("b@x.com")).Key.Should().Be(Probe("A", null, null, Email("b@x.com"), Email("a@x.com")).Key);
    }

    [Fact]
    public void NameKey_ignores_contact_methods()
    {
        Probe("John", "Smith").NameKey.Should().Be(Probe("john", " SMITH ", null, Email("a@b.com"), Phone("12345")).NameKey);
        Probe("John", "Smith").NameKey.Should().NotBe(Probe("John", "Smith", "Johnny").NameKey);
    }

    [Fact]
    public void FromProfile_copies_names_nickname_contact_methods_and_the_excluded_id()
    {
        var methods = new[] { Email("a@b.com") };
        var id = Guid.NewGuid();
        var request = new CreatePersonRequest { FirstName = "Ada", LastName = "Byron", Nickname = "Countess", ContactMethods = methods };

        var query = PossibleDuplicateQuery.FromProfile(request, id);

        query.FirstName.Should().Be("Ada");
        query.LastName.Should().Be("Byron");
        query.Nickname.Should().Be("Countess");
        query.ContactMethods.Should().Equal(methods);
        query.ExcludePersonId.Should().Be(id);
        PossibleDuplicateQuery.FromProfile(request).ExcludePersonId.Should().BeNull();
    }

    [Fact]
    public void Keys_are_capped_at_twenty()
    {
        var emails = Enumerable.Range(0, 30).Select(index => Email($"p{index}@example.com")).ToArray();
        var phones = Enumerable.Range(0, 30).Select(index => Phone($"555{index:D4}")).ToArray();

        var probe = Probe("Ada", null, null, [.. emails, .. phones]);

        probe.EmailKeys.Should().HaveCount(DuplicateProbe.MaxKeys);
        probe.PhoneKeys.Should().HaveCount(DuplicateProbe.MaxKeys);
    }

    [Fact]
    public void Keys_are_distinct_and_unusable_values_are_dropped()
    {
        var probe = Probe("Ada", null, null,
            Email("a@b.com"), Email("A@B.com"), Email("nope"), Phone("555 0142"), Phone("5550142"), Phone("12"),
            new ContactMethodInput(null, ContactMethodKind.Address, null, "a@b.com"));

        probe.EmailKeys.Should().Equal("a@b.com");
        probe.PhoneKeys.Should().Equal("5550142");
    }

    [Fact]
    public void Phone_suffixes_are_the_last_eight_digits_or_the_whole_short_number()
    {
        var probe = Probe("Ada", null, null, Phone("+44 7700 900123"), Phone("07700 900123"), Phone("555 0142"));

        probe.PhoneSuffixes.Should().BeEquivalentTo(["00900123", "5550142"]);
    }

    [Fact]
    public void Over_long_names_turn_name_matching_off()
    {
        var probe = Probe(new string('a', Relio.Domain.Person.FirstNameMaxLength + 1), "Smith");

        probe.First.Should().BeEmpty();
        probe.FullName.Should().BeEmpty();
    }
}
