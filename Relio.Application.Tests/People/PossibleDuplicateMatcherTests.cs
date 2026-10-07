using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

/// <summary>The rules that decide who is reported as a possible duplicate (issue #27). No database.</summary>
public class PossibleDuplicateMatcherTests
{
    private static DuplicateCandidate Person(
        string first,
        string? last = null,
        string? nickname = null,
        bool archived = false,
        string[]? emails = null,
        string[]? phones = null,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), first, last, nickname, archived, emails ?? [], phones ?? []);

    private static PossibleDuplicateQuery Query(
        string? first,
        string? last = null,
        string? nickname = null,
        params ContactMethodInput[] contacts) =>
        new() { FirstName = first, LastName = last, Nickname = nickname, ContactMethods = contacts };

    private static ContactMethodInput Email(string value) => new(null, ContactMethodKind.Email, null, value);

    private static ContactMethodInput Phone(string value) => new(null, ContactMethodKind.Phone, null, value);

    private static IReadOnlyList<PossibleDuplicate> Find(PossibleDuplicateQuery query, params DuplicateCandidate[] candidates) =>
        PossibleDuplicateMatcher.Find(DuplicateProbe.Create(query), candidates, query.ExcludePersonId);

    private static string Key(string phone) => ContactMethodRules.NormalizePhone(phone);

    [Fact]
    public void Jon_Smith_matches_John_Smith_as_a_similar_name()
    {
        var john = Person("John", "Smith");

        var result = Find(Query("Jon", "Smith"), john, Person("Ada", "Lovelace"));

        result.Should().ContainSingle();
        result[0].Id.Should().Be(john.Id);
        result[0].DisplayName.Should().Be("John Smith");
        result[0].Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
    }

    [Theory]
    [InlineData("  JOSÉ  garcía", null, "Jose", "Garcia")]
    [InlineData("Mary-Jane", "Watson", "Mary Jane", "Watson")]
    [InlineData("Conor", "O’Brien", "Conor", "OBrien")]
    [InlineData("John Smith", null, "John", "Smith")]
    public void Exact_names_match_as_the_same_name_ignoring_case_accents_and_spacing(
        string queryFirst, string? queryLast, string firstName, string lastName)
    {
        var existing = Person(firstName, lastName);

        var result = Find(Query(queryFirst, queryLast), existing);

        result.Should().ContainSingle();
        result[0].Reasons.Should().Equal(PossibleDuplicateReason.SameName);
    }

    [Theory]
    [InlineData("Ada", null, "Ada", "Lovelace")]
    [InlineData("Ada", "Lovelace", "Ada", null)]
    public void A_missing_last_name_on_either_side_is_a_similar_name(
        string queryFirst, string? queryLast, string firstName, string? lastName)
    {
        var result = Find(Query(queryFirst, queryLast), Person(firstName, lastName));

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
    }

    [Fact]
    public void A_missing_last_name_with_a_close_first_name_is_a_similar_name()
    {
        Find(Query("Jon"), Person("John")).Should().ContainSingle();
    }

    [Fact]
    public void A_candidate_nickname_matching_the_query_first_name_is_a_similar_name()
    {
        var result = Find(Query("Bob", "Smith"), Person("Robert", "Smith", nickname: "Bob"));

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
    }

    [Fact]
    public void A_query_nickname_matching_the_candidate_first_name_is_a_similar_name()
    {
        var result = Find(Query("Robert", "Smith", nickname: "Bob"), Person("Bob", "Smith"));

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
    }

    [Fact]
    public void Equal_nicknames_are_a_similar_name()
    {
        var result = Find(Query("Robert", "Smith", nickname: "Bobby"), Person("Roberto", "Smith", nickname: "Bobby"));

        result.Should().ContainSingle();
    }

    [Fact]
    public void A_nickname_with_a_different_last_name_is_not_a_match()
    {
        Find(Query("Bob", "Jones"), Person("Robert", "Smith", nickname: "Bob")).Should().BeEmpty();
    }

    [Fact]
    public void A_last_name_typo_with_the_same_first_name_is_a_similar_name()
    {
        var result = Find(Query("John", "Smyth"), Person("John", "Smith"));

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
    }

    [Fact]
    public void A_prefix_of_three_or_more_letters_is_a_similar_name()
    {
        Find(Query("Alex", "Turner"), Person("Alexander", "Turner")).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Mark", "Smith", "Mary", "Smith")]
    [InlineData("Jon", "Smith", "Jan", "Smith")]
    [InlineData("Dan", "Brown", "Don", "Brown")]
    public void Short_names_one_letter_apart_by_substitution_do_not_match(string queryFirst, string queryLast, string first, string last)
    {
        Find(Query(queryFirst, queryLast), Person(first, last)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("John", "Smith", "John", "Jones")]
    [InlineData("Jon", "Smith", "John", "Jones")]
    public void Different_last_names_do_not_match(string queryFirst, string queryLast, string first, string last)
    {
        Find(Query(queryFirst, queryLast), Person(first, last)).Should().BeEmpty();
    }

    [Fact]
    public void Two_letter_names_never_match_fuzzily()
    {
        Find(Query("Al", "Smith"), Person("Ali", "Smith")).Should().BeEmpty();
    }

    [Fact]
    public void Short_last_names_need_an_exact_match()
    {
        Find(Query("John", "Hall"), Person("John", "Hill")).Should().BeEmpty();
    }

    [Fact]
    public void Longer_names_two_edits_apart_do_not_match()
    {
        Find(Query("Marina", "Rossi"), Person("Martin", "Rossi")).Should().BeEmpty();
    }

    [Fact]
    public void Phone_numbers_sharing_fewer_than_eight_final_digits_do_not_match()
    {
        Find(Query("Zed", null, null, Phone("+44 7700 900123")), Person("Yan", phones: [Key("+44 7711 900123")])).Should().BeEmpty();
    }

    [Fact]
    public void Short_phone_numbers_need_an_exact_match()
    {
        Find(Query("Zed", null, null, Phone("0142")), Person("Yan", phones: [Key("5550142")])).Should().BeEmpty();
    }

    [Fact]
    public void Different_email_domains_do_not_match()
    {
        Find(Query("Zed", null, null, Email("ada@example.com")), Person("Yan", emails: ["ada@example.org"])).Should().BeEmpty();
    }

    [Fact]
    public void The_same_email_in_another_case_matches_regardless_of_name()
    {
        var result = Find(Query("Grace", "Hopper", null, Email("  ADA@Example.com ")), Person("Ada", "Byron", emails: ["ada@example.com"]));

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SameEmail);
    }

    [Theory]
    [InlineData("+44 7700 900123", "07700 900123")]
    [InlineData("+39 333 1234567", "3331234567")]
    public void A_phone_with_and_without_the_country_code_matches_on_the_last_eight_digits(string typed, string stored)
    {
        var result = Find(Query("Zed", null, null, Phone(typed)), Person("Yan", phones: [Key(stored)]));

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SamePhone);
    }

    [Fact]
    public void A_short_phone_number_matches_only_exactly()
    {
        var existing = Person("Yan", phones: [Key("555 0142")]);

        Find(Query("Zed", null, null, Phone("555-0142")), existing).Should().ContainSingle();
        Find(Query("Zed", null, null, Phone("+1 555-0142")), existing).Should().BeEmpty();
    }

    [Fact]
    public void Invalid_or_empty_contact_values_are_ignored()
    {
        var existing = Person("Yan", emails: ["not-an-email", string.Empty], phones: ["12", string.Empty]);

        Find(Query("Zed", null, null, Email("not-an-email"), Email("  "), Phone("12"), Phone(""), Phone("abc")), existing).Should().BeEmpty();
    }

    [Fact]
    public void Address_social_and_other_kinds_are_ignored()
    {
        var existing = Person("Yan", emails: ["ada@example.com"]);
        var query = Query("Zed", null, null,
            new ContactMethodInput(null, ContactMethodKind.Address, null, "ada@example.com"),
            new ContactMethodInput(null, ContactMethodKind.Social, null, "ada@example.com"),
            new ContactMethodInput(null, ContactMethodKind.Other, null, "ada@example.com"));

        Find(query, existing).Should().BeEmpty();
    }

    [Fact]
    public void A_blank_first_name_uses_only_contact_rules()
    {
        var byEmail = Person("Ada", "Byron", emails: ["ada@example.com"]);
        var byName = Person("", "Byron");

        var result = Find(Query("  ", "Byron", null, Email("ada@example.com")), byEmail, byName);

        result.Should().ContainSingle().Which.Id.Should().Be(byEmail.Id);
        Find(Query("", "Byron"), byEmail, byName).Should().BeEmpty();
    }

    [Fact]
    public void The_excluded_person_is_never_returned()
    {
        var john = Person("John", "Smith");
        var other = Person("John", "Smith");

        var result = Find(new PossibleDuplicateQuery { FirstName = "John", LastName = "Smith", ExcludePersonId = john.Id }, john, other);

        result.Should().ContainSingle().Which.Id.Should().Be(other.Id);
    }

    [Fact]
    public void Archived_candidates_are_returned_and_flagged()
    {
        var result = Find(Query("John", "Smith"), Person("John", "Smith", archived: true));

        result.Should().ContainSingle().Which.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Several_reasons_are_all_listed_in_order()
    {
        var existing = Person("John", "Smith", emails: ["john@example.com"], phones: [Key("+44 7700 900123")]);

        var result = Find(Query("John", "Smith", null, Phone("07700 900123"), Email("John@example.com")), existing);

        result.Should().ContainSingle().Which.Reasons.Should().Equal(
            PossibleDuplicateReason.SameName, PossibleDuplicateReason.SameEmail, PossibleDuplicateReason.SamePhone);
    }

    [Fact]
    public void Results_are_ranked_and_capped_at_five()
    {
        var strong = Person("John", "Smith");
        var withEmail = Person("Jon", "Smith", emails: ["j@example.com"]);
        var archivedExact = Person("John", "Smith", archived: true);
        var similar = Enumerable.Range(0, 8).Select(index => Person("Jon", "Smith")).ToArray();
        var all = new[] { strong, withEmail, archivedExact }.Concat(similar).ToArray();

        var result = Find(Query("John", "Smith", null, Email("j@example.com")), all);

        result.Should().HaveCount(PossibleDuplicateMatcher.MaxResults);
        // Similar name + same email (2 + 3) outranks the same name alone (4); on that tie the active
        // person comes before the archived one; the plain similar names (2) fill the rest.
        result[0].Id.Should().Be(withEmail.Id);
        result[1].Id.Should().Be(strong.Id);
        result[2].Id.Should().Be(archivedExact.Id);
        result.Select(match => match.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void On_a_tie_active_people_come_before_archived_ones()
    {
        var archived = Person("John", "Smith", archived: true);
        var active = Person("John", "Smith");

        var result = Find(Query("John", "Smith"), archived, active);

        result.Select(match => match.Id).Should().Equal(active.Id, archived.Id);
    }

    [Fact]
    public void Over_long_names_are_not_matched_by_name()
    {
        var longName = new string('a', Relio.Domain.Person.FirstNameMaxLength + 1);

        Find(Query(longName), Person(longName)).Should().BeEmpty();
    }

    [Fact]
    public void Prepared_candidates_give_the_same_results()
    {
        var candidates = new[] { Person("John", "Smith"), Person("Ada", "Lovelace", emails: ["ada@example.com"]), Person("Zoe", "Archer", archived: true) };
        var prepared = PossibleDuplicateMatcher.Prepare(candidates);

        foreach (var query in new[] { Query("Jon", "Smith"), Query("Grace", "Hopper", null, Email("ADA@example.com")), Query("Zoe", "Archer") })
        {
            var probe = DuplicateProbe.Create(query);

            PossibleDuplicateMatcher.Find(probe, prepared).Should().BeEquivalentTo(PossibleDuplicateMatcher.Find(probe, candidates));
        }

        prepared.Count.Should().Be(3);
    }

    [Fact]
    public void Matching_five_thousand_candidates_returns_the_right_people()
    {
        var candidates = Enumerable.Range(0, 5000)
            .Select(index => Person($"Name{index}", $"Family{index}"))
            .Append(Person("John", "Smith"))
            .ToArray();

        var result = Find(Query("Jon", "Smith"), candidates);

        result.Should().ContainSingle().Which.DisplayName.Should().Be("John Smith");
    }
}
