using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PersonArchiveTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // 23:30 UTC on 2 March 2026: still the 2nd in London-ish zones west of UTC+0:30, already the 3rd east of it.
    private static readonly DateTime ArchivedAt = new(2026, 3, 2, 23, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Europe/Rome", "Archived on 3 March")]
    [InlineData("UTC", "Archived on 2 March")]
    [InlineData("Pacific/Pago_Pago", "Archived on 2 March")]
    [InlineData("Pacific/Kiritimati", "Archived on 3 March")]
    public void ArchivedOn_uses_the_users_calendar_date(string timeZoneId, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        PersonArchiveText.ArchivedOn(ArchivedAt, zone, Today).Should().Be(expected);
    }

    [Fact]
    public void ArchivedOn_adds_the_year_for_an_earlier_year()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

        PersonArchiveText.ArchivedOn(ArchivedAt, zone, new DateOnly(2027, 1, 5)).Should().Be("Archived on 3 March 2026");
    }

    [Fact]
    public void ArchivedOn_treats_an_unspecified_kind_as_UTC()
    {
        // SQL Server returns datetime2 values with no kind; they are UTC instants all the same, and the
        // machine's own time zone must not leak in.
        var unspecified = DateTime.SpecifyKind(ArchivedAt, DateTimeKind.Unspecified);
        var local = DateTime.SpecifyKind(ArchivedAt, DateTimeKind.Local);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

        PersonArchiveText.ArchivedOn(unspecified, zone, Today).Should().Be("Archived on 3 March");
        PersonArchiveText.ArchivedOn(local, zone, Today).Should().Be("Archived on 3 March");
    }

    [Fact]
    public void ArchivedOn_without_a_time_says_Archived()
    {
        PersonArchiveText.ArchivedOn(null, TimeZoneInfo.Utc, Today).Should().Be("Archived");
    }

    [Fact]
    public void The_delete_title_names_the_person()
    {
        PersonArchiveText.DeleteTitle("Ada Lovelace").Should().Be("Delete Ada Lovelace?");
    }

    [Fact]
    public void The_delete_copy_says_it_cannot_be_undone_and_the_snackbars_carry_no_name()
    {
        PersonArchiveText.DeleteMessage.Should().Contain("can't be undone").And.NotContain("Undo");
        PersonArchiveText.DeleteConfirmLabel.Should().Be("Delete permanently");
        PersonArchiveText.ArchivedNoteDetail.Should().Be("Hidden from your lists and reminders. Everything you recorded is kept.");

        new[] { PersonArchiveText.Archived, PersonArchiveText.Restored, PersonArchiveText.Deleted }
            .Should().Equal("Person archived", "Person restored", "Person deleted");
    }
}
