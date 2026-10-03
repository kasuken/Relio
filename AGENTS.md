# Relio agent guidance

This file is the canonical entry point for automated contributors. More specific
instructions under `.github/instructions/` and `.github/skills/` apply when they
do not conflict with this file or with the existing implementation.

## Product and architecture

- Relio is a private relationship memory system: people, interactions, notes,
  reminders and difficult moments. It is not a CRM, a social network or an AI
  advisor. See the epics in GitHub issues for the scope of the MVP.
- Relio is a .NET 10 Blazor Web App using Interactive Server rendering,
  MudBlazor, EF Core, SQL Server and ASP.NET Core Identity.
- Preserve the Domain / Application / Data / Web separation. Razor components
  orchestrate UI only; data access and business invariants belong in services.
- Every read, mutation and related-entity assignment must be scoped to the
  authenticated user. Validate both the primary entity and all supplied foreign IDs.
  There is no shared or team data in the MVP.
- Active views exclude archived people by default. Archive and restore must be
  reversible.
- Birthdays, reminder dates and interaction dates are user-calendar dates in the
  user's time zone; audit timestamps remain UTC.
- Relio runs both as a hosted service and self-hosted. Hosted-only features
  (billing, analytics) sit behind configuration flags that are off by default,
  and every feature must work with them off.

## Current product decisions

- MudBlazor is the only UI component framework. All UI follows the design system in
  `docs/design-system/README.md`: use the theme and tokens, never hard-coded colours or fonts.
- No third-party requests from the app (fonts are self-hosted, no CDNs or analytics scripts).
- No AI features in the MVP.
- Tests use xUnit and AwesomeAssertions.

## User-scoped data pattern

Established by issue #10 and exercised end to end by `Person`/`Tag`/`IPeopleService`. Follow this
for every new owned entity and service; do not invent new plumbing per feature.

**Domain (`Relio.Domain`, no dependencies).**
- Every user-owned entity implements `IOwnedEntity` (`OwnerId`, `CreatedAtUtc`, `UpdatedAtUtc`),
  normally by inheriting the `OwnedEntity` base class. `OwnerId` matches the future ASP.NET Core
  Identity user id (epic #14); it is set once by the service that creates the entity, never by a
  caller or the database.
- Archivable entities add their own `IsArchived`/`ArchivedAtUtc`, following `Person`.

**Application (`Relio.Application`).**
- Read the signed-in user only through `ICurrentUser` (`Security/ICurrentUser.cs`) - never
  `HttpContext`. Call the `RequireUserId()` extension first in every service method; it throws
  `UnauthenticatedUserException` when nobody is signed in.
- Define one service interface per aggregate (e.g. `IPeopleService`) with request records for
  create/update input. Every method takes/returns only data already scoped to the current user.
- When a mutation accepts a foreign id (e.g. a tag id), the implementation must resolve it against
  rows owned by the current user and throw `Ownership.ForeignEntityNotOwnedException` if any id
  does not resolve. Not-found and not-owned use the exact same message - never let a request
  distinguish "doesn't exist" from "belongs to someone else".
- For the primary entity, not-found and not-owned are both reported as "no result": `GetAsync`
  returns `null`, `UpdateAsync`/`ArchiveAsync`/`RestoreAsync` return `false`. Never throw a
  not-found exception for the primary entity - it would leak existence through a different code
  path than the foreign-id case.

**Data (`Relio.Data`) - where the EF-dependent implementation lives.**
- Service *interfaces* live in `Relio.Application`; service *implementations* live in
  `Relio.Data` (e.g. `Relio.Data.People.PeopleService`) because they depend on `RelioDbContext`
  directly. This keeps Domain and Application free of EF Core while still satisfying "data access
  belongs in services". Register implementations in `AddRelioData` (`ServiceCollectionExtensions`).
- Ownership is enforced by **explicit filtering in every query and mutation**
  (`.Where(x => x.OwnerId == ownerId)`), not a global EF Core query filter. A global filter would
  need the `DbContext` itself to know the current user, which is riskier to get right with a
  scoped context (easy to forget to set it, easy to bypass with `IgnoreQueryFilters`) and harder to
  unit test in isolation. Explicit filtering keeps the ownership check visible at every call site.
- Add an index on `(OwnerId, <column>)` for every column a list, timeline or lookup filters or
  sorts by (see `PersonConfiguration`, `TagConfiguration`).
- Audit timestamps are stamped once, centrally, in `RelioDbContext.SaveChangesAsync`/`SaveChanges`
  from an injected `TimeProvider` - entities and services never set `CreatedAtUtc`/`UpdatedAtUtc`
  themselves. Inject `TimeProvider` (not `DateTime.UtcNow`) anywhere else a timestamp is needed
  (e.g. `ArchivedAtUtc`), so tests can supply a fake.
- After changing the model, add a migration:
  `dotnet ef migrations add <Name> --project Relio.Data --startup-project Relio.Web`.

**Web (`Relio.Web`).**
- The real `ICurrentUser` is `Relio.Web.Security.AuthenticationStateCurrentUser` (epic #14, issue
  #15), reading the `ClaimTypes.NameIdentifier` claim off `AuthenticationStateProvider`'s current
  `ClaimsPrincipal` - see the "Accounts and authentication" section below for why this is
  `AuthenticationStateProvider`-backed rather than `HttpContext`-backed. Registered in `Program.cs`.

**Tests.**
- Prove cross-user isolation for every new service: create data as user A, then assert user B's
  service instance cannot read, list, update, archive, restore it, or attach user B's own
  foreign-id rows (tags, etc.) to user A's entity. See
  `Relio.Data.Tests/People/PeopleServiceOwnershipTests.cs` for the shape these tests should take.
- These tests use the EF Core InMemory provider with a fake `ICurrentUser` and `TimeProvider` for
  fast feedback on service-level ownership logic. They intentionally stay even though
  `Relio.Data.IntegrationTests` now re-proves the same scenarios against SQL Server: InMemory runs
  in milliseconds with no external dependency, so keep it as the first signal and use the SQL
  Server project to prove anything InMemory cannot (constraints, indexes, migrations, real query
  translation).
- `Relio.Data.IntegrationTests` re-proves these same cross-user scenarios against a real SQL
  Server database (CI's service container, or Docker locally), applies the real EF Core migrations
  with `Database.MigrateAsync()`, and additionally proves the unique `(OwnerId, Name)` index on
  `Tag`. Local Docker is not available in this environment, so its tests use a custom
  `[SqlServerFact]` attribute that skips them (not fails, not silently passes) when
  `ConnectionStrings__Relio` is unset; CI always sets it, so they always run there. To run them
  locally: start a server (e.g.
  `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="Your_password123!" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`)
  then `ConnectionStrings__Relio="Server=localhost,1433;Database=Relio;User Id=sa;Password=Your_password123!;Encrypt=False;TrustServerCertificate=True;" dotnet test`.
- Never log note or person content (see `.github/skills/gdpr-compliant/SKILL.md`); ownership
  exceptions and log messages here only ever reference ids and entity kinds.

## Dates and time zones

Established by issue #12. Calendar dates (birthdays, reminder due dates, interaction dates) are
never UTC instants - they are a specific day in the owning user's time zone. Audit timestamps
(`CreatedAtUtc`, `UpdatedAtUtc`, `ArchivedAtUtc`) are always UTC instants and never touch a user's
time zone. Never call `DateTime.Now`/`DateTime.UtcNow`/`DateTime.Today` anywhere in the product
(Domain, Application, Data or Web) - always go through an injected `TimeProvider`, as
`RelioDbContext` already does for audit timestamps.

- `Relio.Domain.UserProfile` is the per-user settings row (`OwnerId`, `TimeZoneId`), following the
  user-scoped data pattern above. One row per user, enforced by a unique index on `OwnerId`
  (`UserProfileConfiguration`). `TimeZoneId` defaults to `"UTC"` and is always a valid IANA id -
  nothing writes to it without going through `TimeZoneIds.Parse` first.
- `Relio.Application.Time.TimeZoneIds` validates IANA time zone ids
  (`TimeZoneInfo.TryFindSystemTimeZoneById` - .NET resolves IANA ids via ICU on both Linux and
  Windows, so no Windows-id mapping is needed). Use `TryParse` on read paths that should fall back;
  use `Parse` (throws `InvalidTimeZoneIdException`) on write paths - an unknown id must be
  rejected, never silently coerced to UTC.
- `Relio.Application.Time.UserCalendar` holds pure, synchronous helpers - `ToUserDate` (UTC instant
  → calendar date in a given zone), `Today` (reads a `TimeProvider`), `IsDueToday`, `IsOverdue`, and
  `NextOccurrence` (next birthday/anniversary on or after a date). They take a `TimeZoneInfo`
  directly, so tests exercise them without a database or current user.
- `Relio.Application.Time.IUserTimeZoneService` is the per-request/per-user entry point - get the
  current user's time zone and "today", set their time zone, and ask whether a date is due
  today/overdue for them. Implemented in `Relio.Data.Time.UserTimeZoneService` (depends on
  `RelioDbContext`, like `PeopleService`) and registered in `AddRelioData`. Sign-up (#15) and
  account settings (#18) are the only features that should call `SetTimeZoneAsync` directly; every
  other feature only reads.
- A `Person`'s `Birthday` (and any future reminder/interaction date) is a `DateOnly` - never
  convert it to/from UTC. Feb 29 birthdays observe **Feb 28** in non-leap years (not Mar 1) - see
  `UserCalendar.NextOccurrence`.
- `Relio.Web.Time.IBrowserTimeZoneReader`/`BrowserTimeZoneReader` read the browser's IANA time zone
  id via a self-hosted JS module (`wwwroot/js/timezone.js`, `Intl.DateTimeFormat().resolvedOptions().timeZone` -
  no third-party script), for interactive components (e.g. account settings, #18) to default to it
  instead of UTC. Web-only; Application/Data services never depend on it. Sign-up (#15) cannot use
  this - its Register page is static SSR (see "Accounts and authentication" below) with no live
  circuit for JS interop - so it reads the same `timezone.js` logic from a plain, synchronous
  `<script>` that fills a hidden form field instead; see
  `Relio.Web/Components/Account/Pages/Register.razor`.
- Tests must cover users far from UTC (e.g. `Pacific/Kiritimati`, UTC+14; `Pacific/Pago_Pago`,
  UTC-11) around the UTC midnight boundary, and a DST transition (e.g. `America/New_York`,
  `Europe/Rome`), using `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` for
  deterministic "now". See `Relio.Application.Tests/Time/UserCalendarTests.cs`.

## Accounts and authentication

Established by issue #15, under epic #14. ASP.NET Core Identity, local accounts only - no social
login, ever (epic #14's guardrail). Later issues extend this without restructuring it: #16
(login/logout/lockout/remember-me polish - this issue ships a minimal login/logout, just enough to
reach the app and prove it end to end), #17 (password reset), #18 (account settings), #19
(first-user admin + `Registration:Mode`), #20 (2FA - `RelioUser` already has the columns it needs,
from `IdentityUser`).

- **The user entity** is `Relio.Data.Identity.RelioUser : IdentityUser`, in `Relio.Data` (not
  `Relio.Domain`, which stays free of any framework dependency - see the "User-scoped data
  pattern" section above). Every other owned entity still only ever stores its id as `OwnerId`;
  nothing takes a navigation property to it. `RelioDbContext` is an `IdentityDbContext<RelioUser>`
  (migration `AddIdentity`); audit-timestamp stamping in `SaveChanges`/`SaveChangesAsync` is
  unaffected, since Identity's own entities are not `IOwnedEntity`.
- **Identity options live in one place**: `Relio.Web.Identity.ServiceCollectionExtensions.AddRelioIdentity`
  - password policy (length 12+, mixed case, digit, symbol - length over complexity per NIST SP
  800-63B, with complexity rules added for defence in depth, given this stores private
  relationship data), unique email, the cookie scheme, the fallback authorization policy, and which
  `IEmailSender<RelioUser>` to register. Extend options here, not in `Program.cs`.
- **Account pages** live in `Relio.Web/Components/Account/Pages` (`Register.razor`, `Login.razor`,
  `RegisterConfirmation.razor`, `ConfirmEmail.razor`, `AccessDenied.razor`), with shared
  infrastructure in `Relio.Web/Components/Account` (`IdentityComponentsEndpointRouteBuilderExtensions`
  maps the `POST /Account/Logout` minimal API endpoint - logout, like register/login, must write
  directly to the HTTP response to clear the auth cookie) and `Components/Account/Shared`
  (`AccountLayout`, `RedirectToLogin`). Add new account pages (#17's reset, #18's settings) to this
  same folder.
- **Why static SSR**: `App.razor`'s `<Routes @rendermode="InteractiveServer" />` makes every routed
  page interactive by default, but `SignInManager`/`UserManager` need to write the auth cookie
  directly to the HTTP response of the request that is actually submitting the form - something an
  interactive Blazor Server circuit (a SignalR connection, not a request/response pair) cannot do.
  Every account page carries `@attribute [ExcludeFromInteractiveRouting]` (plus `[AllowAnonymous]`)
  to force static SSR regardless of that ambient render mode.
- **MudBlazor in static SSR forms**: MudBlazor's input components (`MudTextField`, etc.) only post
  their value back to the server when a *live circuit* is driving their two-way binding - on a
  static SSR page they render with no `name` attribute at all, so a real form post arrives with the
  field empty. Account pages therefore use plain HTML `<input name="Input.X">` elements (styled
  from the same design tokens via the `.rl-field` CSS class in `wwwroot/app.css`, not MudBlazor's
  own classes) for every value that must survive the post, and keep `MudButton`
  (`ButtonType="ButtonType.Submit"`) only for the submit button, which needs no two-way binding and
  renders a plain `<button type="submit">`.
- **`ICurrentUser` in interactive components**: `Relio.Web.Security.AuthenticationStateCurrentUser`
  (not `HttpContext`-backed) is the real `ICurrentUser`. `IHttpContextAccessor.HttpContext` is only
  populated for the initial HTTP request/prerender - once a Blazor Server circuit's SignalR
  connection takes over, it is `null` for the rest of the circuit, silently breaking any
  `ICurrentUser` built on it. `AuthenticationStateProvider` does not have this problem: Blazor
  Server's own provider captures the signed-in user once, when the circuit is created, and holds it
  for the circuit's whole lifetime. `ICurrentUser` stays a synchronous abstraction (every
  Application service already depends on it that way); `AuthenticationStateCurrentUser` blocks on
  `GetAuthenticationStateAsync().GetAwaiter().GetResult()`, which is safe specifically because that
  call never performs real async work for the circuit's lifetime - see its own remarks. Verified
  end to end by `Relio.Web.E2ETests` (sign in, then exercise a service call from an interactive
  component).
- **Protecting pages**: `Relio.Web.Identity.ServiceCollectionExtensions.AddRelioIdentity` sets a
  fallback authorization policy (`RequireAuthenticatedUser`), so every page requires sign-in unless
  it opts out with `[AllowAnonymous]` (the account pages) or `.AllowAnonymous()` (the `/health/*`
  endpoints). The cookie's `LoginPath` means an anonymous request to a protected page redirects to
  `/Account/Login` automatically, before Blazor even renders anything. `Routes.razor`'s
  `AuthorizeRouteView` (`NotAuthorized` → `RedirectToLogin`) is defence in depth for a circuit whose
  session stops being valid while it is already open (a client-side navigation inside an existing
  circuit does not go through the HTTP pipeline/cookie redirect again).
- **Email abstraction**: ASP.NET Core Identity's built-in `IEmailSender<TUser>`, implemented by
  `Relio.Web.Email.NullEmailSender` (`Email:Provider=None`, the default - sends nothing, logs a
  warning per call, never requires email confirmation) and `Relio.Web.Email.SmtpEmailSender`
  (`Email:Provider=Smtp` - requires confirmation, sends via `System.Net.Mail.SmtpClient`, chosen
  over MailKit because Relio only needs plain transactional emails for now; revisit if a later
  feature needs more). `Email:Smtp:Password` (and any other secret) never goes in
  `appsettings*.json` - user secrets locally, environment variables/host secret store in
  production. Never log an email body or a confirmation/reset token/link - it is a bearer
  credential (see the gdpr-compliant skill).
- **Demo data**: `Relio.Data.Seeding.DemoDataSeeder` creates a `demo@relio.local` account (test/demo
  password only, see its XML docs) with realistic sample people (one archived, one with a Feb 29
  birthday) and a non-UTC time zone, writing directly through `RelioDbContext` rather than through
  `IPeopleService`/`IUserTimeZoneService` - those require a signed-in `ICurrentUser`, which does not
  exist at startup (`Relio.Web.Components.Account.Pages.Register` uses the same escape hatch for
  the one request that creates a brand new user's own `UserProfile`, for the same reason). Runs
  once at startup, only when `DemoData:Enabled=true` (default `false`) and the environment is not
  Production (refuses, logging an error, otherwise); idempotent. See the README's "Run locally
  without SQL Server" section.
- **Tests**: `Relio.Web.E2ETests` signs in through the real login page -
  `RelioAppFixture.SignInAsDemoAsync(page)` - before visiting any protected route; every existing
  shell test (`NavigationTests`, `DashboardTests`, etc.) does this first. `RelioWebAppFactory`
  enables `DemoData:Enabled` for the shared fixture. To exercise a differently-configured app (e.g.
  `Email:Provider=Smtp` with a test email sink, see `EmailConfirmationTests`), construct a *new*
  `RelioWebAppFactory(configureTestServices: ...)` rather than the base class's
  `WithWebHostBuilder` - see `RelioWebAppFactory`'s remarks for why that matters.

## End-to-end tests

`Relio.Web.E2ETests` drives the real Relio.Web app (the `Program` entry point, via its trailing
`public partial class Program;` marker) with Playwright, in a real headless Chromium browser - not
bUnit's rendered-component model (`Relio.Web.Tests`), which never exercises JS interop, real HTTP,
or an actual SignalR circuit.

- **Database**: tests run the app with `Database:Provider=InMemory` (see "Current product
  decisions" below and `Relio.Data.DependencyInjection.ServiceCollectionExtensions`) - no SQL
  Server, no connection string, no migrations (`Database.EnsureCreatedAsync()` instead). Never the
  default; it is test/dev only and logs a startup warning when active.
- **Fixture API** (`Relio.Web.E2ETests/Infrastructure/`):
  - `RelioAppFixture` is an `IAsyncLifetime` shared across every test class via
    `[Collection(RelioAppCollection.Name)]` - one running app (`RelioWebAppFactory`, a real Kestrel
    socket on a random loopback port) and one shared headless Chromium for the whole run.
  - `fixture.BaseUrl` is the app's base address; `await fixture.NewPageAsync(Viewports.Phone |
    Viewports.Desktop)` opens a fresh, isolated browser context (own cookies/localStorage) and
    page - always get a fresh page per test rather than sharing one.
  - `RelioAppFixture.GotoAndWaitForInteractiveAsync(page, path)` navigates and waits for
    `<html data-app-ready="true">` (set by `MainLayout.razor`/`wwwroot/js/theme.js` once the
    circuit has connected and the first interactive render finished) - use this instead of a fixed
    delay, or tests race Blazor Server's circuit connecting.
  - `RelioAppFixture.ClosePageAsync(page)` ends a test: exports a Playwright trace (screenshots,
    DOM snapshots, actions) to `playwright-traces/` next to the test binaries, then closes the
    context. Always call this instead of `page.Context.CloseAsync()` directly, so a failure always
    has a trace to inspect.
  - To exercise a variant of the app (e.g. a later test swapping in an email sink instead of a
    real sender), build a second factory from
    `fixture.App.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => ...))` -
    `RelioWebAppFactory.CreateHost` starts a fresh Kestrel listener for it, so it does not disturb
    the shared instance other tests are using.
- **Running locally**: `dotnet test Relio.Web.E2ETests`. This machine's Playwright NuGet version
  can expect a different cached Chromium revision than what is on disk; rather than require a
  network install, set `RELIO_E2E_CHROMIUM=/usr/bin/chromium` (or another system Chromium/Chrome
  path) to launch that instead of Playwright's bundled browser. Left unset (CI's default), it uses
  the bundled build installed by `playwright.ps1 install --with-deps chromium` (see
  `.github/workflows/ci.yml`).
- **Writing a new test**: add a class under `Relio.Web.E2ETests`, tag it
  `[Collection(RelioAppCollection.Name)]`, take `RelioAppFixture` by constructor injection, open a
  page with `fixture.NewPageAsync(...)`, navigate with `GotoAndWaitForInteractiveAsync`, assert
  with Playwright's `Expect(...)` (`using static Microsoft.Playwright.Assertions;`), and finish with
  `RelioAppFixture.ClosePageAsync(page)`.

## Required validation

Run before handing off changes:

```bash
dotnet restore Relio.slnx
dotnet build Relio.slnx --configuration Release --no-restore
dotnet test Relio.slnx --configuration Release --no-build
dotnet list Relio.slnx package --vulnerable --include-transitive
git diff --check
```

Once the data layer exists, also run
`dotnet ef migrations has-pending-model-changes --project Relio.Data --startup-project Relio.Web --no-build --configuration Release`.
