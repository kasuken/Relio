using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #29 end to end: importing people from a vCard or CSV file through the real page. The file is
/// built in each test (synthetic people, example.com addresses, numbers in the ranges reserved for
/// fiction) and sent to the hidden file input with <c>SetInputFilesAsync</c>, the same way a browser
/// delivers a chosen file, so no native file dialog is involved. Every test registers its own fresh
/// user and uses unique names; the shared demo user is never touched. Absence is only asserted after the
/// preview or the list has appeared.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class ImportPeopleTests(RelioAppFixture fixture)
{
    private static readonly Regex ImportUrl = new("/people/import$");

    private static readonly Regex AddedListUrl = new(@"/people\?sort=added$");

    private async Task<string> RegisterFreshUserAsync(IPage page)
    {
        var email = NewEmail("import");
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    private static string Unique(string name) => $"{name}{Guid.NewGuid():N}"[..(name.Length + 6)];

    private static async Task ChooseFileAsync(IPage page, string name, byte[] bytes, string mimeType) =>
        // After a file has been chosen MudBlazor leaves a second (replacement) input beside the first;
        // the last one is the one the next choice goes to.
        await page.Locator("[data-testid='import-file'] input[type=file]").Last.SetInputFilesAsync(new FilePayload
        {
            Name = name,
            MimeType = mimeType,
            Buffer = bytes,
        });

    private static Task ChooseVCardAsync(IPage page, string text) =>
        ChooseFileAsync(page, "contacts.vcf", Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\r\n")), "text/vcard");

    private static ILocator Rows(IPage page) => page.Locator("[data-testid='import-row']");

    private static ILocator Checks(IPage page) => page.Locator("input[data-testid='import-row-check']");

    private static string AndroidCards(string first1, string first2, string first3) =>
        $"""
        BEGIN:VCARD
        VERSION:2.1
        N:Testperson;{first1};;;
        TEL;CELL:+44 7700 900123
        EMAIL;HOME:{first1.ToLowerInvariant()}@example.com
        BDAY:--0415
        END:VCARD
        BEGIN:VCARD
        VERSION:2.1
        N:Testperson;{first2};;;
        EMAIL;WORK:{first2.ToLowerInvariant()}@example.org
        BDAY:19900301
        END:VCARD
        BEGIN:VCARD
        VERSION:2.1
        N:Testperson;{first3};;;
        TEL;CELL;PREF:+44 7700 900456
        END:VCARD

        """;

    [Fact]
    public async Task A_phone_vcard_imports_names_birthdays_and_contact_methods_only_after_confirming()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        string first1 = Unique("Ottilie"), first2 = Unique("Perpetua"), first3 = Unique("Quillon");

        // From the empty state, through its link.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await page.Locator("[data-testid='people-empty-import']").ClickAsync();
        await Expect(page).ToHaveURLAsync(ImportUrl);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Import people", Exact = true })).ToBeVisibleAsync();

        await ChooseVCardAsync(page, AndroidCards(first1, first2, first3));

        await Expect(Rows(page)).ToHaveCountAsync(3);
        await Expect(page.Locator("[data-testid='import-summary']")).ToContainTextAsync("3 people found. 3 selected.");
        (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId)).Should().BeEmpty("nothing is imported until the person confirms");

        await Checks(page).Nth(1).UncheckAsync();
        await Expect(page.Locator("[data-testid='import-confirm']")).ToHaveTextAsync("Import 2 people");
        (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId)).Should().BeEmpty("choosing rows saves nothing either");
        await page.Locator("[data-testid='import-confirm']").ClickAsync();

        await Expect(page).ToHaveURLAsync(AddedListUrl);
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Imported 2 people" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        var names = page.Locator("[data-testid='people-list-item-name']");
        await Expect(names).ToHaveCountAsync(2);
        await Expect(names.Filter(new() { HasTextString = first1 })).ToHaveCountAsync(1);
        await Expect(names.Filter(new() { HasTextString = first3 })).ToHaveCountAsync(1);
        await Expect(names.Filter(new() { HasTextString = first2 })).ToHaveCountAsync(0);

        var imported = await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId);
        imported.Select(p => p.FirstName).Should().BeEquivalentTo(first1, first3);

        await page.Locator("[data-testid='people-list-item']", new() { HasText = first1 }).ClickAsync();
        await Expect(page.Locator("[data-testid='person-name']")).ToContainTextAsync(first1);
        await Expect(page.Locator("[data-testid='person-birthday']")).ToContainTextAsync("15 April");
        var links = page.Locator("[data-testid='person-contact-method-link']");
        await Expect(links.Filter(new() { HasTextString = "@example.com" })).ToHaveAttributeAsync("href", $"mailto:{first1.ToLowerInvariant()}@example.com");
        await Expect(links.Filter(new() { HasTextString = "7700" })).ToHaveAttributeAsync("href", "tel:+447700900123");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_google_csv_is_mapped_previewed_and_imported()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        string first1 = Unique("Rosalind"), first2 = Unique("Tabitha");
        var csv = "First Name,Last Name,Birthday,E-mail 1 - Label,E-mail 1 - Value,Phone 1 - Label,Phone 1 - Value\r\n" +
                  $"{first1},Csvperson,1985-04-15,* Home,{first1.ToLowerInvariant()}@example.com,Mobile,+44 7700 900123\r\n" +
                  $"{first2},Csvperson,,,,,\r\n";

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/import");
        await ChooseFileAsync(page, "contacts.csv", Encoding.UTF8.GetBytes(csv), "text/csv");

        await Expect(page.Locator("[data-testid='import-preset']")).ToContainTextAsync("Google Contacts export");
        await Expect(page.Locator("[data-testid='import-column']")).ToHaveCountAsync(5);
        await page.Locator("[data-testid='import-preview']").ClickAsync();

        await Expect(Rows(page)).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid='import-row-details']").First).ToContainTextAsync("Birthday 15 April 1985");
        await page.Locator("[data-testid='import-confirm']").ClickAsync();

        await Expect(page).ToHaveURLAsync(AddedListUrl);
        await Expect(page.Locator("[data-testid='people-list-item-name']")).ToHaveCountAsync(2);
        var imported = await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId);
        var person = await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, imported.Single(p => p.FirstName == first1).Id);
        person!.Birthday.Should().Be(Birthday.Create(4, 15, 1985));
        person.ContactMethods.Select(c => (c.Kind, c.Label, c.Value)).Should().Equal(
            (ContactMethodKind.Email, "Home", $"{first1.ToLowerInvariant()}@example.com"),
            (ContactMethodKind.Phone, "Mobile", "+44 7700 900123"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Cancelling_the_preview_imports_nothing()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/import");
        await ChooseVCardAsync(page, AndroidCards(Unique("Ottilie"), Unique("Perpetua"), Unique("Quillon")));
        await Expect(Rows(page)).ToHaveCountAsync(3);

        await page.Locator("[data-testid='import-cancel']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/people$"));
        await Expect(page.Locator("[data-testid='people-empty']")).ToBeVisibleAsync();
        (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId)).Should().BeEmpty();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_possible_duplicate_starts_unselected()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        string existing = Unique("Ottilie"), other = Unique("Perpetua");
        var existingId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = existing, LastName = "Testperson" });
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/import");

        await ChooseVCardAsync(page, AndroidCards(existing, other, Unique("Quillon")));

        await Expect(Rows(page)).ToHaveCountAsync(3);
        await Expect(Checks(page).Nth(0)).Not.ToBeCheckedAsync();
        await Expect(Checks(page).Nth(1)).ToBeCheckedAsync();
        var duplicate = Rows(page).Nth(0).Locator("[data-testid='import-row-duplicate']");
        await Expect(duplicate).ToContainTextAsync("May already be in your list");
        var link = duplicate.Locator("a");
        await Expect(link).ToHaveAttributeAsync("target", "_blank");
        await Expect(link).ToHaveAttributeAsync("href", $"/people/{existingId}");
        await Expect(page.Locator("[data-testid='import-confirm']")).ToHaveTextAsync("Import 2 people");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_too_large_or_unrelated_file_is_explained()
    {
        var page = await fixture.NewPageAsync();
        await RegisterFreshUserAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/import");

        await ChooseFileAsync(page, "huge.vcf", new byte[1_100_000], "text/vcard");
        await Expect(page.Locator("[data-testid='import-file-error']")).ToContainTextAsync("larger than 1 MB");
        await Expect(page).ToHaveURLAsync(ImportUrl);

        await ChooseFileAsync(page, "photo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png");
        await Expect(page.Locator("[data-testid='import-file-error']")).ToContainTextAsync("doesn't look like a vCard or CSV");
        await Expect(page).ToHaveURLAsync(ImportUrl);
        await Expect(page.Locator("[data-testid='import-list']")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Importing_works_on_a_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.SeedPeopleAsync(fixture.App, ownerId, new SeedPerson(Unique("Seeded"), "Existing"));
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");

        // The people list header: Import and Add a person, both full width on a phone.
        await Expect(page.Locator("[data-testid='people-import']")).ToBeVisibleAsync();
        await page.Locator("[data-testid='people-import']").ClickAsync();
        await Expect(page).ToHaveURLAsync(ImportUrl);

        string first1 = Unique("Ottilie"), first2 = Unique("Perpetua");
        await ChooseVCardAsync(page, AndroidCards(first1, first2, Unique("Quillon")) +
            "BEGIN:VCARD\nVERSION:2.1\nN:Testperson;" + new string('x', 30) + ";;;\nEMAIL:" + new string('y', 60) + "@example.com\nEND:VCARD\n");
        await Expect(Rows(page)).ToHaveCountAsync(4);

        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue("no horizontal scroll");
        var checkbox = await page.Locator("[data-testid='import-row'] .rl-import-check").First.BoundingBoxAsync();
        checkbox!.Height.Should().BeGreaterThanOrEqualTo(44, "a comfortable touch target");
        var confirm = await page.Locator("[data-testid='import-confirm']").BoundingBoxAsync();
        confirm!.Width.Should().BeGreaterThan(300, "the action is full width on a phone");

        await page.Locator("[data-testid='import-row'] .rl-import-check").Nth(1).ClickAsync();
        await Expect(page.Locator("[data-testid='import-confirm']")).ToHaveTextAsync("Import 3 people");
        await page.Locator("[data-testid='import-confirm']").ClickAsync();

        await Expect(page).ToHaveURLAsync(AddedListUrl);
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Imported 3 people" })).ToBeVisibleAsync();
        (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId)).Should().HaveCount(4, "the seeded person and three imported");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
