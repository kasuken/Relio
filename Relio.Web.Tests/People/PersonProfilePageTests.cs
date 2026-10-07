using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Components.People;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class PersonProfilePageTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // The profile reads the user's time zone (for the archive date) and hosts a menu, a dialog and
    // snackbars, so every context carries the providers that render them.
    private static BunitContext CreateContext(FakePeopleService people, string timeZoneId = "Europe/Rome") =>
        CreateContext(people, out _, timeZoneId);

    private static BunitContext CreateContext(
        FakePeopleService people, out ProfileProviders providers, string timeZoneId = "Europe/Rome")
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService(timeZoneId, Today));
        providers = new ProfileProviders(
            context.Render<MudPopoverProvider>(),
            context.Render<MudDialogProvider>(),
            context.Render<MudSnackbarProvider>());
        return context;
    }

    private sealed record ProfileProviders(
        IRenderedComponent<MudPopoverProvider> Popovers,
        IRenderedComponent<MudDialogProvider> Dialogs,
        IRenderedComponent<MudSnackbarProvider> Snackbars);

    /// <summary>Opens the "More" menu and returns the item with <paramref name="testId"/> from the popover.</summary>
    private static AngleSharp.Dom.IElement MenuItem(
        IRenderedComponent<PersonProfile> cut, ProfileProviders providers, string testId)
    {
        OpenMenu(cut, providers);
        return providers.Popovers.Find($"[data-testid='{testId}']");
    }

    private static void OpenMenu(IRenderedComponent<PersonProfile> cut, ProfileProviders providers)
    {
        if (providers.Popovers.FindAll(".mud-menu-item").Count == 0)
        {
            cut.Find("[data-testid='person-actions'] button").Click();
        }
    }

    [Fact]
    public async Task Shows_not_found_when_the_service_returns_null()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, Guid.NewGuid()));

        cut.Find("[data-testid='person-not-found']").TextContent.Should().Contain("This person isn't in your list");
        cut.Markup.Should().Contain("The link may be wrong, or they were removed.");
        cut.FindAll("[data-testid='person-name']").Should().BeEmpty();
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task The_not_found_action_goes_back_to_the_people_list()
    {
        await using var context = CreateContext(new FakePeopleService());
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, Guid.NewGuid()));

        cut.Find("[data-testid='person-not-found'] button").Click();

        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/people");
    }

    [Fact]
    public async Task Shows_the_saved_details_including_a_yearless_birthday()
    {
        var friend = new RelationshipType { Name = "Friend" };
        var person = new Person
        {
            FirstName = "Liam",
            LastName = "Chen",
            Nickname = "Lee",
            RelationshipType = friend,
            RelationshipTypeId = friend.Id,
            BirthdayDay = 14,
            BirthdayMonth = 3,
            HowWeMet = "At the climbing wall.",
            Details = "Line one.\nLine two.",
        };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Liam Chen");
        cut.Find("h1").TextContent.Should().Be("Liam Chen");
        cut.Find("[data-testid='person-relationship']").TextContent.Should().Be("Friend");
        cut.Find("[data-testid='person-nickname']").TextContent.Should().Be("Lee");
        cut.Find("[data-testid='person-birthday']").TextContent.Should().Be("14 March");
        cut.Find("[data-testid='person-how-we-met']").TextContent.Should().Be("At the climbing wall.");
        cut.Find("[data-testid='person-details']").TextContent.Should().Be("Line one.\nLine two.");
        cut.Find("[data-testid='person-details']").ClassList.Should().Contain("rl-multiline");
        cut.Find("[data-testid='person-back']").GetAttribute("href").Should().Be("/people");
    }

    [Fact]
    public async Task Shows_a_birthday_with_its_year()
    {
        var person = new Person { FirstName = "Ada", BirthdayDay = 10, BirthdayMonth = 12, BirthdayYear = 1815 };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-birthday']").TextContent.Should().Be("10 December 1815");
    }

    [Fact]
    public async Task Leaves_out_everything_that_is_not_set()
    {
        var person = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Grace");
        foreach (var field in new[] { "person-relationship", "person-nickname", "person-birthday", "person-how-we-met", "person-details" })
        {
            cut.FindAll($"[data-testid='{field}']").Should().BeEmpty($"{field} is not set");
        }
    }

    [Fact]
    public async Task Renders_user_text_as_text_not_markup()
    {
        var person = new Person { FirstName = "Ada", Details = "<script>alert(1)</script><b>bold</b>" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-details']").TextContent.Should().Be("<script>alert(1)</script><b>bold</b>");
        cut.FindAll("script").Should().BeEmpty();
        cut.FindAll("b").Should().BeEmpty();
    }

    private static ContactMethod Contact(ContactMethodKind kind, string value, string? label, int sortOrder, string? normalized = null) =>
        new()
        {
            Kind = kind,
            Value = value,
            Label = label,
            SortOrder = sortOrder,
            NormalizedValue = normalized ?? ContactMethodRules.ToNormalizedValue(kind, value),
        };

    [Fact]
    public async Task Shows_contact_methods_in_order_with_their_headings_and_links()
    {
        var person = new Person
        {
            FirstName = "Ada",
            ContactMethods =
            {
                Contact(ContactMethodKind.Address, "12 Example Square\nLondon", "Home", 3),
                Contact(ContactMethodKind.Email, "Ada@Example.com", "Work", 0),
                Contact(ContactMethodKind.Phone, "+44 (7700) 900-123", "Mobile", 1),
                Contact(ContactMethodKind.Social, "@ada.l", null, 2),
            },
        };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        var rows = cut.FindAll("[data-testid='person-contact-method']");
        rows.Select(r => r.QuerySelector("[data-testid='person-contact-method-heading']")!.TextContent)
            .Should().Equal("Email · Work", "Phone · Mobile", "Social", "Address · Home");

        var email = rows[0].QuerySelector("a[data-testid='person-contact-method-link']")!;
        email.GetAttribute("href").Should().Be("mailto:Ada@Example.com");
        email.TextContent.Should().Be("Ada@Example.com");
        var phone = rows[1].QuerySelector("a[data-testid='person-contact-method-link']")!;
        phone.GetAttribute("href").Should().Be("tel:+447700900123");
        phone.TextContent.Should().Be("+44 (7700) 900-123", "the number is shown the way it was written");

        rows[2].QuerySelector("a")!.Should().BeNull("a social handle is plain text");
        rows[2].QuerySelector("[data-testid='person-contact-method-value']")!.TextContent.Should().Be("@ada.l");
        rows[3].QuerySelector("a").Should().BeNull();
        var address = rows[3].QuerySelector("[data-testid='person-contact-method-value']")!;
        address.TextContent.Should().Be("12 Example Square\nLondon");
        address.ParentElement!.ClassList.Should().Contain("rl-multiline");
    }

    [Fact]
    public async Task A_stored_value_can_never_become_a_script_link()
    {
        var person = new Person
        {
            FirstName = "Ada",
            ContactMethods =
            {
                Contact(ContactMethodKind.Social, "javascript:alert(1)", null, 0),
                Contact(ContactMethodKind.Other, "javascript:alert(2)", null, 1),
                Contact(ContactMethodKind.Email, "javascript:alert(3)", null, 2),
                Contact(ContactMethodKind.Phone, "javascript:alert(4)", null, 3),
            },
        };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.FindAll("a[href^='javascript']").Should().BeEmpty();
        cut.FindAll("[data-testid='person-contact-method-link']").Should().BeEmpty();
        cut.FindAll("[data-testid='person-contact-method-value']").Should().HaveCount(4);
    }

    [Fact]
    public async Task Shows_tags_as_chips_in_name_order()
    {
        var person = new Person { FirstName = "Ada", Tags = { new Tag { Name = "Mentor" }, new Tag { Name = "chess" } } };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.FindAll("[data-testid='person-tags'] [data-testid='person-tag']").Select(t => t.TextContent.Trim())
            .Should().Equal("chess", "Mentor");
    }

    [Fact]
    public async Task Shows_no_contact_rows_or_tag_list_when_there_are_none()
    {
        var person = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.FindAll("[data-testid='person-contact-method']").Should().BeEmpty();
        cut.FindAll("[data-testid='person-tags']").Should().BeEmpty();
    }

    [Fact]
    public async Task Links_to_the_edit_page()
    {
        var person = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        var edit = cut.Find("[data-testid='person-edit']");
        edit.GetAttribute("href").Should().Be($"/people/{person.Id}/edit");
        edit.TextContent.Trim().Should().Be("Edit");
    }

    [Fact]
    public async Task Does_not_offer_to_edit_a_person_that_is_not_there()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, Guid.NewGuid()));

        cut.FindAll("[data-testid='person-edit']").Should().BeEmpty();
    }

    [Fact]
    public async Task Loads_the_new_person_when_the_route_parameter_changes()
    {
        var first = new Person { FirstName = "Ada" };
        var second = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.AddRange([first, second]);
        await using var context = CreateContext(people);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, first.Id));
        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Ada");

        cut.Render(parameters => parameters.Add(p => p.PersonId, second.Id));

        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Grace");
    }

    private static Person ActivePerson() => new() { FirstName = "Ada", LastName = "Lovelace" };

    private static Person ArchivedPerson(DateTime? archivedAtUtc = null) =>
        new()
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            IsArchived = true,
            ArchivedAtUtc = archivedAtUtc ?? new DateTime(2026, 3, 2, 23, 30, 0, DateTimeKind.Utc),
        };

    private static string[] MenuLabels(ProfileProviders providers) =>
        providers.Popovers.FindAll(".mud-menu-item").Select(item => item.TextContent.Trim()).ToArray();

    [Fact]
    public async Task The_actions_menu_offers_archive_and_delete_for_an_active_person()
    {
        var person = ActivePerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-actions']").TextContent.Should().Contain("Edit").And.Contain("More");
        OpenMenu(cut, providers);

        MenuLabels(providers).Should().Equal("Archive", "Delete");
        providers.Popovers.FindAll("[data-testid='person-restore-menu']").Should().BeEmpty();
        cut.FindAll("[data-testid='person-archived']").Should().BeEmpty("an active person has no archived note");
        cut.Find(".rl-avatar").ClassList.Should().NotContain("rl-avatar-archived");
    }

    [Fact]
    public async Task The_actions_menu_offers_restore_and_delete_for_an_archived_person()
    {
        var person = ArchivedPerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        OpenMenu(cut, providers);

        MenuLabels(providers).Should().Equal("Restore", "Delete");
        providers.Popovers.FindAll("[data-testid='person-archive']").Should().BeEmpty();
        cut.Find(".rl-avatar").ClassList.Should().Contain("rl-avatar-archived");
    }

    [Fact]
    public async Task Archiving_shows_the_archived_note_and_says_Person_archived()
    {
        var person = ActivePerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        MenuItem(cut, providers, "person-archive").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-archived']").TextContent.Should().Contain(PersonArchiveText.ArchivedNoteDetail));
        people.Archived.Should().Equal(person.Id);
        providers.Snackbars.WaitForAssertion(() => providers.Snackbars.Markup.Should().Contain("Person archived"));
        providers.Snackbars.Markup.Should().NotContain("Ada", "a snackbar never carries the person's name");
        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Ada Lovelace", "the profile stays on screen");
        cut.Find("[data-testid='person-restore']").TextContent.Trim().Should().Be("Restore");
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task An_archived_person_shows_the_archive_date_in_the_users_time_zone()
    {
        // 23:30 UTC on 2 March is already 3 March in Rome.
        var person = ArchivedPerson(new DateTime(2026, 3, 2, 23, 30, 0, DateTimeKind.Unspecified));
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-archived-date']").TextContent.Should().Be("Archived on 3 March");
    }

    [Fact]
    public async Task An_archived_person_without_an_archive_time_just_says_Archived()
    {
        var person = ArchivedPerson();
        person.ArchivedAtUtc = null;
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-archived-date']").TextContent.Should().Be("Archived");
    }

    [Fact]
    public async Task Restoring_from_the_note_hides_it_and_says_Person_restored()
    {
        var person = ArchivedPerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-restore']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='person-archived']").Should().BeEmpty());
        people.Restored.Should().Equal(person.Id);
        providers.Snackbars.WaitForAssertion(() => providers.Snackbars.Markup.Should().Contain("Person restored"));
        providers.Snackbars.Markup.Should().NotContain("Ada");
        cut.Find(".rl-avatar").ClassList.Should().NotContain("rl-avatar-archived");
    }

    [Fact]
    public async Task Restoring_from_the_menu_works_too()
    {
        var person = ArchivedPerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        MenuItem(cut, providers, "person-restore-menu").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='person-archived']").Should().BeEmpty());
        people.Restored.Should().Equal(person.Id);
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_naming_the_person_with_a_destructive_button()
    {
        var person = ActivePerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        MenuItem(cut, providers, "person-delete").Click();

        providers.Dialogs.WaitForAssertion(() => providers.Dialogs.Markup.Should().Contain("Delete Ada Lovelace?"));
        providers.Dialogs.Markup.Should().Contain("It can't be undone.");
        var confirm = providers.Dialogs.Find("[data-testid='confirm-dialog-confirm']");
        confirm.TextContent.Trim().Should().Be("Delete permanently");
        confirm.ClassList.Should().Contain("mud-button-filled-error");
        people.Deleted.Should().BeEmpty("nothing is deleted before the confirmation");
    }

    [Fact]
    public async Task Cancelling_the_delete_calls_nothing_and_keeps_the_profile()
    {
        var person = ActivePerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uriBefore = navigation.Uri;
        MenuItem(cut, providers, "person-delete").Click();
        providers.Dialogs.WaitForAssertion(() => providers.Dialogs.FindAll("[data-testid='confirm-dialog-cancel']").Should().ContainSingle());

        providers.Dialogs.Find("[data-testid='confirm-dialog-cancel']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-actions-menu']").HasAttribute("disabled").Should().BeFalse());
        people.Deleted.Should().BeEmpty();
        navigation.Uri.Should().Be(uriBefore);
        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Ada Lovelace");
        providers.Snackbars.Markup.Should().NotContain("Person deleted");
    }

    [Fact]
    public async Task Confirming_the_delete_navigates_to_people_replacing_the_history_entry_and_says_Person_deleted()
    {
        var person = ActivePerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));
        MenuItem(cut, providers, "person-delete").Click();
        providers.Dialogs.WaitForAssertion(() => providers.Dialogs.FindAll("[data-testid='confirm-dialog-confirm']").Should().ContainSingle());

        providers.Dialogs.Find("[data-testid='confirm-dialog-confirm']").Click();

        var navigation = context.Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => people.Deleted.Should().Equal(person.Id));
        cut.WaitForAssertion(() => navigation.Uri.Should().EndWith("/people"));
        var entry = ((Bunit.TestDoubles.BunitNavigationManager)navigation).History.First();
        entry.Uri.Should().EndWith("/people");
        entry.Options.ReplaceHistoryEntry.Should().BeTrue("Back from the list must not land on a profile that is gone");
        providers.Snackbars.WaitForAssertion(() => providers.Snackbars.Markup.Should().Contain("Person deleted"));
        providers.Snackbars.Markup.Should().NotContain("Ada");
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("restore")]
    [InlineData("delete")]
    public async Task An_action_on_a_person_removed_elsewhere_shows_not_found(string action)
    {
        var person = action == "restore" ? ArchivedPerson() : ActivePerson();
        var people = new FakePeopleService { ArchiveResult = false, RestoreResult = false, DeleteResult = false };
        people.Known.Add(person);
        await using var context = CreateContext(people, out var providers);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        switch (action)
        {
            case "archive":
                MenuItem(cut, providers, "person-archive").Click();
                break;
            case "restore":
                cut.Find("[data-testid='person-restore']").Click();
                break;
            default:
                MenuItem(cut, providers, "person-delete").Click();
                providers.Dialogs.WaitForAssertion(() => providers.Dialogs.FindAll("[data-testid='confirm-dialog-confirm']").Should().ContainSingle());
                providers.Dialogs.Find("[data-testid='confirm-dialog-confirm']").Click();
                break;
        }

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-not-found']").TextContent.Should().Contain("This person isn't in your list"));
        cut.FindAll("[data-testid='person-actions']").Should().BeEmpty();
        providers.Snackbars.Markup.Should().NotContain("Person archived").And.NotContain("Person restored").And.NotContain("Person deleted");
    }

    [Fact]
    public async Task The_archived_state_has_exactly_one_h1()
    {
        var person = ArchivedPerson();
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.FindAll("h1").Should().ContainSingle();
        cut.Find("[data-testid='person-archived']").QuerySelector("h1, h2, h3").Should().BeNull("the note is not a heading");
    }
}
