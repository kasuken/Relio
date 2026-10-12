using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Relio.Application.Portability;
using Relio.Domain;

namespace Relio.Application.Tests.Portability;

public sealed class UserDataJsonTests
{
    private static readonly DateTime Now = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Serialization_uses_versioned_camel_case_and_string_enums_without_identity_secrets()
    {
        await using var stream = new MemoryStream();
        await UserDataJson.SerializeAsync(stream, EmptyDocument());

        var json = Encoding.UTF8.GetString(stream.ToArray());
        using var parsed = JsonDocument.Parse(json);

        parsed.RootElement.GetProperty("formatVersion").GetInt32().Should().Be(UserDataExportDocument.CurrentFormatVersion);
        parsed.RootElement.GetProperty("profile").GetProperty("reminderEmailDelivery").GetString().Should().Be("none");
        json.Should().Contain("\"exportedAtUtc\"");
        json.Should().Contain("\"productActivity\": null");
        json.Should().NotContain("ownerId");
        json.Should().NotContain("unsubscribeToken");
        json.Should().NotContain("securityStamp");
        json.Should().NotContain("passwordHash");

        stream.Position = 0;
        var roundTrip = await UserDataJson.DeserializeAsync(stream);
        roundTrip.Should().NotBeNull();
        roundTrip!.FormatVersion.Should().Be(UserDataExportDocument.CurrentFormatVersion);
        roundTrip.Profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
    }

    [Fact]
    public async Task Deserialization_rejects_unknown_fields()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"formatVersion":1,"unexpected":"value"}"""));

        var act = async () => await UserDataJson.DeserializeAsync(stream);

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task A_document_written_before_person_links_existed_reads_as_having_no_links()
    {
        await using var stream = new MemoryStream();
        await UserDataJson.SerializeAsync(stream, EmptyDocument());
        var json = JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()))!.AsObject();
        json.Remove("personLinks").Should().BeTrue();
        await using var older = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));

        var document = await UserDataJson.DeserializeAsync(older);

        document!.PersonLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task A_person_link_round_trips_with_a_camel_case_kind()
    {
        var link = new PersonLinkSnapshot
        {
            Id = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            RelatedPersonId = Guid.NewGuid(),
            Kind = PersonLinkKind.DirectReport,
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now,
        };
        await using var stream = new MemoryStream();
        await UserDataJson.SerializeAsync(stream, EmptyDocument() with { PersonLinks = [link] });

        Encoding.UTF8.GetString(stream.ToArray()).Should().Contain("\"directReport\"");
        stream.Position = 0;
        var roundTrip = await UserDataJson.DeserializeAsync(stream);

        roundTrip!.PersonLinks.Should().ContainSingle().Which.Should().BeEquivalentTo(link);
    }

    private static UserDataExportDocument EmptyDocument() => new()
    {
        FormatVersion = UserDataExportDocument.CurrentFormatVersion,
        ExportedAtUtc = Now,
        Profile = new UserProfileSnapshot
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now,
            TimeZoneId = "UTC",
            DisplayName = null,
            OnboardingDismissed = true,
            BirthdayRemindersEnabled = true,
            DefaultBirthdayLeadDays = 0,
            ReminderEmailDelivery = ReminderEmailDelivery.None,
        },
        RelationshipTypes = [],
        Tags = [],
        People = [],
        Interactions = [],
        Notes = [],
        Reminders = [],
        ProductActivity = null,
    };
}
