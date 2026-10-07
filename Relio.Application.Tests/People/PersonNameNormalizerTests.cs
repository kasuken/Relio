using Relio.Application.People;

namespace Relio.Application.Tests.People;

/// <summary>The name normalizer duplicate detection (#27), import (#29) and search (#52) compare names by.</summary>
public class PersonNameNormalizerTests
{
    public static TheoryData<string> Samples =>
    [
        "José",
        "ZOË",
        "Müller",
        "Σοφία",
        "İsmail",
        "Strauß",
        "Ørsted",
        "Łukasz",
        "Æsa",
        "O'Brien",
        "O’Brien",
        "O´Brien",
        "Mary-Jane",
        "J.R.",
        "  Anne   Marie  ",
        "Ｊｏｈｎ",
        "ﬁona",
        "한국어",
        "王小明",
    ];

    [Theory]
    [InlineData("José", "jose")]
    [InlineData("ZOË", "zoe")]
    [InlineData("Müller", "muller")]
    [InlineData("İsmail", "ismail")]
    [InlineData("Renée", "renee")]
    public void Normalize_strips_accents_and_case(string value, string expected)
    {
        PersonNameNormalizer.Normalize(value).Should().Be(expected);
    }

    [Fact]
    public void Normalize_treats_a_greek_name_and_its_upper_case_as_equal()
    {
        PersonNameNormalizer.Normalize("Σοφία")
            .Should().Be(PersonNameNormalizer.Normalize("ΣΟΦΊΑ"));
    }

    [Theory]
    [InlineData("Strauß", "strauss")]
    [InlineData("Ørsted", "orsted")]
    [InlineData("Łukasz", "lukasz")]
    [InlineData("Æsa", "aesa")]
    [InlineData("Đorđe", "dorde")]
    [InlineData("Þor", "thor")]
    [InlineData("ırmak", "irmak")]
    public void Normalize_maps_letters_without_a_decomposition(string value, string expected)
    {
        PersonNameNormalizer.Normalize(value).Should().Be(expected);
    }

    [Fact]
    public void Normalize_collapses_whitespace_and_trims()
    {
        PersonNameNormalizer.Normalize("  Anne \t\r\n  Marie  ").Should().Be("anne marie");
        PersonNameNormalizer.Normalize("a b").Should().Be("a b");
    }

    [Theory]
    [InlineData("O'Brien", "obrien")]
    [InlineData("O’Brien", "obrien")]
    [InlineData("OʼBrien", "obrien")]
    [InlineData("O`Brien", "obrien")]
    [InlineData("O´Brien", "obrien")]
    [InlineData("Mary-Jane", "mary jane")]
    [InlineData("J.R.", "j r")]
    [InlineData("--Ada--", "ada")]
    [InlineData("Smith, Jr.", "smith jr")]
    public void Normalize_drops_apostrophes_and_turns_other_punctuation_into_spaces(string value, string expected)
    {
        PersonNameNormalizer.Normalize(value).Should().Be(expected);
    }

    [Fact]
    public void Normalize_folds_full_width_letters_and_ligatures()
    {
        PersonNameNormalizer.Normalize("Ｊｏｈｎ").Should().Be("john");
        PersonNameNormalizer.Normalize("ﬁona").Should().Be("fiona");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Normalize_of_null_or_blank_is_empty(string? value)
    {
        PersonNameNormalizer.Normalize(value).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Normalize_is_idempotent(string value)
    {
        var once = PersonNameNormalizer.Normalize(value);

        PersonNameNormalizer.Normalize(once).Should().Be(once);
    }

    [Theory]
    [InlineData("王小明")]
    [InlineData("한국어")]
    [InlineData("Анна")]
    public void Normalize_keeps_non_latin_names_comparable(string value)
    {
        var normalized = PersonNameNormalizer.Normalize(value);

        normalized.Should().NotBeEmpty();
        PersonNameNormalizer.Normalize(value + " ").Should().Be(normalized);
        if (value == "王小明" || value == "한국어")
        {
            normalized.Should().Be(value);
        }
    }

    [Fact]
    public void FullName_treats_a_whole_name_in_the_first_name_like_first_plus_last()
    {
        PersonNameNormalizer.FullName("John Smith", null).Should().Be(PersonNameNormalizer.FullName("John", "Smith"));
        PersonNameNormalizer.FullName("John", null).Should().Be("john");
        PersonNameNormalizer.FullName(null, null).Should().BeEmpty();
    }
}
