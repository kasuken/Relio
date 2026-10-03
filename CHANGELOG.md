# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

- Account registration with email and password (issue #15): ASP.NET Core Identity with
  `Relio.Data.Identity.RelioUser` (`RelioDbContext` is now an `IdentityDbContext<RelioUser>`, see
  the `AddIdentity` migration), a strong password policy (12+ characters, mixed case, digit and
  symbol), a MudBlazor-styled Register/Login flow under `Relio.Web/Components/Account/Pages`
  (static SSR - see AGENTS.md on why Identity account pages can't be interactive), a sign-up-time
  browser time zone capture via a hidden field filled by a self-hosted script, and an
  `IEmailSender<RelioUser>` abstraction (`Email:Provider=None` by default - no email sent, no
  confirmation required; `Email:Provider=Smtp` requires confirmation and sends through
  `System.Net.Mail.SmtpClient`). Every page now requires sign-in by default (a fallback
  authorization policy plus `AuthorizeRouteView`), except the account pages and `/health/*`.
  `ICurrentUser` is now backed by `AuthenticationStateProvider`
  (`Relio.Web.Security.AuthenticationStateCurrentUser`) instead of `HttpContext`, so it keeps
  resolving the signed-in user after a Blazor Server circuit's SignalR connection takes over - see
  the new "Accounts and authentication" section of AGENTS.md. Also adds
  `Relio.Data.Seeding.DemoDataSeeder` (`DemoData:Enabled`, off by default and refused outside
  Production), seeding a `demo@relio.local` account with realistic sample people, for exploring
  Relio without registering first - see the README's "Run locally without SQL Server" section.
- EF Core InMemory database mode for tests/local dev: `Database:Provider=InMemory` (vs. the
  default `SqlServer`) in `Relio.Data.DependencyInjection.ServiceCollectionExtensions.AddRelioData`
  - no connection string required, no migrations (`EnsureCreatedAsync` instead), and a loud startup
  warning when active so it is never mistaken for a real deployment.
- `Relio.Web.E2ETests`: a Playwright (Chromium, headless) end-to-end test project that drives the
  real Relio.Web app on a real Kestrel socket with the InMemory provider. `RelioAppFixture`/
  `RelioWebAppFactory` provide the reusable harness (shared app + browser, per-test pages/contexts,
  phone/desktop viewports, trace export on `ClosePageAsync`) documented in AGENTS.md's new
  "End-to-end tests" section. Covers the app shell from issue #11: the dashboard empty state, nav
  drawer link navigation, the phone-viewport hamburger drawer, dark-mode switching/persistence, and
  the `/health/live`/`/health/ready` endpoints. CI installs the matching Chromium build
  (`playwright.ps1 install --with-deps chromium`, cached) before running the suite and uploads
  Playwright traces on failure.

- User time zones for dates and reminders (issue #12): `Relio.Domain.UserProfile` (one per user,
  `TimeZoneId` defaulting to UTC), `Relio.Application.Time.IUserTimeZoneService` (current user's
  time zone/"today", set time zone, due-today/overdue checks) implemented by
  `Relio.Data.Time.UserTimeZoneService`, pure `UserCalendar` helpers (UTC-instant ↔ user-local
  conversion, next-birthday-occurrence with Feb 29 handled as Feb 28 in non-leap years), IANA time
  zone id validation (`TimeZoneIds`), a self-hosted browser-time-zone JS reader
  (`Relio.Web.Time.IBrowserTimeZoneReader`, for sign-up #15 to default to), and the `AddUserProfile`
  migration. Documented in the new "Dates and time zones" section of AGENTS.md.
- MudBlazor app shell: a responsive nav drawer (Dashboard, People, Reminders, Difficult
  moments, Settings) that collapses to a hamburger-toggled overlay on phones, an app bar with a
  System/Light/Dark theme menu, and placeholder pages with calm empty-state copy. Light/dark
  preference (`ThemeMode`, `ThemeModeState`, `IThemeModeStore`) follows the system setting by
  default, is changeable from the app bar or Settings, and persists per browser via
  `wwwroot/js/theme.js` (per-user, server-side persistence comes with accounts, epic #14).
  Shared `EmptyState` and `ConfirmDialog` components, `DialogServiceExtensions.ShowConfirmAsync`,
  `SnackbarExtensions`, and `EntryKind`/`EntryKindVisuals` (the single source of truth for each
  entry kind's icon and colour) round out the shell. `Relio.Web.Tests` (xUnit, AwesomeAssertions,
  bUnit) covers the theme preference logic, `EntryKindVisuals` distinctness and the shell
  components.
- `Relio.Data.IntegrationTests`: SQL Server-backed integration tests that re-prove the
  `PeopleService` cross-user isolation scenarios and the unique `(OwnerId, Name)` tag index against
  a real database, applying the actual EF Core migrations via `Database.MigrateAsync()`. A custom
  `[SqlServerFact]` attribute skips these tests when `ConnectionStrings__Relio` is unset (e.g. no
  local Docker) instead of failing or silently passing; CI always sets it, so they always run
  there.
- User-scoped domain model and data-access pattern: `IOwnedEntity`/`OwnedEntity` (Relio.Domain), the first product entities (`Person`, `Tag`, with a many-to-many), `ICurrentUser` (Relio.Application, implemented in Relio.Web from `HttpContext` claims), `IPeopleService` with ownership checks on every read, mutation and foreign tag id, audit timestamps stamped from `TimeProvider` in `RelioDbContext.SaveChangesAsync`, owner-scoped indexes, and the `AddPeople` migration. The reusable pattern is documented in `AGENTS.md`.
- Data layer: `RelioDbContext` (Relio.Data) with SQL Server via EF Core, the `AddRelioData` DI extension, a design-time factory for `dotnet ef`, and an initial migration. Migrations auto-apply on startup in Development only; production applies them with an explicit `dotnet ef database update`.
- `/health/ready` now checks database connectivity (tagged `ready`); `/health/live` stays process-only.
- CI: SQL Server service container, a `dotnet ef migrations has-pending-model-changes` gate and a `dotnet ef database update` proof step.
- Relio design system: MudBlazor light and dark theme that follows the system setting, self-hosted Alegreya and Hanken Grotesk fonts, CSS tokens and the timeline thread styles.
- Initial .NET 10 Blazor solution with MudBlazor, health checks, CI, CodeQL and CLA workflows.

### Fixed

- The app bar's appearance (System/Light/Dark) menu never opened when actually clicked in a
  browser: `ThemeModeMenu.razor` wrapped a `MudIconButton` in `MudMenu`'s `ActivatorContent`, and
  that two-element activator (a wrapping `div` plus an independently-interactive button) did not
  trigger the menu's open state. Switched to `MudMenu`'s own `Icon`/`AriaLabel` parameters, which
  render and wire up a single activator button. Found by `Relio.Web.E2ETests.ThemeTests` - the
  first time this app had been driven in an actual browser.
