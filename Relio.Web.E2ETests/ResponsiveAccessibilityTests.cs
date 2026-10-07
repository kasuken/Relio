using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Data.Time;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class ResponsiveAccessibilityTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData(360, 800)]
    [InlineData(768, 1024)]
    [InlineData(1440, 900)]
    public async Task Signed_in_mvp_pages_fit_phone_tablet_and_desktop(
        int width,
        int height)
    {
        var viewport = new ViewportSize { Width = width, Height = height };
        var page = await fixture.NewPageAsync(viewport);
        IPage? adminPage = null;

        try
        {
            var email = NewMaximumLengthEmail();
            await RegisterAsync(page, email, StrongPassword);
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Get started", Exact = true }))
                .ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, "/onboarding", width);

            var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
            var today = await GetTodayAsync(fixture.App, ownerId);
            var firstName = new string('F', 80) + Guid.NewGuid().ToString("N")[..20];
            var personId = await PeopleTestHelpers.CreatePersonAsync(
                fixture.App,
                ownerId,
                new CreatePersonRequest
                {
                    FirstName = firstName,
                    LastName = new string('L', Person.LastNameMaxLength),
                    BirthdayDay = today.Day,
                    BirthdayMonth = today.Month,
                });
            var otherPersonId = await PeopleTestHelpers.CreatePersonAsync(
                fixture.App,
                ownerId,
                new CreatePersonRequest
                {
                    FirstName = $"Merge{Guid.NewGuid():N}"[..15],
                    LastName = new string('M', Person.LastNameMaxLength),
                });

            await PeopleTestHelpers.CreateNoteAsync(
                fixture.App,
                ownerId,
                personId,
                $"A note with a long uninterrupted word: {new string('n', 1_800)}",
                isPinned: true);
            await PeopleTestHelpers.SeedInteractionsAsync(fixture.App, ownerId, personId, today, 55);
            var upcomingReminderId = await PeopleTestHelpers.CreateReminderAsync(
                fixture.App,
                ownerId,
                personId,
                new string('R', Reminder.TitleMaxLength),
                today.AddDays(3));
            await PeopleTestHelpers.CreateReminderAsync(
                fixture.App,
                ownerId,
                personId,
                $"Overdue{new string('O', 120)}",
                today.AddDays(-2));

            await VisitInteractivePageAsync(
                page,
                "/",
                "[data-testid='dashboard-overview']",
                width);
            await Expect(page.GetByTestId("appbar-log-interaction"))
                .ToHaveAttributeAsync("aria-label", "Log an interaction");

            var skipLink = page.GetByRole(AriaRole.Link, new()
            {
                Name = "Skip to main content",
                Exact = true,
            });
            await Expect(skipLink).ToHaveAttributeAsync("href", "#main-content");

            await VisitInteractivePageAsync(
                page,
                "/people",
                "[data-testid='people-list']",
                width);
            await VisitInteractivePageAsync(
                page,
                "/people/new",
                "[data-testid='person-form-save']",
                width);
            await VisitInteractivePageAsync(
                page,
                $"/people/{personId}",
                "[data-testid='person-timeline']",
                width);
            await Expect(page.GetByTestId("person-reminders-list")).ToBeVisibleAsync();
            await Expect(page.GetByTestId($"person-reminder-snooze-{upcomingReminderId}"))
                .ToHaveAttributeAsync("aria-label", "Snooze reminder");
            var personSnooze = page.GetByTestId($"person-reminder-snooze-{upcomingReminderId}");
            var personSnoozeSize = await personSnooze.EvaluateAsync<double[]>(
                "element => { const rect = element.getBoundingClientRect(); return [rect.width, rect.height]; }");
            personSnoozeSize[0].Should().BeGreaterThanOrEqualTo(width <= 599 ? 44 : 24);
            personSnoozeSize[1].Should().BeGreaterThanOrEqualTo(width <= 599 ? 44 : 24);
            await personSnooze.ClickAsync();
            var snoozeDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(snoozeDialog.GetByTestId("snooze-save-custom")).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, "person snooze dialog", width);
            await page.Keyboard.PressAsync("Escape");
            await Expect(snoozeDialog).ToBeHiddenAsync();
            await Expect(personSnooze).ToBeFocusedAsync();
            await Expect(page.Locator("[data-testid='timeline-entry']")).ToHaveCountAsync(50);
            await Expect(page.GetByTestId("timeline-load-more")).ToBeVisibleAsync();
            await VisitInteractivePageAsync(
                page,
                $"/people/{personId}/edit",
                "[data-testid='person-form-save']",
                width);
            await VisitInteractivePageAsync(
                page,
                $"/people/{personId}/merge?with={otherPersonId}",
                "[data-testid='merge-heads']",
                width);

            await VisitInteractivePageAsync(
                page,
                "/people/import",
                "[data-testid='import-choose']",
                width);
            await page.Locator("[data-testid='import-file'] input[type=file]").Last.SetInputFilesAsync(
                new FilePayload
                {
                    Name = "responsive.csv",
                    MimeType = "text/csv",
                    Buffer = Encoding.UTF8.GetBytes(
                        "First Name,Last Name,Email\r\nMarigold,Example,marigold@example.com\r\n"),
                });
            await page.GetByTestId("import-map").WaitForAsync();
            await AssertNoHorizontalOverflowAsync(page, "CSV column mapping", width);
            await page.GetByTestId("import-preview").ClickAsync();
            await Expect(page.GetByTestId("import-preview-list")).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, "CSV import preview", width);

            await VisitInteractivePageAsync(
                page,
                "/reminders",
                "[data-testid='reminders-active-list']",
                width);
            await Expect(page.GetByTestId($"reminder-snooze-{upcomingReminderId}"))
                .ToHaveAttributeAsync("aria-label", "Snooze reminder");

            var todayBeforeReminderDialog = await GetTodayAsync(fixture.App, ownerId);
            var addReminderButton = page.GetByTestId("add-reminder-button");
            await page.Keyboard.PressAsync("Tab");
            await Expect(addReminderButton).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
            var reminderDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(reminderDialog).ToBeVisibleAsync();
            var dueDateInput = reminderDialog.GetByLabel("Due date", new() { Exact = true });
            var selectedDueDateText = await dueDateInput.InputValueAsync();
            DateOnly.TryParseExact(
                    selectedDueDateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var selectedDueDate)
                .Should()
                .BeTrue();
            var todayAfterReminderDialog = await GetTodayAsync(fixture.App, ownerId);
            (selectedDueDate == todayBeforeReminderDialog || selectedDueDate == todayAfterReminderDialog)
                .Should()
                .BeTrue("a new reminder defaults to the user's current calendar date");
            await AssertNoHorizontalOverflowAsync(page, "new reminder dialog", width);
            await page.Keyboard.PressAsync("Escape");
            await Expect(reminderDialog).ToBeHiddenAsync();
            (await addReminderButton.EvaluateAsync<bool>(
                "element => element === document.activeElement"))
                .Should().BeTrue("closing a dialog with Escape returns focus to its opener");

            await VisitInteractivePageAsync(
                page,
                "/difficult-moments",
                ".rl-empty",
                width);
            await VisitInteractivePageAsync(
                page,
                "/interactions/new",
                "[data-testid='quick-log-person-picker']",
                width);
            var quickLogPicker = page.GetByTestId("quick-log-person-picker");
            await quickLogPicker.FillAsync(firstName[..12]);
            await page.GetByRole(AriaRole.Option).First.ClickAsync();
            await Expect(page.GetByTestId("interaction-editor")).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, "quick-log editor", width);
            await VisitInteractivePageAsync(
                page,
                "/settings",
                "[data-testid='settings-display-name-save']",
                width);
            await page.GetByTestId("settings-current-email").WaitForAsync();
            await page.GetByTestId("settings-2fa-state").WaitForAsync();
            await VisitInteractivePageAsync(
                page,
                "/settings/relationship-types",
                "[data-testid='relationship-type-list']",
                width);
            var renameType = page.GetByTestId("relationship-type-rename").First;
            await Expect(renameType).ToHaveAttributeAsync("aria-label", "Rename Family");
            if (width <= 599)
            {
                var renameTarget = await renameType.BoundingBoxAsync()
                    ?? throw new InvalidOperationException("The rename action should be visible on a phone.");
                renameTarget.Width.Should().BeGreaterThanOrEqualTo(44);
                renameTarget.Height.Should().BeGreaterThanOrEqualTo(44);
            }

            await TabUntilFocusedAsync(page, renameType);
            await page.Keyboard.PressAsync("Enter");
            var renameDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(renameDialog.GetByTestId("label-name-dialog-field")).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, "relationship type rename dialog", width);
            await page.Keyboard.PressAsync("Escape");
            await Expect(renameDialog).ToBeHiddenAsync();
            await Expect(renameType).ToBeFocusedAsync();

            await VisitInteractivePageAsync(
                page,
                "/settings/tags",
                "[data-testid='tags-empty']",
                width);
            await VisitInteractivePageAsync(
                page,
                "/settings/reminders",
                "[data-testid='settings-reminder-delivery-group']",
                width);

            await CheckStaticPageAsync(page, "/Account/Manage/ChangePassword", width);
            await CheckStaticPageAsync(page, "/Account/Manage/Email", width);
            await CheckStaticPageAsync(page, "/Account/Manage/TwoFactorAuthentication", width);
            await CheckStaticPageAsync(page, "/Account/Manage/EnableAuthenticator", width);
            await Expect(page.GetByTestId("manage-2fa-qr")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("manage-2fa-shared-key")).ToBeVisibleAsync();

            var twoFactor = await TwoFactorTestHelpers.EnableTwoFactorAsync(fixture.App, email);
            await SignOutAsync(page);
            await TwoFactorTestHelpers.SignInWithTwoFactorAsync(
                page,
                new TwoFactorTestHelpers.TwoFactorUser(
                    email,
                    StrongPassword,
                    twoFactor.Key,
                    twoFactor.RecoveryCodes));

            await CheckStaticPageAsync(page, "/Account/Manage/TwoFactorAuthentication", width);
            await CheckStaticPageAsync(page, "/Account/Manage/GenerateRecoveryCodes", width);
            await page.GetByTestId("manage-2fa-codes-password").FillAsync(StrongPassword);
            await page.GetByTestId("manage-2fa-codes-submit").ClickAsync();
            await Expect(page.GetByTestId("recovery-codes")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("recovery-code")).ToHaveCountAsync(10);
            await AssertNoHorizontalOverflowAsync(page, "generated recovery codes", width);
            await CheckStaticPageAsync(page, "/Account/Manage/Disable2fa", width);
            await CheckStaticPageAsync(page, "/Account/Manage/ResetAuthenticator", width);

            adminPage = await fixture.NewPageAsync(viewport);
            await RelioAppFixture.SignInAsDemoAsync(adminPage);
            await VisitInteractivePageAsync(
                adminPage,
                "/admin/users",
                "[data-testid='admin-accounts']",
                width);
            await Expect(adminPage.Locator("[data-testid='admin-accounts']"))
                .ToHaveCountAsync(1);
            await Expect(adminPage.Locator("[data-testid='admin-accounts'] thead th").Nth(2))
                .ToHaveTextAsync("Actions");
            await Expect(adminPage.Locator("[data-testid='admin-accounts'] tbody tr td:last-child").First)
                .ToHaveAttributeAsync("data-label", "Actions");
        }
        finally
        {
            if (adminPage is not null)
            {
                await RelioAppFixture.ClosePageAsync(adminPage);
            }

            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Theory]
    [InlineData(360, 800)]
    [InlineData(768, 1024)]
    [InlineData(1440, 900)]
    public async Task Anonymous_account_pages_fit_at_supported_widths(int width, int height)
    {
        var page = await fixture.NewPageAsync(new ViewportSize { Width = width, Height = height });

        try
        {
            var paths = new[]
            {
                "/Account/Login",
                "/Account/Register",
                "/Account/RegisterConfirmation",
                "/Account/ForgotPassword",
                "/Account/ForgotPasswordConfirmation",
                "/Account/ResetPassword?userId=unknown&code=invalid",
                "/Account/ResetPasswordConfirmation",
                "/Account/ConfirmEmail?userId=unknown&code=invalid",
                "/Account/ConfirmEmailChange?userId=unknown&code=invalid",
                "/Account/LoginWith2fa?returnUrl=%2F&rememberMe=false",
                "/Account/LoginWithRecoveryCode?returnUrl=%2F",
                "/Account/AccessDenied",
            };

            foreach (var path in paths)
            {
                await page.GotoAsync(path);
                await Expect(page.Locator("h1")).ToHaveCountAsync(1);

                if (path == "/Account/Register")
                {
                    var longEmail =
                        $"{new string('e', 63)}@{new string('d', 63)}.{new string('c', 60)}.org";
                    await page.Locator("[data-testid='register-email']").FillAsync(longEmail);
                }

                await AssertNoHorizontalOverflowAsync(page, path, width);
            }
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Keyboard_skip_link_moves_focus_to_main_content()
    {
        var page = await fixture.NewPageAsync(new ViewportSize { Width = 360, Height = 800 });

        try
        {
            var email = NewEmail("skip-link");
            await RegisterAsync(page, email, StrongPassword);
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");

            var skipLink = page.GetByRole(AriaRole.Link, new()
            {
                Name = "Skip to main content",
                Exact = true,
            });
            await Expect(skipLink).ToHaveAttributeAsync("href", "#main-content");
            await TabUntilFocusedAsync(page, skipLink, "Shift+Tab");
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.Locator("#main-content")).ToBeFocusedAsync();
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Keyboard_only_person_add_and_interaction_logging_persist()
    {
        var page = await fixture.NewPageAsync(new ViewportSize { Width = 360, Height = 800 });

        try
        {
            var email = NewEmail("keyboard");
            await RegisterAsync(page, email, StrongPassword);
            var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
            var firstName = $"Kendall{Guid.NewGuid():N}"[..18];

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");
            var addPersonLink = page.GetByTestId("dashboard-add-person");
            await page.Keyboard.PressAsync("Tab");
            await Expect(addPersonLink).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(page).ToHaveURLAsync(new Regex("/people/new$"));

            var addHeading = page.GetByRole(AriaRole.Heading, new() { Name = "Add a person", Exact = true });
            await Expect(addHeading).ToBeFocusedAsync();
            var firstNameInput = page.GetByLabel("First name", new() { Exact = true });
            await TabUntilFocusedAsync(page, firstNameInput);
            (await firstNameInput.EvaluateAsync<bool>(
                "element => element === document.activeElement"))
                .Should().BeTrue("the first form field should be reachable by keyboard from the page heading");
            await page.Keyboard.TypeAsync(firstName);

            var savePerson = page.GetByTestId("person-form-save");
            await TabUntilFocusedAsync(page, savePerson);
            var focusDiagnostic = await GetFocusDiagnosticAsync(page);
            (await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.activeElement).outlineStyle"))
                .Should().Be("solid", $"keyboard focus has a visible design-token ring: {focusDiagnostic}");
            (await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.activeElement).outlineColor"))
                .Should().NotBe("rgba(0, 0, 0, 0)");
            (await page.EvaluateAsync<double>(
                "() => parseFloat(getComputedStyle(document.activeElement).outlineWidth)"))
                .Should().BeGreaterThanOrEqualTo(2);
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync(firstName);

            var person = (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId))
                .Single(candidate => candidate.FirstName == firstName);

            var appbarLog = page.GetByTestId("appbar-log-interaction");
            await TabUntilFocusedAsync(page, appbarLog, "Shift+Tab");
            await page.Keyboard.PressAsync("Enter");
            await Expect(page).ToHaveURLAsync(new Regex("/interactions/new$"));

            var personInput = page.GetByTestId("quick-log-person-picker");
            await TabUntilFocusedAsync(page, personInput);
            (await personInput.EvaluateAsync<bool>(
                "element => element === document.activeElement"))
                .Should().BeTrue("the quick-log person picker is reachable by keyboard");
            await page.Keyboard.TypeAsync(firstName);
            await Expect(page.GetByRole(AriaRole.Option, new() { Name = firstName, Exact = true }))
                .ToBeVisibleAsync();
            await page.Keyboard.PressAsync("ArrowDown");
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByTestId("interaction-editor")).ToBeVisibleAsync();

            var description = page.GetByLabel("What happened?", new() { Exact = true });
            await TabUntilFocusedAsync(page, description);
            await page.Keyboard.TypeAsync("We talked about a walk in the garden.");

            var saveInteraction = page.GetByTestId("interaction-save");
            await TabUntilFocusedAsync(page, saveInteraction);
            (await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.activeElement).outlineStyle"))
                .Should().Be(
                    "solid",
                    $"the save action keeps a visible keyboard focus ring: {await GetFocusDiagnosticAsync(page)}");
            await page.Keyboard.PressAsync("Enter");

            await Expect(page).ToHaveURLAsync(new Regex($"/people/{person.Id}$"));
            await Expect(page.GetByTestId("timeline-entry-text"))
                .ToContainTextAsync("We talked about a walk in the garden.");
            (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, person.Id))!
                .LastContactedOn.Should().NotBeNull();
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Dashboard_and_timeline_load_bounded_data_regions_within_the_smoke_budget()
    {
        var page = await fixture.NewPageAsync(new ViewportSize { Width = 360, Height = 800 });

        try
        {
            var email = NewEmail("page-load");
            await RegisterAsync(page, email, StrongPassword);
            var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
            var personId = await PeopleTestHelpers.CreatePersonAsync(
                fixture.App,
                ownerId,
                new CreatePersonRequest { FirstName = "Pagination" });
            var today = await GetTodayAsync(fixture.App, ownerId);
            await PeopleTestHelpers.SeedInteractionsAsync(fixture.App, ownerId, personId, today, 55);

            var dashboardTimer = Stopwatch.StartNew();
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");
            var dashboardInteractions = page.GetByTestId("dashboard-interactions-list");
            await dashboardInteractions.WaitForAsync();
            await Expect(dashboardInteractions.Locator("[data-testid^='dashboard-interaction-']"))
                .ToHaveCountAsync(5);
            dashboardTimer.Stop();
            dashboardTimer.Elapsed.Should().BeLessThan(
                TimeSpan.FromSeconds(15),
                "this is a generous interactive page-load smoke budget, not a production SLA");

            var timelineTimer = Stopwatch.StartNew();
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
            var timelineEntries = page.Locator("[data-testid='timeline-entry']");
            await Expect(timelineEntries).ToHaveCountAsync(50);
            await page.GetByTestId("timeline-load-more").WaitForAsync();
            timelineTimer.Stop();
            timelineTimer.Elapsed.Should().BeLessThan(
                TimeSpan.FromSeconds(15),
                "the first timeline page should render without loading the full history");
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Long_invitation_link_wraps_without_page_overflow_on_a_phone()
    {
        await using var app = VariantApp.Create(
            fixture,
            new Dictionary<string, string?> { ["Registration__Mode"] = "InviteOnly" });
        var page = await app.NewPageAsync();
        await page.SetViewportSizeAsync(360, 800);

        await LoginAndWaitForAppAsync(page, DemoDataSeeder.DemoEmail, DemoDataSeeder.DemoPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/admin/users");
        await page.GetByLabel("Email address").FillAsync(NewMaximumLengthEmail());
        await page.GetByTestId("admin-invite-submit").ClickAsync();
        await Expect(page.GetByTestId("admin-invite-link")).ToBeVisibleAsync();
        await AssertNoHorizontalOverflowAsync(page, "admin invitation link", 360);
    }

    private static async Task VisitInteractivePageAsync(
        IPage page,
        string path,
        string readySelector,
        int width)
    {
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, path);
        await page.Locator(readySelector).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(page, path, width);
    }

    private static async Task CheckStaticPageAsync(IPage page, string path, int width)
    {
        await page.GotoAsync(path);
        await Expect(page.Locator("h1")).ToHaveCountAsync(1);
        await AssertNoHorizontalOverflowAsync(page, path, width);
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, string path, int width)
    {
        await page.EvaluateAsync("() => document.fonts.ready");
        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - window.innerWidth");
        var diagnostic = overflow > 1
            ? await page.EvaluateAsync<string>(
                """
                () => {
                    const candidates = [...document.body.querySelectorAll("*")]
                        .map(element => {
                            const rect = element.getBoundingClientRect();
                            const style = getComputedStyle(element);
                            return {
                                tag: element.tagName.toLowerCase(),
                                classes: (element.getAttribute("class") ?? "").slice(0, 160),
                                testId: element.getAttribute("data-testid"),
                                left: Math.round(rect.left),
                                right: Math.round(rect.right),
                                width: Math.round(rect.width),
                                scrollWidth: element.scrollWidth,
                                clientWidth: element.clientWidth,
                                overflowX: style.overflowX,
                            };
                        })
                        .filter(element =>
                            element.right > window.innerWidth + 1 ||
                            element.left < -1 ||
                            element.scrollWidth > element.clientWidth + 1)
                        .sort((left, right) =>
                            (right.scrollWidth - right.clientWidth) -
                            (left.scrollWidth - left.clientWidth))
                        .slice(0, 5);

                    return JSON.stringify({
                        viewport: window.innerWidth,
                        document: {
                            clientWidth: document.documentElement.clientWidth,
                            scrollWidth: document.documentElement.scrollWidth,
                        },
                        body: {
                            clientWidth: document.body.clientWidth,
                            scrollWidth: document.body.scrollWidth,
                        },
                        candidates,
                    }, null, 2);
                }
                """)
            : "none";
        overflow.Should().BeLessThanOrEqualTo(
            1,
            $"the {path} page at {width}px must not scroll horizontally; overflowing element boxes (no text content): {diagnostic}");
    }

    private static Task<string> GetFocusDiagnosticAsync(IPage page) =>
        page.EvaluateAsync<string>(
            """
            () => {
                const describe = element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    const pseudo = name => {
                        const pseudoStyle = getComputedStyle(element, name);
                        return {
                            display: pseudoStyle.display,
                            outline: pseudoStyle.outline,
                            boxShadow: pseudoStyle.boxShadow,
                            border: pseudoStyle.border,
                        };
                    };

                    return {
                        tag: element.tagName.toLowerCase(),
                        classes: (element.getAttribute("class") ?? "").slice(0, 160),
                        testId: element.getAttribute("data-testid"),
                        role: element.getAttribute("role"),
                        focusVisible: element.matches(":focus-visible"),
                        outline: style.outline,
                        boxShadow: style.boxShadow,
                        border: style.border,
                        bounds: {
                            left: Math.round(rect.left),
                            top: Math.round(rect.top),
                            right: Math.round(rect.right),
                            bottom: Math.round(rect.bottom),
                            width: Math.round(rect.width),
                            height: Math.round(rect.height),
                        },
                        before: pseudo("::before"),
                        after: pseudo("::after"),
                    };
                };

                const chain = [];
                for (let element = document.activeElement, depth = 0;
                     element && depth < 5;
                     element = element.parentElement, depth++) {
                    chain.push(describe(element));
                }

                return JSON.stringify(chain, null, 2);
            }
            """);

    private static string NewMaximumLengthEmail() => string.Concat(
        new string('e', 32),
        Guid.NewGuid().ToString("N"),
        "@",
        new string('d', 63),
        ".",
        new string('c', 63),
        ".",
        new string('x', 57),
        ".org");

    private static async Task<DateOnly> GetTodayAsync(RelioWebAppFactory app, string ownerId)
    {
        using var scope = app.CreateRealScope();
        var timeZoneService = new UserTimeZoneService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await timeZoneService.GetTodayAsync();
    }

    private sealed class OwnerCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }

    private static async Task TabUntilFocusedAsync(
        IPage page,
        ILocator target,
        string key = "Tab")
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }

            await page.Keyboard.PressAsync(key);
        }

        throw new InvalidOperationException("The keyboard could not reach the expected control.");
    }
}
