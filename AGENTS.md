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
- Per-user lookup lists are owned entities, not enums or global tables: `RelationshipType` (issue
  #22) is one, and a `Person` references it by `RelationshipTypeId`, which is a **foreign id** in the
  sense below. Its defaults (`RelationshipType.DefaultNames`, `CreateDefaults(ownerId)`) are seeded
  in exactly three places and never lazily (a lazy "ensure the defaults exist" would resurrect a
  default the user deleted in #25): `AccountRegistrationService.RegisterAsync` (new accounts, in the
  same save as the new `UserProfile`), the `AddPersonProfile` migration (accounts that already
  existed), and `DemoDataSeeder`. **Every code path that creates a `RelioUser` must add
  `RelationshipType.CreateDefaults(user.Id)` in the same save.** The migration keeps its own literal
  copy of the names on purpose.
- Every child of a person (contact methods, interactions, notes, reminders, difficult moments, ...)
  has a `PersonId` foreign key to `People` with `OnDelete(Cascade)` **and** its own `OwnerId`,
  filtered explicitly like everything else, with an index on `(OwnerId, PersonId)`. Deleting a person
  (#26) goes through one private `PeopleService.RemoveDependentsAsync(ownerId, person)` and every new
  child adds its line to it **and** to `PersonDeleteChecklistTests`: the InMemory provider only cascades
  *tracked* dependents, and the database cascade is the SQL Server backstop (see "Archive, restore and
  delete" under People). `ContactMethod` (#24) is the first child, handled there; **#59's account deletion
  must include `ContactMethods` too**.
- **One place builds a person.** `Relio.Data.People.PersonEntityBuilder` (`NewPerson`, `ApplyProfile`,
  `CreateContactMethod`, `ApplyContactMethod`) turns a profile input into a `Person` and its contact methods
  (normalized text, comparison keys, positions). `PeopleService.CreateAsync`/`UpdateAsync` and the import (#29)
  use it, so a new profile field is mapped once.
- **The merge checklist (#28), next to the delete checklist.** Merging two profiles moves everything that
  belongs to the duplicate to the primary in one private `PersonMergeService.MoveDependentsAsync`. Every
  new entity with a `PersonId` must, in the same pull request: (1) cascade on delete (it is the backstop for
  anything the merge did not load), (2) add its line to `PeopleService.RemoveDependentsAsync`, (3) add its
  line to `PersonMergeService.MoveDependentsAsync` saying how its rows move (reassign `PersonId`; for
  participant or link rows, remove the duplicate's row when the primary already has one, so nothing is
  repeated), (4) be added to `PersonDeleteChecklistTests` **and** `PersonMergeChecklistTests` and to the
  SQL Server foreign-key lists in `PersonDeleteSqlServerTests` and `PersonMergeSqlServerTests`, and (5)
  extend the E2E `PersonMergeTests` merge test so the merged profile shows the new entries. A new column on
  `Person` also needs a rule in `PersonMergeRules.Combine` (`Every_person_column_has_a_merge_rule` fails
  until it is listed). Load rows with `OwnerId == ownerId && PersonId == duplicate.Id`, tracked; never
  `ExecuteUpdate`.
- **New children are added with `DbSet.Add` explicitly.** `OwnedEntity` gives every instance a non-empty
  `Guid` up front, so an entity that is merely *reachable* from a tracked parent (added to its collection)
  is discovered by EF Core as an existing row, tracked `Modified`, and the save fails with a
  `DbUpdateConcurrencyException` for an UPDATE that touches no row. `Add` on a brand new graph
  (`People.Add(person)` in create) is fine; on an update, add each new child and each new `Tag` to its own `DbSet`.

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
  returns `null`, `UpdateAsync`/`ArchiveAsync`/`RestoreAsync`/`DeleteAsync` return `false`. Never throw a
  not-found exception for the primary entity - it would leak existence through a different code
  path than the foreign-id case.
- **Validation errors are codes in Application and messages in Web.** Rules that depend only on the
  input are pure functions (`People/PersonProfileRules.Validate(input, today)` - "today" is a
  parameter, so tests need no clock); the service trims with the same class, validates, and throws
  `PersonValidationException` carrying `PersonValidationError` codes (its message lists the codes,
  never the submitted values). `Relio.Web` words each code next to the field it belongs to
  (`PersonFormMessages`, with a test that every code has a message). The service is the authority:
  the UI never duplicates a rule, and never reads the clock (the future-birthday rule runs in the
  user's own time zone inside the service). A request that fails validation saves nothing.

**Data (`Relio.Data`) - where the EF-dependent implementation lives.**
- Service *interfaces* live in `Relio.Application`; service *implementations* live in
  `Relio.Data` (e.g. `Relio.Data.People.PeopleService`) because they depend on `RelioDbContext`
  directly. This keeps Domain and Application free of EF Core while still satisfying "data access
  belongs in services". Register implementations in `AddRelioData` (`ServiceCollectionExtensions`)
  with `AddDataService`, not `AddScoped` (see "One database operation at a time" below).
- Ownership is enforced by **explicit filtering in every query and mutation**
  (`.Where(x => x.OwnerId == ownerId)`), not a global EF Core query filter. A global filter would
  need the `DbContext` itself to know the current user, which is riskier to get right with a
  scoped context (easy to forget to set it, easy to bypass with `IgnoreQueryFilters`) and harder to
  unit test in isolation. Explicit filtering keeps the ownership check visible at every call site.
- Add an index on `(OwnerId, <column>)` for every column a list, timeline or lookup filters or
  sorts by (see `PersonConfiguration`, `TagConfiguration`). A list screen that hides archived rows
  and sorts uses **composite** `(OwnerId, IsArchived, <sort columns>)` indexes, one per ordering:
  the people list's three (`FirstName, LastName` / `CreatedAtUtc` / `LastContactedOn`, issue #23)
  replaced the bare `(OwnerId, IsArchived)`, which they cover. SQL Server's 1,700-byte key limit is
  the budget to watch (the widest key, owner + flag + two 100-character names, is about 1.3 KB).
- Audit timestamps are stamped once, centrally, in `RelioDbContext.SaveChangesAsync`/`SaveChanges`
  from an injected `TimeProvider` - entities and services never set `CreatedAtUtc`/`UpdatedAtUtc`
  themselves. Inject `TimeProvider` (not `DateTime.UtcNow`) anywhere else a timestamp is needed
  (e.g. `ArchivedAtUtc`), so tests can supply a fake.
- **The scoped `RelioDbContext` lives as long as a Blazor circuit** - minutes or hours, not a request
  - because the people pages are interactive. So in every owned-data service: reads use
  `AsNoTracking()` (a tracked entity would answer later reads from memory and hide changes made
  elsewhere), and every mutation loads what it changes tracked, saves once, and calls
  `dbContext.ChangeTracker.Clear()` in a `finally` block (it also covers a failed `SaveChanges`
  leaving an *Added* entity that the next save would insert again). Not an `IDbContextFactory`:
  Identity's stores need the scoped context, and tests build `new PeopleService(dbContext,
  currentUser, timeProvider)` directly, so a service constructor must not gain parameters lightly.
  Read-only loads belong in `OnInitializedAsync`/`OnParametersSetAsync`, which prerendering runs
  twice.
- **One database operation at a time per `RelioDbContext` (the database lane).** A `DbContext`
  allows one operation in flight, and Blazor's renderer does not wait. When a component's
  `OnInitializedAsync`/`OnParametersSetAsync` awaits, the renderer starts the next sibling's, and
  a second UI event can arrive while a first handler awaits. Every component of a circuit (and of
  one prerender) shares the scoped context, so `/settings`' three sections used to throw "A second
  operation was started on this context instance" against SQL Server. The InMemory provider
  completes synchronously and never shows it. `RelioDbContext.Lane`
  (`Relio.Data.Concurrency.DatabaseLane`) is a per-context async gate. **Every data service is
  registered with `AddDataService<IService, Service>()` in `AddRelioData`, never `AddScoped`.**
  That wraps it in `DatabaseLaneProxy`, so each interface call runs alone on its context and the
  others wait their turn. Constructors stay `(RelioDbContext, ICurrentUser, ...)`, and unit tests
  keep building services directly.
  - Data service interfaces return only `Task`/`Task<T>` (checked at startup).
  - Within one call a service may use `UserManager`, `SaveChangesAsync` or another data service
    (the lane is re-entrant within one async flow). Never block synchronously on a service task.
    Never start database work you do not await (`Task.Run`, fire-and-forget, `Task.WhenAll` over
    one context). Never call back into UI code from a service.
  - Interactive components reach the database only through data services. Never
    `@inject RelioDbContext`, `UserManager<RelioUser>` or `SignInManager<RelioUser>` in a
    component; only static SSR pages (`[ExcludeFromInteractiveRouting]`) may.
    `InteractiveComponentDataAccessTests` and `DataServiceRegistrationTests` enforce both rules.
  - The lane prevents overlap, not staleness: `AsNoTracking()` reads and `ChangeTracker.Clear()`
    after writes still apply.
  - Concurrency only shows on SQL Server. For a new data service, add its read to
    `DatabaseLaneSqlServerTests` next to another service's. For a page that composes several
    data-loading components, add it to the E2E `SqlServerPageLoadTests`.
- A multi-step write is **one** tracked `SaveChangesAsync` (a transaction on SQL Server). The InMemory
  provider used by `Relio.Data.Tests` ignores check constraints, unique indexes and case-insensitive
  collation, throws on `BeginTransaction`, and has no `ExecuteUpdate`/`ExecuteDelete`: prove
  constraints and indexes in `Relio.Data.IntegrationTests`, and do not reach for those APIs in a
  service.
- After changing the model, add a migration:
  `dotnet ef migrations add <Name> --project Relio.Data --startup-project Relio.Web`. A migration
  that reads columns it adds earlier in the same migration puts that SQL in `EXEC(N'...')` (SQL
  Server compiles a whole batch before running it, and `dotnet ef migrations script --idempotent`
  puts a migration in one batch); `AddPersonProfile` is the worked example, and it has a test that
  migrates a database holding real rows (`AddPersonProfileMigrationSqlServerTests`).

**Web (`Relio.Web`).**
- The real `ICurrentUser` is `Relio.Web.Security.AuthenticationStateCurrentUser` (epic #14, issue
  #15), reading the `ClaimTypes.NameIdentifier` claim off `AuthenticationStateProvider`'s current
  `ClaimsPrincipal` - see the "Accounts and authentication" section below for why this is
  `AuthenticationStateProvider`-backed rather than `HttpContext`-backed. Registered in `Program.cs`.

**Tests.**
- Prove cross-user isolation for every new service: create data as user A, then assert user B's
  service instance cannot read, list, update, archive, restore or delete it, or attach user B's own
  foreign-id rows (tags, etc.) to user A's entity. See
  `Relio.Data.Tests/People/PeopleServiceOwnershipTests.cs` for the shape these tests should take.
- These tests use the EF Core InMemory provider with a fake `ICurrentUser` and `TimeProvider` for
  fast feedback on service-level ownership logic. They intentionally stay even though
  `Relio.Data.IntegrationTests` now re-proves the same scenarios against SQL Server: InMemory runs
  in milliseconds with no external dependency, so keep it as the first signal and use the SQL
  Server project to prove anything InMemory cannot (constraints, indexes, migrations, real query
  translation).
- InMemory completes every async query synchronously, so it can never reveal two operations
  overlapping on one context. `DatabaseLaneSqlServerTests` proves the lane on SQL Server, with
  `SlowReaderInterceptor` making the overlap deterministic (and a control test showing the same
  reads do collide without the lane).
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

- `Relio.Domain.UserProfile` is the per-user settings row (`OwnerId`, `TimeZoneId`, and the optional
  `DisplayName` added by #18), following the user-scoped data pattern above. One row per user, enforced by a unique index on `OwnerId`
  (`UserProfileConfiguration`). `TimeZoneId` defaults to `"UTC"` and is always a valid IANA id -
  nothing writes to it without going through `TimeZoneIds.Parse` first.
- `Relio.Application.Time.TimeZoneIds` validates IANA time zone ids
  (`TimeZoneInfo.TryFindSystemTimeZoneById` - .NET resolves IANA ids via ICU on both Linux and
  Windows, so no Windows-id mapping is needed). Use `TryParse` on read paths that should fall back;
  use `Parse` (throws `InvalidTimeZoneIdException`) on write paths - an unknown id must be
  rejected, never silently coerced to UTC.
  `TimeZoneIds.GetAvailableIds()` is the (cached, sorted, distinct) list a time zone picker offers.
  On Linux/macOS it is every IANA zone the system knows; on Windows it is one IANA id per Windows
  zone (`TryConvertWindowsIdToIanaId`, so `Europe/Berlin` stands in for "W. Europe", and
  `Europe/Rome` is not listed). It is a convenience, never the set of valid ids: `TryParse` stays
  the source of truth, so a picker must also accept the stored zone, the browser's zone and typed
  values (`TimeZoneSettings` does) - and tests must never assert that a specific non-universal zone
  is in the list.
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
- `Person.LastContactedOn` (`DateOnly?`, column `date`, issue #23) is the user-calendar date of the
  most recent interaction, `null` for "never". Create/Update requests never carry it; issue #34 is the
  one feature that maintains it, from the interactions it records. Until then only seed data sets it.
  `Relio.Web.Time.DateDisplay.FormatRelative(date, today)` is the shared "Today / Yesterday / 12 days
  ago, then 3 March / 3 March 2025" phrase (future dates fall back to the plain date); "today" always
  comes from `IUserTimeZoneService.GetTodayAsync()`, never a clock in a component.
- A reminder or interaction date is a `DateOnly` - never convert it to/from UTC. A `Person`'s
  birthday is not, because its year is optional (issue #22): it is stored as three nullable int
  columns, `BirthdayYear`/`BirthdayMonth`/`BirthdayDay` (check constraints: month 1-12, day 1-31, year
  1-9999, and day and month come together), and read through the computed, EF-ignored
  `Person.Birthday` (`Relio.Domain.Birthday`, a value type with `TryCreate`/`IsValid`; year-less 29
  February is valid). Never use `Person.Birthday` inside a LINQ query - filter on the columns. Feb 29
  birthdays observe **Feb 28** in non-leap years (not Mar 1), with or without a year - see
  `UserCalendar.NextOccurrence(Birthday, today)`; the `DateOnly` overload delegates to it. A
  birthday with a year may not be after today **in the user's time zone** (enforced in
  `PersonProfileRules`, tested for Kiritimati and Pago Pago).
- `Relio.Web.Time.IBrowserTimeZoneReader`/`BrowserTimeZoneReader` read the browser's IANA time zone
  id via a self-hosted JS module (`wwwroot/js/timezone.js`, `Intl.DateTimeFormat().resolvedOptions().timeZone` -
  no third-party script), for interactive components. `Settings/TimeZoneSettings.razor` (#18) uses
  it to *suggest* the browser's zone when it differs from the saved one; the suggestion is never
  saved without the user pressing Save. Web-only; Application/Data services never depend on it. Sign-up (#15) cannot use
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
login, ever (epic #14's guardrail). Issue #16 hardened the minimal login/logout #15 shipped into
the real thing (lockout, remember-me, open-redirect protection, circuit revalidation - see its own
bullet below). Issue #17 added password reset by email (see its own bullet below). Issue #18 added account
settings (display name, time zone, change email, change password - see "Account settings" below).
Issue #19 added self-hosted administration (first-account administrator, `Registration:Mode`,
invitations, disabling accounts - see "Self-hosted administration" below). Issue #20 added optional
two-factor authentication with an authenticator app (see "Two-factor authentication" below),
completing epic #14.

- **The user entity** is `Relio.Data.Identity.RelioUser : IdentityUser`, in `Relio.Data` (not
  `Relio.Domain`, which stays free of any framework dependency - see the "User-scoped data
  pattern" section above). Every other owned entity still only ever stores its id as `OwnerId`;
  nothing takes a navigation property to it. `RelioDbContext` is an `IdentityDbContext<RelioUser>`
  (migration `AddIdentity`); audit-timestamp stamping in `SaveChanges`/`SaveChangesAsync` is
  unaffected, since Identity's own entities are not `IOwnedEntity`.
  `RelioUser.PendingEmail` (migration `AddRelioUserPendingEmail`, [PersonalData], max 256) holds
  an email address awaiting confirmation (#18); see "Account settings" below.
- **Identity options live in one place**: `Relio.Web.Identity.ServiceCollectionExtensions.AddRelioIdentity`
  - password policy (length 12+, mixed case, digit, symbol - length over complexity per NIST SP
  800-63B, with complexity rules added for defence in depth, given this stores private
  relationship data), unique email, the cookie scheme, the fallback authorization policy, and which
  `IEmailSender<RelioUser>` to register. Extend options here, not in `Program.cs`.
- **Login, lockout and session (issue #16)**: `Relio.Web.Identity.AccountOptions`, bound from the
  `Account` configuration section (`ServiceCollectionExtensions.BuildAccountOptions`, unit tested
  with no DI container, like `RequiresConfirmedAccount`) - safe defaults, override in
  `appsettings*.json`/environment variables/user secrets, never hard-code a different value inline:
  - `Account:Lockout` → `IdentityOptions.Lockout`: `MaxFailedAccessAttempts` (5), `DefaultLockoutTimeSpan`
    (15 minutes), `AllowedForNewUsers` (`true` - every account is lockout-protected from creation).
    `Login.razor` passes `lockoutOnFailure: true` to `PasswordSignInAsync`; a locked-out result shows
    a deliberately vague message (no exact unlock time, no attempt count) and - because
    `SignInManager` checks lockout *before* the password - the same message even with the correct
    password while still locked out.
  - `Account:Cookie:ExpireTimeSpan` (14 days) → the application cookie's sliding-expiration ticket
    lifetime, for both persistent and session cookies - see `AccountCookieOptions`'s remarks for why
    one value covers both. The cookie itself is `HttpOnly`, `SameSite=Lax`, and `Secure` (relaxed to
    `SameAsRequest` only in `Development`, where tests/local `dotnet run` serve plain HTTP with no
    HTTPS endpoint). "Remember me" (`Login.razor`'s checkbox, a plain HTML `<input type="checkbox">`
    - same static-SSR reasoning as every other account-page field) sets `isPersistent` per sign-in:
    checked → a persistent cookie (survives closing the browser); unchecked → a session cookie (no
    `Expires` attribute, gone once the browser closes).
  - **No account enumeration**: unknown email, wrong password, and (outside an active lockout) a
    just-triggered lockout all show the exact same message, `"Email or password is incorrect."` -
    only the *locked-out* message differs, and that is reachable for any real account regardless of
    whether the attacker knows the correct password. `Login.razor` logs every outcome
    (success/wrong-password/locked-out/unconfirmed) with the user id only, never the submitted
    email - see the gdpr-compliant skill.
  - **Open redirect protection**: `Relio.Web.Security.ReturnUrlValidator.GetSafeReturnUrl` (pure,
    unit tested) reduces a `returnUrl` to a same-origin path or falls back to `/` - rejects a
    different host, a protocol-relative `//evil.example`, and the `/\evil.example` backslash
    variant browsers normalize the same way. `Login.razor` and `RedirectToLogin.razor` both go
    through it before calling `NavigationManager.NavigateTo`.
  - **Logout**: `POST /Account/Logout` (see the next bullet) always redirects to `/Account/Login`
    directly, logs the signed-out user id, and - combined with `Program.cs`'s `Cache-Control:
    no-store` on every authenticated response - means the back button after signing out does not
    reveal a cached protected page.
  - **Circuit revalidation**: `Relio.Web.Security.RelioRevalidatingAuthenticationStateProvider`
    (registered in `Program.cs` in place of the plain cascading `AuthenticationStateProvider`)
    re-checks a connected circuit's security stamp and disabled flag every
    `Account:Session:ValidationInterval` (30 minutes by default, issue #19 - the same value feeds
    the cookie's `SecurityStampValidatorOptions.ValidationInterval`), the standard ASP.NET Core
    Identity Blazor template shape adapted to `RelioUser` - without it, a circuit that was already
    open when a session was revoked (signed out elsewhere, password changed, account disabled) would
    stay "signed in" until the circuit itself ended, no matter how long that took. `SessionIsValid`
    is the pure function behind it. Note that it flips the circuit's authentication state, which
    `AuthorizeView` (the app bar) follows immediately; `AuthorizeRouteView` only enforces pages that
    carry an `[Authorize]` attribute, so a shell page already on screen stays rendered until the
    next navigation (any request or service call is refused).
- **Account pages** live in `Relio.Web/Components/Account/Pages` (`Register.razor`, `Login.razor`,
  `RegisterConfirmation.razor`, `ConfirmEmail.razor`, `AccessDenied.razor`, the reset pages,
  `ConfirmEmailChange.razor`, and the signed-in `Pages/Manage/ChangePassword.razor` and
  `Pages/Manage/Email.razor` - the Manage pages are `[Authorize]`, not `[AllowAnonymous]` - and
  the two-factor pages: `LoginWith2fa.razor`, `LoginWithRecoveryCode.razor`,
  `Pages/Manage/TwoFactorAuthentication.razor`, `EnableAuthenticator.razor`,
  `GenerateRecoveryCodes.razor`, `Disable2fa.razor`, `ResetAuthenticator.razor`), with shared
  infrastructure in `Relio.Web/Components/Account` (`IdentityComponentsEndpointRouteBuilderExtensions`
  maps the `POST /Account/Logout` minimal API endpoint - logout, like register/login, must write
  directly to the HTTP response to clear the auth cookie) and `Components/Account/Shared`
  (`AccountLayout`, `RedirectToLogin`, `QrCodeImage`, `RecoveryCodesPanel`). Add new account pages to this same folder
  (`Pages/Manage` for ones that need a signed-in user).
- **Why static SSR**: `SignInManager`/`UserManager` need to write the auth cookie directly to the
  HTTP response of the request that is actually submitting the form - something an interactive
  Blazor Server circuit (a SignalR connection, not a request/response pair) cannot do. Every account
  page therefore carries `@attribute [ExcludeFromInteractiveRouting]` (plus `[AllowAnonymous]`, or
  `[Authorize]` for the signed-in `Manage` pages). That attribute only works because `App.razor`
  picks the `<Routes>` render mode per request - `InteractiveServer` when
  `HttpContext.AcceptsInteractiveRouting()`, otherwise none (plain static SSR). With an
  unconditional `InteractiveServer` mode the circuit that starts after an excluded page loads cannot
  find it in the interactive route table and replaces it with the Not Found page (issue #18 hit
  this on the first signed-in static page; earlier account pages were only spared because their
  scripts never loaded for anonymous visitors, which was itself a bug - see the next bullet).
- **Static assets and anonymous pages**: `app.MapStaticAssets().AllowAnonymous()` in `Program.cs` -
  without it the fallback authorization policy redirects every asset request (app.css, MudBlazor,
  `_framework/blazor.web.js`, fonts, `js/*.js`, favicon) from a signed-out visitor to the login page,
  leaving the account pages unstyled and script-less. So `blazor.web.js` *does* load on the static
  SSR account pages, and **enhanced navigation** is active there: a link between two account pages
  is a fetch + DOM patch, not a full page load. Consequences: (1) every form already ends its
  success path in `NavigateTo(..., forceLoad: true)` and none opts into `data-enhance`, so form
  posts and cookie-writing redirects stay plain full requests - keep it that way; (2) scripts inside
  patched-in content do not run, so a page that depends on an inline `<script>` (`Register.razor`'s
  time zone field) must be reached with `data-enhance-nav="false"` on every link to it (see
  `Login.razor`). `AnonymousAssetsTests` guards the first, `AnonymousEnhancedNavigationTests` the
  second.
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
  endpoints, the `POST /Account/Logout` endpoint and `MapStaticAssets()` - static assets must be
  anonymous or signed-out visitors get no CSS/JS). The cookie's `LoginPath` means an anonymous request to a protected page redirects to
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
- **Password reset (issue #17)**: `/Account/ForgotPassword` → `/Account/ResetPassword` →
  `/Account/ResetPasswordConfirmation`, under `Components/Account/Pages` alongside every other
  account page (same static-SSR/`[AllowAnonymous]` reasoning as above). "Forgot your password?" on
  `Login.razor` links to it.
  - **No account enumeration**: `ForgotPassword.razor` redirects to the exact same
    `ForgotPasswordConfirmation` page with the exact same message ("If an account exists for that
    email address, we've sent a link to reset your password.") whether the submitted email matches
    no account, an unconfirmed account, or a confirmed one - an unconfirmed account can still reset
    its password (it still can't sign in afterwards until it confirms; there is no security reason
    to also refuse the reset). The found/not-found branches also do near-identical work: both
    generate a password reset token (`UserManager.GeneratePasswordResetTokenAsync`), the not-found
    one against a throwaway, never-persisted `RelioUser` instead of skipping it outright - so the
    page's response time is not an easy account-enumeration oracle on top of the identical message.
    Only the actual email send (and its own `NullEmailSender`/`SmtpEmailSender` branch) differs.
    Rate-limiting repeated requests is explicitly out of scope (issue #60).
  - **`Email:Provider=None`**: `ForgotPassword.razor` still shows an info note ("Password reset by
    email isn't available on this Relio instance. Ask your administrator.") - this is safe because
    it is a static, instance-wide fact, not tied to the submitted email, so it reveals nothing
    about any particular account. The found/not-found behaviour above is unchanged: a found user
    still goes through `NullEmailSender`, which logs its own per-call warning (never the email
    address) that a reset was requested with no provider configured.
  - **Expiry and a dedicated token provider**: password reset tokens expire after
    `Account:PasswordReset:TokenLifespan` (default 1 hour, `AccountOptions.PasswordReset`).
    Identity's "Default" token provider backs *both* `IdentityOptions.Tokens.EmailConfirmationTokenProvider`
    and `PasswordResetTokenProvider` out of the box, so simply configuring
    `DataProtectionTokenProviderOptions.TokenLifespan` would have also changed email
    confirmation's. Instead, `Relio.Web.Identity.PasswordResetTokenProvider<TUser>` (a
    `DataProtectorTokenProvider<TUser>` subclass with its own `PasswordResetTokenProviderOptions`
    options type) is registered under its own provider name and wired up as
    `IdentityOptions.Tokens.PasswordResetTokenProvider` in
    `ServiceCollectionExtensions.AddRelioIdentity` - email confirmation keeps using "Default"
    untouched. See that type's remarks for the official ASP.NET Core guidance this follows.
  - **Single use**: `UserManager.ResetPasswordAsync` rotates the account's security stamp on
    success (Identity's own password-change behaviour), and the reset token is bound to the
    security stamp that was current when it was issued - so reusing an already-used link fails
    Identity's own token verification with error code `InvalidToken`, the same code an
    expired/tampered link produces. `ResetPassword.razor` shows the identical calm
    `InvalidLinkMessage` ("This password reset link is invalid or has expired.") with a link back
    to `/Account/ForgotPassword` for all three (invalid, expired, reused) plus a missing/unknown
    user id - never distinguishing them, same no-enumeration reasoning as above. A password-policy
    failure (e.g. too short) is shown separately and keeps the link valid for another attempt.
  - **Lockout cleared on success**: a successful reset also calls
    `UserManager.ResetAccessFailedCountAsync`/`SetLockoutEndDateAsync(user, null)` - the whole
    point of regaining access by email is to not still be locked out afterwards. The security
    stamp rotation above also signs out every other active session, at its next
    `RelioRevalidatingAuthenticationStateProvider` check (within 30 minutes - see "Circuit
    revalidation" above).
- **Account settings (issue #18)**: `/settings` (`Components/Pages/Settings.razor`) is an interactive
  hub made of small components in `Components/Settings/` - `DisplayNameSettings`,
  `TimeZoneSettings`, `SignInSecuritySettings` - plus a disabled "Reminder email preferences" button
  (`TODO(#40)`, enabled once notification preferences exist) and the existing appearance control.
  Anything that has to write the auth cookie lives on a **static SSR** page instead, for the reason
  under "Why static SSR" above: `Pages/Manage/ChangePassword.razor` and `Pages/Manage/Email.razor`
  (`[Authorize]` + `[ExcludeFromInteractiveRouting]`, plain `<input name="Input.X">` fields,
  `AccountLayout`) and the anonymous `ConfirmEmailChange.razor`. The hub links to them with plain
  `Href`s - the interactive router answers a click on an excluded page with a full page load
  (covered by `AccountSettingsTests`). Don't add a drawer entry: the shell
  keeps its five routes (`NavMenuTests`); the signed-in email in the app bar links to `/settings`.
  Issue #25 added a "Relationship types and tags" section to the hub: two link buttons to the sub-pages
  `/settings/relationship-types` and `/settings/tags` (see "Relationship types and tags (issue #25)"
  under People). The hub makes **no data calls** for them - its sibling components already share one
  circuit `DbContext` - and the sub-pages need no drawer entry (the Settings link matches by prefix).
  - **Display name** lives on `UserProfile.DisplayName` (nullable, `DisplayNameMaxLength` = 100),
    read/written through `Relio.Application.Profile.IUserProfileService` (implemented in
    `Relio.Data.Profile.UserProfileService`, same user-scoped pattern as `UserTimeZoneService`; it
    trims, treats whitespace as "cleared", rejects longer values with `ArgumentException` before
    touching the database, and creates the profile with the default time zone if missing). It is
    deliberately *not* a column on `RelioUser`: it is product profile data, and Domain/Application
    never see Identity.
  - **Change password**: `UserManager.ChangePasswordAsync` rotates the security stamp, which signs
    out every other session at its next validation (the application cookie's
    `SecurityStampValidatorOptions.ValidationInterval`, 30 minutes by default, and the circuit
    revalidation above). The session that changed it would be signed out too, so the page calls
    `SignInManager.RefreshSignInAsync(user)` right after - always do this after any call that rotates
    the stamp for the signed-in user (`ChangePasswordAsync`, `SetEmailAsync`, `SetUserNameAsync`,
    `ChangeEmailAsync`, and for #20 `SetTwoFactorEnabledAsync` and `ResetAuthenticatorKeyAsync` -
    including the setup page's first-key creation on a plain GET). A wrong current password and a policy failure are reported separately and
    never reveal anything about other accounts.
  - **Change email** always requires the current password. `Email:Provider=Smtp`
    (`EmailOptions.CanSendEmail`): the new address is stored as `RelioUser.PendingEmail` (so the link
    carries only `userId` and an opaque change-email token, never an email address - see the
    gdpr-compliant skill) and sent through `IEmailSender<RelioUser>.SendConfirmationLinkAsync`, which
    is why `SmtpEmailSender`'s confirmation wording is neutral. `ConfirmEmailChange.razor` reads the
    new address from `PendingEmail`, calls `ChangeEmailAsync` (token bound to that address and the
    security stamp - so the link is **single use**, and a **newer request overwrites `PendingEmail`
    and invalidates the older link**), clears `PendingEmail`, keeps `UserName` equal to the email,
    and refreshes the session only when the browser is signed in as that same user. An address that
    is already registered gets the identical "we've sent a link" response and no email (the same
    token work is done and discarded), so it cannot be used to enumerate accounts. Invalid, expired,
    used and superseded links all show the same message. `Email:Provider=None` (the default):
    there is nothing to send, so the change **applies immediately** (`SetEmailAsync` +
    `SetUserNameAsync`) and the page says so up front; a taken address is rejected plainly, as
    registration already does. Notifying the old address is a follow-up.
  - Log user ids only, never email addresses or links (gdpr-compliant skill).
- **Self-hosted administration (issue #19)**: who runs an instance and who may join it. All of it is
  instance administration, not user content, and none of it can reach anyone's people, notes or
  moments.
  - **One role, seeded as model data.** ASP.NET Core Identity roles with a single role,
    `Relio.Application.Administration.RelioRoles.Administrator`. `AddRelioIdentity` calls
    `.AddRoles<IdentityRole>()` **before** `.AddEntityFrameworkStores<RelioDbContext>()` (the other
    order wires a user-only store and every role call throws `NotSupportedException`); test helpers
    that build a `UserManager` (`UserManagerTestFactory`) must do the same. The role row is
    `HasData` in `AdministratorRoleConfiguration` with a **fixed id and concurrency stamp** - never
    change them - so it ships in the migration (no runtime "create the role if missing" race) and
    `EnsureCreated` (InMemory) creates it too. An InMemory `RelioDbContext` used with roles must call
    `Database.EnsureCreated()`.
  - **First-account rule.** `Relio.Data.Administration.AccountRegistrationService` (behind
    `IAccountRegistrationService`; `Register.razor` is now a thin page over it, and the new user's
    `UserProfile` is written there) makes a new account the Administrator only if it is the
    **only** account in `AspNetUsers` right after its own insert - not "no Administrator exists yet",
    which would let a stranger claim an instance that predates #19. Two layers keep it race safe: a
    process-wide singleton `RegistrationLock` serializes registrations, and the decision is made
    after the insert (`wasEmpty && CountAsync() == 1`), so two processes sharing a database can never
    both be promoted (worst case: nobody is, recoverable below). No transactions/`ExecuteUpdate` are
    used so the logic runs on InMemory.
  - **`Registration:Mode`** = `Open` (default) | `InviteOnly` | `Closed`, plus
    `Registration:InvitationLifetime` (7 days, must be > 0), parsed eagerly by
    `ServiceCollectionExtensions.BuildRegistrationOptions` so a typo (`InviteOnyl`, `1`, `99`) stops the
    app at startup instead of silently leaving sign-up open; names are case-insensitive. **While the
    instance has no accounts at all, registration is allowed in every mode** (otherwise a fresh
    Closed/InviteOnly install could never get an Administrator) and the page says the first account
    will be the administrator. The service re-checks the mode on every `RegisterAsync`, so a crafted
    POST creates nothing; the page is only the friendly front. `Closed` answers the page with **HTTP
    403** and a "Sign-up is closed" panel (not 404: Blazor treats a component-set 404 as not-found and
    the status code pages middleware replaces the body); the login page hides "Create an account"
    unless sign-up is possible.
  - **Invitations** (`RegistrationInvitation`, table `RegistrationInvitations`; Identity-adjacent
    infrastructure, **not** `IOwnedEntity`): a link `/Account/Register?invite=<token>`, never
    containing the email. 256-bit CSPRNG token, only its SHA-256 hash is stored
    (`InvitationTokens`), bound to the invited email (the form pre-fills it read-only; a different
    address gets `InvitationEmailMismatch` and the invitation stays usable), **single use** (deleted
    on successful registration; a rejected password consumes nothing), expiring, revocable, and a
    newer invitation for the same address replaces the older. Only created and honoured in
    `InviteOnly`. Relio does **not** email invitations: the administrator copies the link from the page
    (shown once) - `IEmailSender<RelioUser>` needs a `RelioUser` recipient, and emailing is a
    follow-up. Expired rows are purged on admin operations and at startup (loaded and removed, not
    `ExecuteDelete`, for InMemory).
  - **Disabling accounts** uses a dedicated `RelioUser.IsDisabled` flag, **not** Identity lockout:
    `ResetPassword.razor` clears lockout, so a lockout-based disable could be undone by the user it was
    applied to. `UserAdministrationService.DisableAccountAsync` sets the flag and rotates the security
    stamp in one save (set the flag first - `UpdateSecurityStampAsync` saves the whole user), which ends
    open sessions within `Account:Session:ValidationInterval`. Enabling restores everything exactly.
    `RelioSignInManager` (registered with `.AddSignInManager<RelioSignInManager>()`) enforces it:
    `PasswordSignInAsync` verifies the password in a *private* method, so overriding
    `CheckPasswordSignInAsync` does nothing, and `CanSignInAsync` runs *before* the password is checked
    (it would reveal which accounts are disabled). The one protected hook reached only after a
    successful password check (and before two-factor, #20) is `SignInOrTwoFactorAsync`: it returns
    `RelioSignInResult.Disabled` (still `IsNotAllowed`), so a wrong password gets the usual "Email or
    password is incorrect." and counts towards lockout, and only the right password sees "This account has
    been disabled...". `ValidateSecurityStampAsync(ClaimsPrincipal)` rejects disabled users (ending
    cookie sessions) and `SignInWithClaimsAsync` is a backstop that never issues a cookie to one.
  - **The administrator can't lock everyone out.** An administrator cannot disable their own account,
    and a disabled administrator cannot administer, so an active administrator always remains.
    `UserAdministrationService` re-reads role and `IsDisabled` from the database with **untracked**
    queries on every call (not from claims, which only refresh at sign-in, and not through the tracked
    `UserManager.FindByIdAsync`, because a circuit's `DbContext` lives as long as the circuit and would
    answer from a stale tracked copy); when it loads the target of a change it reloads it first for the
    same reason.
  - **`Administration:AdministratorEmail`** (unset by default, never in `appsettings.json` - it names a
    person) is how an instance that predates #19 gets its first Administrator, and the recovery path if
    nobody is one: `AdministratorBootstrapper` (run at startup after the demo seeder) promotes that
    account, idempotently; an unknown address promotes nobody and logs a warning **without** the address;
    accounts-but-no-Administrator logs a warning telling the operator about this setting. A role is read
    into the sign-in cookie at sign-in, so a promoted account needs a fresh sign-in before the nav link
    appears.
  - **The admin page** `/admin/users` (`Components/Pages/AdminUsers.razor` - not `Administration.razor`, which
    would clash with the `Relio.Web.Components.Administration` namespace of its components) is gated by the
    `RelioPolicies.Administrator` policy and shows the account list (`AccountList`: email and status
    chips - Administrator, You, Disabled, Locked out, Unconfirmed when confirmation is required), the
    registration mode (read-only: it is configuration), and, in `InviteOnly`, the `InvitationPanel`. The
    drawer link is an `AuthorizeView` on the same policy (so `NavMenu` needs authorization services in
    tests). `Routes.razor`'s `NotAuthorized` sends a signed-in user to `/Account/AccessDenied`
    (`RedirectToAccessDenied`) and only an anonymous one to login - otherwise a non-administrator
    navigating inside a circuit would be bounced to the login page.
  - **Administrators never see user content, by construction.** `IUserAdministrationService` and
    `IAccountRegistrationService` only exchange primitives and the records in
    `Relio.Application.Administration`; `AdministrationSurfaceTests` reflects over them and fails if any
    `Relio.Domain` type or `RelioUser` appears. The admin list has no per-row links and never shows
    `UserProfile.DisplayName` (owned data). **Never add a role-based bypass to an owned-data service**
    (`IPeopleService` and friends stay strictly `OwnerId == current user`, whatever the caller's role).
  - Log ids only: never an email, an invitation token or an invitation link (gdpr-compliant skill).
  - **Upgrade note**: the migration `AddSelfHostedAdministration` adds `AspNetUsers.IsDisabled`, the
    `RegistrationInvitations` table and inserts the Administrator role row. If an operator had manually
    created a role named "Administrator" the insert would hit `RoleNameIndex` (the project is
    unreleased; delete the manual role first). Apply migrations before starting the new version: startup
    queries the new table.
- **Two-factor authentication (issue #20)**: optional, per account, authenticator app (TOTP) only - no
  SMS, no email codes, no passkeys. It needed **no migration**: `AspNetUsers.TwoFactorEnabled` and
  `AspNetUserTokens` (Identity keeps the authenticator key and the recovery codes there, under the
  `[AspNetUserStore]` provider) already exist from `AddIdentity`. Identity's authenticator token
  provider is registered by `AddDefaultTokenProviders` already.
  - **Pages** (all static SSR: `[ExcludeFromInteractiveRouting]`, `AccountLayout`, plain HTML inputs).
    Sign-in: `LoginWith2fa` (`/Account/LoginWith2fa`) and `LoginWithRecoveryCode`, both
    `[AllowAnonymous]`. Management, `[Authorize]`: `Manage/TwoFactorAuthentication` (status hub, links
    only), `EnableAuthenticator` (key + QR + verify), `GenerateRecoveryCodes`, `Disable2fa`,
    `ResetAuthenticator`. `/settings` shows the state through `Application.Accounts.ITwoFactorStatusService`
    (implemented in `Relio.Data.Identity.TwoFactorStatusService`) and links to the hub. The settings
    hub is interactive and its `DbContext` lives as long as the circuit, so the service reads
    **untracked** (it queries `AspNetUsers`/`AspNetUserTokens` directly; a test proves its recovery-code
    count always equals `UserManager.CountRecoveryCodesAsync`, which guards the private token names it
    mirrors). Do the same for any status read in an interactive component.
  - **Sign-in flow.** `Login.razor`'s `PasswordSignInAsync` returns `RequiresTwoFactor` (Identity has
    written the 5-minute `Identity.TwoFactorUserId` cookie holding only the user id); `Login` hands
    `returnUrl` (already reduced by `ReturnUrlValidator`) and `rememberMe` to `/Account/LoginWith2fa` as
    query parameters, and **both** two-factor pages run `ReturnUrlValidator.GetSafeReturnUrl` again
    (a query string is editable). `EditForm` has no `action`, so the POST keeps the query string. With no
    pending sign-in (no cookie, or it ran out) both pages show a calm "your sign-in timed out" message
    and keep a hidden form so a late POST still finds it. `Login` checks branches in this order:
    succeeded, `RequiresTwoFactor`, `RelioSignInResult.Disabled`, locked out, not allowed, wrong
    password - **`Disabled` is also `IsNotAllowed`, so it must come before it**. A malformed code
    (not exactly six digits after removing spaces/hyphens, `TwoFactorCodes`) never reaches Identity, so a
    typo is not a failed attempt. Messages shared by the sign-in pages live in `Identity/SignInMessages`.
  - **No "remember this device".** Always `rememberClient: false`; `RememberTwoFactorClientAsync` is
    never called, so every sign-in asks for a code. A remembered-device cookie is a long-lived
    bearer token that skips the second factor and would need its own revocation and "forget this
    device" story; revisit as a follow-up if people ask. The two-factor
    cookies are still hardened (`HttpOnly`, `SameSite=Lax`, `Secure` outside Development) by the shared
    `ApplyCookieSecurity` in `AddRelioIdentity`. A recovery-code sign-in always issues a *session*
    cookie, whatever "Remember me" said (Identity's `TwoFactorRecoveryCodeSignInAsync` has no such
    parameter); the authenticator-code sign-in honours it.
  - **One lockout budget, and recovery-code hardening.** With two-factor authentication on, a correct
    password no longer resets the failed-attempt counter (Identity resets it only when the whole
    sign-in completes), and a wrong authenticator code is counted by Identity itself, so password and
    code failures add up towards `Account:Lockout:MaxFailedAccessAttempts`. Identity's
    `TwoFactorRecoveryCodeSignInAsync` has **no `PreSignInCheck` (a locked-out account could use a
    valid code) and counts nothing**, so `RelioSignInManager` overrides it: refuse disabled, run
    `PreSignInCheck`, then count a wrong code like any other guess. Do not "fix" that by overriding
    `CanSignInAsync` (it runs before the password check; see the class remarks). Locked-out and
    disabled messages are the same as the password step's.
  - **Disabled accounts (#19) at the code step.** `SignInOrTwoFactorAsync` already stops a disabled
    user before the cookie is issued; `RelioSignInManager` also overrides
    `TwoFactorAuthenticatorSignInAsync`/`TwoFactorSignInAsync`/`TwoFactorRecoveryCodeSignInAsync` so an
    account disabled *during* the five-minute window is refused with `RelioSignInResult.Disabled` and
    the two-factor cookie is cleared.
  - **Every change needs the current password** (`UserManager.CheckPasswordAsync`): turning on (together
    with a valid code from the app), turning off, switching apps, regenerating codes. Turning on =
    `TwoFactorAccountExtensions.TurnOnTwoFactorAsync` (`SetTwoFactorEnabledAsync(true)` + always 10
    fresh codes); turning off = `TurnOffTwoFactorAsync` (disable + **rotate the key** + clear the
    codes, so nothing from the old setup ever works again); "set up a different app" = turn off, then
    the setup page with the new key (two-factor authentication stays off until the new app proves it
    works, so a half-finished switch can never lock anyone out). The setup page creates the key on
    first visit (`ResetAuthenticatorKeyAsync` rotates the stamp, so it calls `RefreshSignInAsync`
    even on that GET); both turn-on and turn-off rotate the stamp too, so every other session ends
    at its next validation and the page says so. The setup page redirects away when it is already on
    (it must never silently replace a working key); the three change pages redirect when it is off.
  - **Recovery codes are shown once, in the response to the POST that created them**
    (`RecoveryCodesPanel`) - never in a redirect, query string, TempData or cookie. They are
    `XXXXX-XXXXX`, ten per batch, stored by Identity as one `;`-joined string and matched exactly (the
    login page upper-cases and strips spaces first). Authenticated responses already carry
    `Cache-Control: no-store`. The status page shows only a count and warns calmly at 3 or fewer
    (`TwoFactorStatus.LowRecoveryCodeThreshold`).
  - **QR code**: generated server-side by `Net.Codecrete.QrCodeGenerator` (MIT, no dependencies) into
    an inline SVG (`Identity/QrCodeSvg`, `Components/Account/Shared/QrCodeImage.razor`): one rect and
    one path, no script, no `<image>`, no request - the secret never leaves the response. Colours come
    from the `qr-ink`/`qr-ground` tokens through CSS classes (dark on light in **both** themes:
    scanners cannot read an inverted code; never use `fill="..."` attributes). The `otpauth://` URI
    (`Identity/AuthenticatorUri`) uses the constant issuer `Relio`; the key is also shown grouped in
    fours for manual entry. **Never log the key, the URI, a code or a recovery code** - ids and counts only.
  - **Secrets at rest**: Identity's built-in token storage keeps the authenticator key and the recovery
    codes in plain text in `AspNetUserTokens` (the default; `IProtectedUserStore`/personal-data
    protection is not enabled). Relio does not configure a persisted Data Protection key ring, so
    encrypting them would risk making every account's key unreadable after a restart; instead the
    database must be protected like the password hashes' database. Follow-up: protect two-factor
    secrets at rest once a persisted key ring is a supported configuration.
  - **Password reset (#17) does not turn two-factor authentication off** (a test locks it in): the
    reset link proves control of the mailbox, not of the phone. Someone who loses their phone *and*
    every recovery code cannot recover in-app; the pages tell them to contact the person who runs the
    instance, and the README documents the operator's SQL fix. An administrator action to turn it off
    is a follow-up.
- **Demo data**: `Relio.Data.Seeding.DemoDataSeeder` creates a `demo@relio.local` account (test/demo
  password only, see its XML docs) - which is also made an **Administrator** (idempotently, on every
  run, so a demo database that predates #19 gets the role) - with realistic sample people (one
  archived, one with a Feb 29 birthday, one with a birthday without a year, most with a relationship
  type from the default list the seeder also creates, two with "how we met" and multi-line details,
  most with a different `LastContactedOn` counted back from today in the demo zone - today, yesterday,
  3, 12, 45 and 200 days ago, two never - so the list's "last contacted" sort and wording have
  something to show; a demo database that was seeded earlier is not backfilled)
  and a non-UTC time zone, writing directly through
  `RelioDbContext` rather than through `IPeopleService`/`IUserTimeZoneService` - those require a
  signed-in `ICurrentUser`, which does not exist at startup (`AccountRegistrationService` uses the same
  escape hatch for the one request that creates a brand new user's own `UserProfile`, for the same
  reason). Accounts registered while demo data is on are **not** administrators (the demo account is
  never "first"). Runs once at startup, only when `DemoData:Enabled=true` (default `false`) and the
  environment is not Production (refuses, logging an error, otherwise); idempotent. See the README's
  "Run locally without SQL Server" section.
- **Tests**: `Relio.Web.E2ETests` signs in through the real login page -
  `RelioAppFixture.SignInAsDemoAsync(page)` - before visiting any protected route; every existing
  shell test (`NavigationTests`, `DashboardTests`, etc.) does this first. `RelioWebAppFactory`
  enables `DemoData:Enabled` for the shared fixture. To exercise a differently-configured app (e.g.
  `Email:Provider=Smtp` with a test email sink, see `EmailConfirmationTests` and `PasswordResetTests`,
  the latter also setting a one-second `Account:PasswordReset:TokenLifespan` to prove expiry),
  construct a *new* `RelioWebAppFactory(configureTestServices: ...)` rather than the base class's
  `WithWebHostBuilder` - see `RelioWebAppFactory`'s remarks for why that matters.
  `AuthenticationTests` (issue #16) registers a fresh account per test (its own
  `RegisterNewUserAsync` helper) rather than reusing the shared demo user for anything that fails a
  sign-in on purpose (wrong password, lockout) - the demo user is shared across the whole
  collection, and locking it out (even temporarily) would make every other test that signs in as it
  flaky.
  Tests that change account state (password, email, time zone - `AccountSettingsTests`,
  `ChangePasswordTests`, `ChangeEmailTests`) also register a fresh `...@example.com` user and never
  touch the demo user; their shared helpers live in `Infrastructure/AccountTestHelpers.cs`
  (`RegisterAsync`, `LoginAsync`, `SignOutAsync`, `CreateSmtpFactory`, `CreateFactory`, `GetUserAsync`).
  `TestEmailSink` records each confirmation email's recipient as well as its link. To prove another
  session is signed out, use a variant factory that sets `SecurityStampValidatorOptions.ValidationInterval`
  to `TimeSpan.Zero` through `configureTestServices` (the default is 30 minutes). On static SSR pages
  navigate with `page.GotoAsync` after asserting the link's `href` rather than clicking links
  (Blazor's enhanced navigation races Playwright's actionability checks there), and wait on a
  `data-testid` locator - static pages never set `data-app-ready`.
  Administration (#19): the shared fixture app always has exactly one administrator - the demo
  account - and every account a test registers is an ordinary user, so `AdministrationTests` act on
  freshly registered users and never disable, demote or change the demo account. Anything that needs a
  different `Registration:Mode`, an instance with **no** accounts (the first-account rule), or a short
  `Account:Session:ValidationInterval` runs in a `VariantApp` (`Infrastructure/VariantApp.cs`): a second
  independent app (own port and InMemory database) created with `VariantApp.Create(fixture, environment,
  seedDemoData:)`. Settings read while `Program.cs` builds the app are environment variables
  (`Registration__Mode`, `Account__Session__ValidationInterval`), set before the host is built and reset
  right after (process-wide, safe only because the collection runs serially); `seedDemoData: false` works
  through `PostConfigure<DemoDataOptions>` because `RelioWebAppFactory.CreateHost` always sets
  `DemoData__Enabled=true`. `RegistrationModeTests` covers sign-up control and invitations,
  `AdministrationTests` the page, disabling and the "never sees other users' people" rule. Unit tests
  that need roles use `Relio.Data.Tests/Administration/AdministrationTestHarness.cs`.
  `Relio.Data.IntegrationTests` shares one SQL Server database across tests, so the #19 tests there
  only assert schema facts (seeded role, unique indexes) and never "there is exactly one account".
  Two-factor authentication (#20): Identity's authenticator provider can only *verify* a code -
  `GenerateTwoFactorTokenAsync` returns an empty string for it - so tests compute codes themselves with
  `Relio.Web.E2ETests/Infrastructure/Totp.cs` (RFC 6238; `TotpTests` pins it to the RFC vector; it is
  also compiled into `Relio.Web.Tests` as a linked file). `Infrastructure/TwoFactorTestHelpers.cs`
  creates a fresh user with two-factor authentication on (`CreateTwoFactorUserAsync`: registers through
  the real page, then turns it on through the app's services) and drives the two sign-in steps. **Never
  turn two-factor authentication on for the shared demo user** - every other test signs in as it.
  `Relio.Web.Tests` drives `RelioSignInManager` across several scopes with `FakeAuthenticationService`
  (holds the two-factor cookie in memory); the sign-in manager tests are the contract for the shared
  lockout budget. `Relio.Data.IntegrationTests` references the ASP.NET Core shared framework (it builds a
  real `UserManager`), and can be run locally without Docker against LocalDB:
  `ConnectionStrings__Relio='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=False;TrustServerCertificate=True;'`.

## People

Established by issue #22 under epic #21. The routes are pages, not dialogs: `/people` (the list),
`/people/new`, `/people/{PersonId:guid}` (the profile) and, in later issues, `/people/{id}/edit`,
`/people/{id}/merge` and `/people/import`. Dialogs (`ConfirmDialog`) are for short confirmations only.

- **All of them are interactive** (MudBlazor inputs only bind over a circuit - see the static SSR note
  under "Accounts and authentication"); never add `[ExcludeFromInteractiveRouting]` to a people page.
  Each page has exactly one `h1` (`FocusOnNavigate` targets it) and sets no `AutoFocus`.
- **`Components/People/PersonForm.razor`** is the one form for a person: add (#22), edit (#24) and
  the possible-duplicate warning (#27, see below) plug into it. It binds to `PersonFormModel`, shows one message
  per field from `PersonFormMessages`, and is a `<form novalidate>` (so the browser's own "fill out
  this field" bubble never pre-empts Relio's message) **with no submit button**: Save is a
  `ButtonType.Button` with `OnClick`, because Enter inside the tag or label autocomplete would otherwise
  submit the whole form (and Enter is how a tag is chosen). A form without a submit button and with
  several text inputs is never submitted implicitly, so Enter in a text field does nothing. Loop
  `MudSelectItem`s with `foreach`, never `for`: the item content renders after the loop has finished.
  The services that fill its pickers (`IRelationshipTypeService`, then `ITagService`) are awaited one
  after the other, which is the simplest thing; the database lane would queue them anyway
  (`ITagService` is registered with `AddDataService` like every data service, and
  `DatabaseLaneSqlServerTests` covers the edit page's pickers loading while a save runs).
- **Browser tab titles stay generic** (`Person - Relio`, `Add a person - Relio`): titles land in
  browser history and tab lists, and a person's name is private. A person that does not exist and
  one that belongs to someone else show the identical "This person isn't in your list" panel.
- User-written text (names, nickname, how you met, details) is set in the serif (`rl-serif` in
  inputs, `rl-entry-text` on the profile) and rendered as text, never as markup; line breaks are kept
  with `rl-multiline`. Never log it.
- **The list (issue #23).** `/people` reads through `IPeopleService.ListPageAsync(PeopleListQuery)`
  -> `PeopleListResult` (a `PagedResult<PersonListItem>` plus `ActiveCount`/`ArchivedCount`, which
  describe the whole account, not the filter). `PersonListItem` is a projection: only what a row shows
  (no nickname, how-we-met, details or birthday - data minimisation). Offset paging, `PageSize` 50
  (`PeopleListQuery.DefaultPageSize`, clamped 1 to `MaxPageSize` 100); a page below 1 or past the end
  is clamped by the service and `PagedResult.Page` says which page came back; an undefined `Sort`
  throws before any query. `ListAsync` stays for callers that need everyone (pickers, exports) - the
  list screen no longer uses it. **Every ordering ends with `Id`** so a tie never reshuffles between
  pages: Name = first, last name; RecentlyAdded = `CreatedAtUtc` descending; LastContacted =
  `LastContactedOn` descending with **nulls last spelled out** (`OrderBy(p => p.LastContactedOn == null)`
  first), then name. Order by the real columns - `DisplayName` is not one.
- **The view lives in the address, and only there.** `/people?sort=name|added|contacted&archived=true&page=N`,
  parsed and written by the pure `Components/People/PeopleListQueryString` (defaults left out, fixed
  parameter order, anything invalid falls back to the default, never an error). Only those three values
  ever go in an address - never a name, search text or id (history, proxy logs, referrers). The query
  parameters are `string?` on purpose: a `bool?`/`int?` parameter throws on a value it cannot read. The
  drawer link `/people` resets to the defaults. `People.razor` loads in **`OnParametersSetAsync`**, not
  `OnInitializedAsync` (a query-only navigation re-uses the component), skips a query it already asked
  for, and runs loads **one at a time behind a `SemaphoreSlim` with a version counter** because the
  circuit's `DbContext` refuses two concurrent queries. Handlers (`Sort`, `Show archived`, pager) only
  `NavigateTo` a new address - a push, so Back restores the view - and a new sort or filter returns to
  page 1; nothing navigates from a lifecycle method. Words for the list (sort labels, "Last contacted 12
  days ago", "Not contacted yet", the count line) live in `PeopleListText`. Archived rows are mixed in,
  labelled "Archived" with the `Inventory2` icon; there is no "archived only" view. After a page change
  the scroll position is not reset yet (follow-up; no JS for it so far).
- **Testing the list.** Guid order differs between SQL Server and .NET, so tie-break tests assert
  distinct ids and a stable order, never a particular one; InMemory sorts strings case-sensitively and
  SQL Server (the CI collation) does not, so the case-insensitive test lives in `Relio.Data.IntegrationTests`.
  To backdate `CreatedAtUtc` in a test, save, assign, save again (a modified row only gets `UpdatedAtUtc`
  stamped): `TestDataFactory.CreatePersonAsync` and the E2E `PeopleTestHelpers.SeedPeopleAsync` do it.
  The list loads after the circuit connects, so an E2E test waits for `people-count` (or
  `people-all-archived`) **before** asserting that something is absent. Never assert relative-date
  wording ("12 days ago") against demo data - it moves every day; use bUnit with a fixed "today".

### Edit, contact methods and tags (issue #24)

- **Route and page.** `/people/{PersonId:guid}/edit` (`Components/Pages/EditPerson.razor`) loads through
  `IPeopleService.GetAsync` in `OnParametersSetAsync`, shows the shared `PersonNotFound` panel (also used
  by the profile) for a missing or foreign person, and renders `<PersonForm @key="_person.Id" Person=...>`
  so another person's edit page is a fresh form, never the first person's half-edited values. Archived
  people are editable. The title is the generic `Edit person - Relio` and the single `h1` is "Edit
  details" (no name). The `HeadOutlet` is static, so a `PageTitle` only changes on a full page load,
  not on a client-side navigation; E2E tests check titles after `GotoAsync`.
- **`ContactMethod`** (`Relio.Domain`, table `ContactMethods`, migration `AddContactMethods`): `Kind`
  (`Email|Phone|Address|Social|Other`) stored **as a string** with a check constraint
  (`CK_ContactMethods_Kind` - a new kind needs a migration that rewrites it), `Label` 50, `Value` **300**
  (the roadmap said 500: `(OwnerId, NormalizedValue)` is a 450 + 300 character `nvarchar` key, 1,500
  bytes, under SQL Server's 1,700-byte limit; a test inserts the widest possible row), `NormalizedValue`
  300, `SortOrder`. Cascade on `PersonId`; indexes `(OwnerId, PersonId)` and `(OwnerId, NormalizedValue)`
  (the lookup #27 and #28 match on).
- **`ContactMethodRules`** (Application, pure) owns normalization and the comparison key
  (`ToNormalizedValue`: email lower-cased; phone = leading `+` plus ASCII digits, no `00` rewriting; social
  without leading `@`, lower-cased; address/other whitespace-collapsed, lower-cased; idempotent and never
  longer than the trimmed value) and validation (`ValidateAll` reports `ContactMethodProblem(index, code)`).
  #27, #28 and #29 **reuse** it; do not write `NormalizedValue` any other way. Email/phone checks are
  plausibility checks, not delivery guarantees. `TagNameRules` does the same for tag names
  (`Normalize`, `Comparer` = invariant culture ignoring case, kana type and width - an approximation of the
  SQL Server `CI_AS` collation, accents stay distinct). Max 20 contact methods and 20 tags per person.
- **The request carries the whole list.** `UpdatePersonRequest.ContactMethods` is diffed by id: matched
  rows are edited, rows missing from the request are deleted, rows with no id are added, and `SortOrder`
  is the position in the request (no re-ordering UI; new rows go at the end). Every id must be one of
  *this person's* contact methods, else `ForeignEntityNotOwnedException("contact methods")`: that covers
  another user's, another person's, one that never existed, and one deleted in another tab, all with the
  same message. `CreatePersonRequest` accepts the same list and tags, but any id on create is foreign. All
  foreign ids (relationship type, tags, contact methods) are checked **before** anything is mutated or
  any tag created; a person that is not the user's returns `false` before any of that. Repeating an id
  in one request is a malformed request (`ArgumentException`), not a validation error.
- **Tags are created by typing** (`NewTagNames`): names are matched against the user's own tags in
  code with `TagNameRules.Comparer` (InMemory is case-sensitive), unmatched names become new `Tag` rows
  in the **same** `SaveChanges` as the person (an abandoned form leaves no stray tag), and the unique
  `(OwnerId, Name)` index stays the authority: a lost race (`SqlException` 2601/2627 naming
  `IX_Tags_OwnerId_Name`) becomes `PersonValidationError.TagNameConflict`, the form reloads its tags and
  asks the user to choose the tag from the list (proven with a `SaveChangesInterceptor` in
  `ContactMethodSqlServerTests`). `ITagService` also manages the list since #25 (see below). `ForeignEntityNotOwnedException`
  now has `EntityName` (constants in `ForeignEntityNames`) so the form tells a stale relationship type, tag
  and contact method apart; the message is unchanged and never contains an id or a value.
- **Last write wins, with stale-tab detection.** There is no concurrency token. A save from a stale tab
  replaces tags and fields, and drops contact methods that were added elsewhere in the meantime; the one
  thing it notices is a contact method it still shows that no longer exists, answered by a "This profile
  changed in another tab or window" alert with Reload. `Person.UpdatedAtUtc` is not bumped when only tags
  or contact methods change.
- **Contact data is never logged, never in a URL, and only links through `ContactLinks`.** Blazor does not
  sanitise attribute values, so `ContactLinks.HrefFor` is the only place an `href` is built from a contact
  method: `mailto:` for an email made of ordinary address characters, `tel:` from the normalized phone number
  with at least 3 digits, nothing for address, social or other. The value inputs carry `autocomplete="off"`.
  The profile lists tags as labels (not links); the people **list shows no tags** (minimisation; #53).
- **Form pieces.** `ContactMethodsEditor` (rows of kind, label autocomplete with per-kind suggestions in
  `ContactLabelSuggestions`, value, remove; add button disabled at 20; messages by row `Key` so a skipped
  blank row never shifts an error onto another row) and `TagPicker` (chips plus a `MudAutocomplete<TagOption>`
  from `TagSuggestions`; the remove buttons are our own with `aria-label="Remove tag {name}"` because
  `MudChip`'s close button is just "Close"). `PersonFormModel.FromPerson` starts the form from a person;
  a new row with no value and no label is not sent, but a saved row whose text was cleared **is** (so it
  asks for a value instead of being deleted silently). A kind change swaps the input type (`email`,
  `tel`, text; `MudTextField` with `Sizing.Auto` renders a `textarea`, so only the address row uses it).
- **EF.** `AddRelioData` states `UseQuerySplittingBehavior(SingleQuery)` for SQL Server (loading a person
  with tags and contact methods is two collection includes, which warns otherwise); queries never call
  `AsSingleQuery`/`AsSplitQuery` (relational-only, and E2E runs on InMemory).
- **Tests.** Race tests use a `SaveChangesInterceptor` that writes through a second context
  (`InsertTagOnFirstSaveInterceptor`, `SqlServerDatabaseFixture.CreateDbContext(params IInterceptor[])`);
  database cascades are proven with `ExecuteDeleteAsync` (it bypasses the tracker). bUnit tests that click
  an autocomplete option use `WaitForAssertion` (the choice is handled asynchronously). `PeopleTestHelpers.CreatePersonAsync`
  seeds a person with contact methods through the real service. Demo data now has contact methods (reserved
  example domains and fictional numbers only); a demo database seeded earlier is not backfilled.

### Archive, restore and delete (issue #26)

- **The profile's actions.** A labelled `MudMenu` "More" sits next to Edit (`person-actions`): **Archive**
  (or **Restore** for an archived person) and **Delete**. It uses MudMenu's own `Label`/`EndIcon`, never
  `ActivatorContent` (see the note in `ThemeModeMenu.razor`: the two-element activator does not open in a
  real browser). **No confirmation for archive or restore**: both are reversible, so they act at once, say
  "Person archived" / "Person restored" in a snackbar and stay on the profile, which reloads through
  `GetAsync` (never edits its local copy). An archived person shows a quiet **archived note** under the
  header (`Inventory2` icon, "Archived on 3 March", "Hidden from your lists and reminders. Everything you
  recorded is kept.", an outlined Restore button): no warning colour, no live region, no heading. The date
  is `ArchivedAtUtc` as a calendar day **in the user's time zone** (`PersonArchiveText.ArchivedOn`; SQL
  Server returns the instant with kind `Unspecified`, so it is treated as UTC); "Archived" alone when there
  is no time. Wording lives in `PersonArchiveText`. A person who disappears under an action (another tab)
  shows the usual not-found panel. `_busy` stops a second click starting a second action.
- **Delete is permanent and asks.** `IPeopleService.DeleteAsync(id)` hard-deletes: no soft delete, no undo
  (archive is the reversible one), and it works on active and archived people. The destructive
  `ConfirmDialog` is titled `Delete {name}?` (the one place a name appears outside the profile), says what
  goes and "It can't be undone.", and its red button is "Delete permanently"; the menu item itself is plain.
  The dialog does not focus the destructive button (MudBlazor 9's default focus is the dialog itself, so
  Enter does not delete): do not set `DefaultFocus`. After a delete the snackbar says "Person deleted" (no
  name) and the page goes to `/people` with `replace: true`, taking the profile out of the history so Back
  never lands on a "not in your list" panel - the one deliberate exception to the list's "navigation pushes"
  rule. Snackbars, the page title, logs and exception messages never carry a name.
- **The delete checklist.** `PeopleService.DeleteAsync` loads the person (owner in the query) with its tags
  tracked, calls the one private `RemoveDependentsAsync`, removes the person and saves **once** (a
  transaction on SQL Server; no `ExecuteDelete`, no `BeginTransaction`). The tags are *links*: only the
  `PersonTags` rows go (`person.Tags.Clear()`), never the `Tag` rows or other people's links to them, and the
  relationship type stays. Notes are removed with their person. An interaction is shared: remove only
  this person's participant row, and delete the interaction only when it has no remaining participants.
  **Every new entity with a `PersonId` must, in the same pull request: (1) have an
  `OnDelete(Cascade)` foreign key, (2) add one line to `RemoveDependentsAsync` (always filtered by `OwnerId`
  and `PersonId`), and (3) be added to `PersonDeleteChecklistTests`.** That test reads the
  model and fails when an entity references `Person` that the list does not name, so a forgotten child cannot
  pass quietly (the InMemory provider cascades only tracked dependents and enforces no foreign keys, so a
  missing line would otherwise leave orphans that look fine). `PersonDeleteSqlServerTests` proves every
  foreign key to `People` is `ON DELETE CASCADE` on a real database. A table with two foreign keys to `People`
  cannot cascade both (SQL Server's multiple cascade paths): make one `NoAction` and delete those rows in
  `RemoveDependentsAsync`. Account deletion (#59) deletes every owned table itself. Backups sit outside the
  app (#65). After a write changes or removes interactions, recalculate `Person.LastContactedOn` from the
  surviving interactions for every affected owner/person pair in the same tracked save; do not trust the
  denormalized value on a profile being merged or deleted. Two deletes racing from two tabs: the second
  normally reads `false`; in the tiny window between both loads and the first save it throws
  `DbUpdateConcurrencyException` (nothing half-deleted).
- **Archived people.** The rule is "exclude by default, include where the user is working with the person",
  enforced by explicit predicates (`!p.IsArchived`, or `!r.Person.IsArchived` on a child table) and a test per
  feature, not by a global filter or a shared `ActivePeople()` extension - the same reasoning as ownership
  (an extension is easy to forget and hides the filter at the call site). **Exclude**: the people list default
  (#23), the dashboard (#47), the creation of reminders (#37, #38, #41) and the scheduler that sends them at
  send time (#39), search by default with an opt-in (#52), filters (#53), person pickers (#35, #49).
  **Include**: the profile and its timeline (#33), edit (#24), delete (#26), duplicate detection (#27), merge
  (#28), import matching (#29), last-contacted maintenance (#34), export (#58), account deletion (#59) and the
  relationship type and tag counts (#25). Archiving never deletes or changes anything about the person's
  children, and restoring brings everything back. **Every feature that excludes archived people adds a
  `..._excludes_archived_people` test.**

### Relationship types and tags (issue #25)

- **Where.** Two interactive sub-pages, `/settings/relationship-types` and `/settings/tags`
  (`Components/Pages/SettingsRelationshipTypes.razor`, `SettingsTags.razor`: a back link, the page's one
  `h1`, and a component), reached from a section of the `/settings` hub. The components are
  `Components/Settings/RelationshipTypeSettings.razor` and `TagSettings.razor`, with `LabelNameDialog`
  (rename), `RemoveRelationshipTypeDialog` and `LabelSettingsText` (all the words, pure). Each sub-page is the
  **one data consumer** on its page and awaits its calls in turn (`_busy` disables the controls). Renaming is a
  dialog, not inline; the add form sits above the list and Enter adds. "Remove", not "Delete", in the UI.
- **Services.** `IRelationshipTypeService` gained `ListWithUsageAsync` (one query: the people count is a
  correlated subquery, served by `IX_People_RelationshipTypeId`), `CreateAsync(name)` (appended with
  `SortOrder = max + 1`), `RenameAsync(id, name)` and `DeleteAsync(id, Guid? reassignToId)`;
  `ITagService` gained `ListWithUsageAsync`, `CreateAsync`, `RenameAsync` and `DeleteAsync(id)`. Counts
  **include archived people** (the page says so). Not-found and not-owned are `false`, as for people.
- **Names.** `LabelNameRules` (pure; it reuses `TagNameRules.Normalize`/`Comparer`, so a type and a tag
  mean the same by "the same name"; limit 50 for both) gives `LabelValidationError` codes
  (`NameRequired`, `NameTooLong`, `NameTaken`) thrown as `LabelValidationException` before anything is loaded
  for a blank or long name. Uniqueness is checked in code with the comparer (InMemory is case-sensitive), a
  rename **excludes the row itself** (so `friend` -> `Friend` works), and the unique `(OwnerId, Name)` index is
  the backstop for a race: `SqlServerErrors.IsUniqueIndexViolation(exception, indexName)` (2601/2627 naming
  `TagConfiguration.NameIndexName` / `RelationshipTypeConfiguration.NameIndexName` - never add
  `HasDatabaseName`) turns it into `NameTaken`. That `SqlException` quotes the owner id and the name, so it is
  **never logged, attached as an inner exception or shown**. `LabelSettingsText.Message` words the codes.
- **Removing a type** (`DeleteAsync`): a `reassignToId` equal to the removed id is an `ArgumentException`
  (before loading); the primary type is loaded first (missing/foreign -> `false`), **then** the target is
  checked (`ForeignEntityNotOwnedException(RelationshipTypes)`, same message whether it does not exist or is
  someone else's); every affected person (archived too) is loaded **tracked**, given the new id (or `null`),
  and only then is the type removed, all in **one** `SaveChangesAsync`. No `ExecuteUpdate` (InMemory lacks
  it, and InMemory does not null untracked dependents), so reassign **before** `Remove`; the database's
  `ON DELETE SET NULL` is the backstop for a person given the type at the same moment. People's
  `UpdatedAtUtc` changes. The dialog (`RemoveRelationshipTypeDialog`) offers "Move them to another
  relationship type" (a select, no preselection, Remove disabled until chosen) or "Leave their relationship
  type empty"; with no other type only the second is shown; an unused type gets a plain `ShowConfirmAsync`.
  The count is refreshed right before the dialog opens.
- **Removing a tag**: `Include(t => t.People)`, `tag.People.Clear()`, `Remove(tag)`, one save, then the
  tracker is cleared; the people are not modified (the join table's cascading foreign keys are the SQL Server
  backstop, proven with `ExecuteDeleteAsync`). The confirmation says how many people have it.
- **Decisions.** The user may remove **every** relationship type (the person form then shows the select
  disabled with "You have no relationship types. Add them in Settings."); there is **no "restore the
  defaults"** and **no reordering** yet (follow-ups); defaults are seeded only at registration, in the
  `AddPersonProfile` migration and by the demo seeder - **no read ever creates a type**, and
  `DemoDataSeeder` does not recreate a removed type once the demo user has people. Renaming a tag or type
  changes it for everyone who has it (people refer to it by id). No caches: a rename shows on the next read.
- **Tests.** `Relio.Data.Tests`: `RelationshipTypeServiceManagementTests`/`TagServiceManagementTests` and the
  `...OwnershipTests` (cross-user), `Mutations_leave_nothing_tracked...`; `Relio.Data.IntegrationTests`:
  `RelationshipTypeServiceSqlServerTests`/`TagServiceSqlServerTests` (case-insensitive index, the races via
  `RunOnFirstSaveInterceptor`, one save via `CountSavesInterceptor`, a failing delete rolled back via
  `FailDeleteFromInterceptor`) and the label calls in `DatabaseLaneSqlServerTests`; `Relio.Web.Tests`: the
  `Fake...Service` doubles in `People/Fakes.cs` are real in-memory fakes (rules, records, `Add(name, people)`),
  and bUnit needs `MudPopoverProvider` + `MudDialogProvider` + `MudSnackbarProvider`, `MouseDown` on
  `.mud-input-control` to open a select and a click on the radio's `input` to choose it (bUnit's form
  `Submit()` stands in for Enter; Enter itself is in E2E). E2E (`RelationshipTypesAndTagsSettingsTests`)
  registers a **fresh user per test** - their six default types are theirs to change - and **never changes the
  demo user's relationship types or tags**; pick options in dialogs with `AriaRole.Option`/`AriaRole.Radio`.
  A page that composes label components belongs in `SqlServerPageLoadTests`.

### Possible duplicates (issue #27)

- **API.** `IPeopleService.FindPossibleDuplicatesAsync(PossibleDuplicateQuery, ct)` returns at most 5
  `PossibleDuplicate(Id, FirstName, LastName, IsArchived, Reasons)` (`PossibleDuplicateReason`: SameName,
  SimilarName, SameEmail, SamePhone; several can apply), strongest first. It is a read in the People
  aggregate, so the lane proxy already covers it (`DatabaseLaneSqlServerTests` includes it). The query
  carries first/last name, nickname, the contact methods (**only Email and Phone count**; an email without
  `@` or a phone with under 3 digits is ignored) and `ExcludePersonId` (the person being edited). The
  exclusion is not an assigned foreign id, so it is not ownership-checked: another user's id excludes
  nothing, since their people are never candidates. A blank first name means only the contact rules run;
  nothing to compare returns `[]`.
- **Candidates are every person the owner has, archived included** (an archived duplicate is still a
  duplicate; the warning labels them "Archived"). This is the one place an archived person is deliberately
  part of a default-looking view (see the #26 audit).
- **The matching is pure and lives in Application**, so merge (#28), import (#29) and search (#52) reuse
  it: `PersonNameNormalizer` (FormKD, drop non-spacing marks, then lower-case, hand-mapped ß æ œ ø ł đ ð þ
  dotless i, apostrophes removed - done first, because U+00B4 decomposes into a space - other punctuation to
  space, collapse, FormC; each step has a test), `NameSimilarity` (Damerau-Levenshtein with early exit;
  `FirstNamesClose`, `LastNamesClose`), `DuplicateProbe` (a query reduced to its keys) and
  `PossibleDuplicateMatcher` (`Prepare` normalizes candidates once, `Find` ranks). The rules are
  **stricter than a plain edit distance on purpose**: a first name under 3 letters matches only exactly; a
  prefix of 3+ letters matches (Alex/Alexander); otherwise one typo (two above 8 letters) matches, except a
  single-letter substitution in a name of 5 letters or fewer (Mark/Mary, Jon/Jan, Dan/Don stay apart;
  Jon/John, Jhon/John match; Eric/Erik is an accepted miss); last names need 5+ letters and one typo
  (Smith/Smyth yes, Hall/Hill no). Same email is an exact match on the lower-cased address; same phone is
  an exact match or the same last 8 digits when both have 8+ (so `+44 7700 900123` equals `07700 900123`).
  There is no nickname dictionary (Bob/Robert); only a stored nickname counts. Do not "simplify" the
  thresholds back to the roadmap sketch. #29 must call `Prepare` once and `Find` per row, never load
  the people again per row.
- **Nothing is persisted, cached or logged.** Normalized names and keys are computed in memory per call
  (and the form keeps one `DuplicateProbe.Key` string in component memory). No column, no cache, no
  migration. Never put a normalized name in an exception message, a URL or a title.
- **Data.** `Relio.Data.People.DuplicateCandidateLoader` (internal, reused by #28) loads, untracked and
  one query after another: every owner person as a projection; emails with an exact `IN` on
  `NormalizedValue` (a seek on `IX_ContactMethods_OwnerId_NormalizedValue`); phones with one `EndsWith`
  query per distinct suffix (the last 8 digits, or the whole short number), an owner-range scan of the
  same index since a suffix is not sargable - fine at personal scale. Always filter `Kind` (an address
  equal to an email must not match).
- **The form checks on a save attempt only**, never on blur or while typing and never from a lifecycle
  method: `PersonForm.SaveAsync` builds the request, asks `FindPossibleDuplicatesAsync`, and shows
  `PossibleDuplicateWarning` (a `Severity.Warning` `MudAlert`, links to `/people/{id}` with
  `target="_blank" rel="noopener"` and a visually hidden "(opens in a new tab)", reasons, **Save anyway**)
  directly above the form actions instead of saving. "Save anyway" acknowledges the probe key
  (`DuplicateProbe.Key`: names, nickname and the sorted email and phone keys) and saves; another Save with
  the same key does not warn again (also after a validation error), while any change to a name or an
  email/phone key checks again. A blank first name is not checked, so the service's validation message
  shows. In **edit** mode only a rename (a different `DuplicateProbe.NameKey` from the loaded person) is
  checked, and `ExcludePersonId` is always the person's id. `PossibleDuplicateWarning.ItemActions` is the
  hook #28 uses for "Merge instead" (edit mode only). Wording lives in `PossibleDuplicateText`.
- **Tests.** Every `IPeopleService` fake must implement the new method (`FakePeopleService` records
  `DuplicateQueries` and returns `Duplicates`). `PeopleTestHelpers.ListPeopleAsync` and
  `FindPossibleDuplicatesAsync` read through the real service in E2E tests. SQL Server translation of the
  three candidate queries is proven in `PossibleDuplicateSqlServerTests`.

### Merging profiles (issue #28)

- **Routes and entry points.** `/people/{PersonId:guid}/merge` (`Components/Pages/MergePeople.razor`) and
  `?with={otherId}`. **The route person is always the primary**: the profile that is kept, whose id, URL and
  `CreatedAtUtc` survive; `with` is the duplicate, merged in and removed. "Keep the other profile instead"
  just links to the other profile's merge page. Two entry points: **Merge with…** in the profile's More menu
  (`person-merge`, a plain link) and **Merge instead** on the #27 warning, **in edit mode only**
  (`PersonForm` passes `ItemActions`; create mode has no saved profile to merge into). The address carries
  opaque ids only, never a name; `with` is a `string?` parameter parsed with `Guid.TryParse` (anything else,
  or the person's own id, is ignored). The page title is the generic `Merge profiles - Relio`.
- **One page, two steps** (so reload and Back work): (1) choose the other profile - the #27 suggestions as
  quick picks plus a `MudAutocomplete` over every other person, archived included, filtered in memory with
  `PersonNameNormalizer` (`MergeCandidateSearch`), loaded once by `IPersonMergeService.ListCandidatesAsync`;
  choosing pushes `?with=`; (2) compare, choose and confirm. It loads in `OnParametersSetAsync` behind a
  `SemaphoreSlim` and a version counter, one call after another, like the list.
- **`IPersonMergeService`** (`Relio.Application.People`; `PersonMergeService` in `Relio.Data`, registered
  with `AddDataService`). `MergeAsync(MergePeopleRequest { PrimaryId, DuplicateId, FieldChoices })` returns
  `MergeOutcome.Merged` or `NotFound` (either id missing **or** not the user's, indistinguishable, nothing
  changes); the same ids or a malformed choice are `ArgumentException`s thrown before any database work; a
  merged profile that breaks a limit throws `PersonValidationException` and saves nothing.
- **`PersonMergeRules` is shared by the page and the service** (pure, in Application): `ConflictingFields`,
  `DefaultChoice`, `CanKeepBoth`, `ValidateChoices` and `Combine`, which returns a `MergedProfile` (also an
  `IPersonProfileInput`, so `PersonProfileRules.Validate` checks the limits like any save). The page previews
  `Combine` and the service applies it, so a default, a dedupe or a limit is never re-implemented in Razor.
  **Only the fields the two profiles disagree on are asked** (a `MudRadioGroup` each: name, nickname,
  relationship, birthday, how you met, details, status); a field only one side has is kept without asking. The
  name is chosen as a whole (first and last together), the birthday as a whole (day, month, year), except that
  the same day and month with a year on one side only keeps the year. How you met and Details also offer
  **Keep both** (primary first, a blank line, the duplicate), disabled when the joined text would be too long.
  Defaults: the primary's value, except the **archived state, which defaults to active**; if both are archived the
  primary's `ArchivedAtUtc` is kept. `LastContactedOn` is the later date.
- **Contact methods are united and deduplicated by `(Kind, NormalizedValue)`**, not by the normalized value
  alone (normalization is per kind): the primary's rows stay in order (and take a label they lacked from the
  repeated row), the duplicate's new rows are appended after them in their own order, and repeats (also within
  the duplicate) are deleted. **Tags are a union by id**; only join rows change, the `Tag` rows are never removed.
  The union can break the 20 contact method and 20 tag limits, and **an over-limit merge is rejected**, never
  truncated: the page words it (`PersonMergeText.Problem`) and nothing changes.
- **One tracked save, one transaction.** `MergeAsync` loads both people in **one** tracked query (so a shared
  tag is one instance), combines and validates **before** any mutation, moves the dependents
  (`MoveDependentsAsync`, see the checklist above), calls `ChangeTracker.DetectChanges()` and then
  `Remove(duplicate)`, and saves once; `ChangeTracker.Clear()` runs in `finally` on every path. EF Core cascades a
  removed principal's delete to the dependents it tracks *immediately*, so a row has to have left the duplicate
  first; current EF Core runs change detection inside `Remove` too, so the explicit call is a pinned safety net
  (the moved-row tests prove the rows survive on InMemory and SQL Server). A `DbUpdateConcurrencyException` (a side
  was deleted between the load and the save) is `NotFound`; the transaction rolled back, and a child added to the
  duplicate in that tiny window goes with it by the database cascade, the same as when a person is deleted.
  `PersonMergeSqlServerTests` proves the transaction (every write shares one, a failure in the middle rolls
  everything back, and a second save would fail those tests).
- **The confirmation names both people** (`Merge {duplicate} into {primary}?`, a destructive `ConfirmDialog`,
  button "Merge profiles"); nothing is merged before it. Afterwards the snackbar says "Profiles merged" (no
  names) and the page goes to the primary with `replace: true`, so the address that carries the removed
  profile's id is no longer in the history (Back reaches at most the first step, `/merge` without `with`).
  A profile gone in the meantime shows "One of these profiles is no longer in your list." and the page reloads.
  Nothing here logs.
- **Tests.** `PersonMergeRulesTests` (pure), `PersonMergeServiceTests` and `PersonMergeServiceOwnershipTests`
  (InMemory), `PersonMergeChecklistTests` (the model-based guards), `PersonMergeSqlServerTests`, the
  `MergePeoplePageTests`/`MergeCandidateSearchTests`/`PersonMergeTextTests` bUnit tests (a
  `FakePersonMergeService` must implement every member) and the E2E `PersonMergeTests`. bUnit puts a MudRadio's
  `data-testid` on its `<input>` and a `MudRadioGroup`'s class on an outer wrapper (the radios sit in
  `.mud-radio-group`, whose own rule needs a three-class selector to override). The full timeline of both
  Notes move to the primary with their text, pin state and audit timestamps unchanged; interaction
  participant rows move too, except the duplicate row is removed when the primary already participates
  in that shared interaction. A shared interaction stays one entry, never a copy. E2E and SQL Server
  tests prove both cases. Future person children (for example reminders and difficult moments) extend
  `MoveDependentsAsync`, both checklists, both SQL Server foreign-key tests and the E2E merge test.

### Importing people (issue #29)

`/people/import` (`Components/Pages/ImportPeople.razor`, interactive like every people page) imports people from
a vCard or CSV file. Entry points: an outlined **Import** button next to **Add a person** in the list header
(`people-import`) and a link under the empty state (`people-empty-import`); there is no drawer entry
(`NavMenuTests` keeps five routes). It completes epic #21. No migration, no new package, no new table.

- **The file is never stored or logged.** Not on disk, not in a cache or session, not in a log: neither the
  file, its name, its headers or cells, nor the text of any exception that could carry them (the page logs the
  exception *type* only; `ImportFileException` and `PeopleImportValidationException` carry codes and indexes).
  `ImportFileReader` reads the browser file into memory (`OpenReadStream(maxAllowedSize, ct)` with the limit
  always passed - the default is 512,000 bytes - and a token cancelled in `Dispose`; `IBrowserFile.Size` is
  client-reported, so it only gives the friendly message), the pure parsers read the bytes, and the bytes and the
  decoded text are dropped: the page keeps only the drafts and the preview, which die with the circuit. The
  file reaches no data service. Never hold the `IBrowserFile` past the handler, and never put a name from the
  file in a title, a URL or a log.
- **Limits** (`ImportLimits`): 1 MB, 2,000 people (a longer file keeps its first 2,000 and the preview says so),
  200 CSV columns (more means "not a contacts file"), 20,000 characters per CSV cell (cut), 200,000 per vCard
  line (skipped: an embedded photo). A NUL byte in a `.csv` means it is not a text file.
- **The parsers are hand-rolled and pure** (`Relio.Application/People/Import/`, no I/O, no clock, invariant
  culture only, no regular expression over untrusted text). FolkerKinzel.VCards was evaluated and not used: we
  need a small read-only subset, the Apple quirks (`X-APPLE-OMIT-YEAR`, `itemN.X-ABLabel`, `X-SOCIALPROFILE`)
  need custom code with it anyway, it would add three packages (one maintainer, eight major versions of breaking
  API) to an AGPL app, and one parser style for CSV and vCard keeps the limits and the never-log rule in one
  place. Revisit if people report vCards `VCardReader` cannot read.
  - `TextDecoder`: byte order mark, else UTF-16 by zero bytes, else strict UTF-8, else Windows-1252 (Outlook,
    older Windows exports) through `CodePagesEncodingProvider.Instance` directly - **never**
    `Encoding.RegisterProvider` in library code.
  - `VCardReader` (2.1, 3.0, 4.0): **unfold on the bytes before decoding**, so a multi-byte UTF-8 character that
    an exporter split across a fold survives (UTF-16 is decoded first). Quoted-printable (Android 2.1) is decoded
    to bytes and then to text in the property's `CHARSET`, and a trailing `=` joins the next line; structured
    values are split on unescaped separators *before* decoding. 2.1 bare parameters (`TEL;CELL;VOICE`), quoted
    and comma-listed `TYPE`s, groups (`item1.TEL`) with Apple's `X-ABLabel` (`_$!<Mobile>!$_` is "Mobile", the
    "Other" label is no label), `tel:` URIs (the extension is dropped), faxes skipped, nested `AGENT` cards
    skipped, a final card without `END` kept. Names: `N`, else `FN` split at the last space; an organisation alone
    gives no name. Ignored: `CATEGORIES`, `ORG`, `TITLE`, `URL`, `IMPP`, `X-` extensions, photos.
  - `CsvReader`: RFC 4180 state machine, delimiter from the first record (`.tsv` is tab), blank records skipped,
    ragged records padded or cut. `CsvMappingPresets` recognises Google's two exports and Outlook's from the
    headers (otherwise common column names); `CsvDraftMapper` applies the mapping the user confirmed. Several
    values in one cell ("a ::: b", Google) become several contact methods; Google's "* " label prefix is removed.
  - **Birthdays are parsed strictly** (`BirthdayParser`): `yyyy-MM-dd`, `yyyyMMdd`, `--MM-dd`, `--MMdd`,
    `yyyy/MM/dd` and `a/b/yyyy`, `a.b.yyyy`, `a-b-yyyy` read in the file's `DateOrder`; the year 1604 (Apple) or
    `X-APPLE-OMIT-YEAR` means no year; Outlook's `0/0/00` means none. Two-digit years, month names, times and
    anything impossible are "unreadable": the birthday is left out with a warning, never guessed. When the dates
    do not prove the order (03/04/1990) the mapping step asks, and the preview says to check a birthday.
- **Blocking problems and warnings** (`ImportCandidateBuilder`, `ImportProblem`, words in `ImportText`):
  no usable first name, or a first or last name that is too long, blocks the row (shown, disabled, with the
  reason). Everything else is a warning and the offending field is dropped or kept in another form (an email or
  phone number that fails `ContactMethodRules` becomes an "other" detail labelled "Email"/"Phone"; more than 20
  contact details keep the first 20). The builder runs the *real* `PersonProfileRules` and `ContactMethodRules`
  until they report nothing, so every importable `ImportPersonRequest` is valid; `ImportAsync` validates it again.
- **Duplicates through #27's matcher.** `PreviewAsync` loads the user's people once
  (`DuplicateCandidateLoader.LoadAllAsync`, archived included), `Prepare`s them once and `Find`s per row; each
  row is also compared with the rows *above* it in the file (`SameAsRowNumber`). A row with any duplicate starts
  **unselected** but stays selectable. The matcher is O(rows x people): `PreparedCandidate` carries its keys as
  arrays and `Find` uses indexed loops, because an enumerator allocated per pair made 2,000 x 2,000 take seconds
  (`Two_thousand_rows_build_in_reasonable_time`).
- **`IPeopleImportService`** (registered with `AddDataService`; Task-only): `PreviewAsync` is read-only (untracked,
  today in the user's zone via `UserToday`) and `ImportAsync` is **one `AddRange` and one `SaveChangesAsync`**,
  all or nothing (one transaction on SQL Server, proven in `PeopleImportSqlServerTests` with a failing command),
  `ChangeTracker.Clear()` in `finally`. There are no foreign ids: an import sets **no relationship type and no
  tags** (follow-up: import groups as tags), so there is no ownership check beyond `OwnerId`.
  `PersonEntityBuilder` (`Relio.Data/People`) is the one place a `Person` and its contact methods are built from
  a profile input: `CreateAsync`, `UpdateAsync` and the import share it.
- **The page** keeps three steps in one component: choose a file, (CSV) `CsvMappingStep` (headers and cells are
  text, never markup; the column list hides label columns and groups Outlook's address columns into one switch),
  then `ImportPreviewList` (50 rows a page, `ImportSelection` survives paging and the filter, checkboxes are
  MudCheckBox, duplicates link to the existing profile with `target=_blank`). A validation failure at import time
  (today moved on, say) reloads the preview; any other failure says nothing was saved. Afterwards the snackbar says
  "Imported N people" and the page opens `/people?sort=added`. After a file has been chosen MudBlazor leaves a
  second `<input type=file>` beside the first: E2E tests use `.Last`.
- **Fixtures are byte-exact.** `Relio.Application.Tests/People/Import/Fixtures/` holds synthetic files (fictional
  people, `example.com`, numbers in the ranges reserved for fiction) and `.gitattributes` marks them
  `-text -whitespace`, with an `.editorconfig` override, so CRLF, trailing spaces (folds) and the UTF-16 and
  Windows-1252 bytes survive git and `git diff --check`. Write non-UTF-8 fixtures with a tool that writes raw
  bytes and check them with a hex dump.

### Interactions, notes and timeline (epic #30; issues #31–#35)

- **Interactions are shared records.** `Relio.Domain.Interaction` is an `OwnedEntity` with a
  user-calendar `DateOnly OccurredOn`, `InteractionKind`, and a required description (maximum 10,000
  characters). People are related through `InteractionParticipant`, not a `PersonId` on the interaction:
  each participant has its own `OwnerId`, `InteractionId` and `PersonId`; the unique pair prevents a
  person being listed twice. Interaction deletion cascades to participants, and person deletion cascades
  to their participant links. `InteractionService` validates that there are 1–20 distinct, non-empty,
  owner-scoped participant ids and that the profile being viewed is included; it rejects every bad
  foreign id before mutating anything. Dates after `IUserTimeZoneService.GetTodayAsync()` are rejected
  in the user's calendar; never compare an interaction date to UTC today. New archived people cannot be
  added, but existing archived participants remain available when editing. The profile can create or
  edit an interaction, and the same record appears on every participant's timeline. Explain in the UI
  that an edit or deletion affects everyone listed.
- **Last contact follows surviving interactions.** `Person.LastContactedOn` is the latest `OccurredOn`
  among that owner's surviving interactions for the person, or `null`. Create, update and delete
  recalculate every old and new participant in the same tracked save; moving or deleting a shared
  interaction must not leave stale dates on any profile. `PersonLastContactUpdater` is also used when
  merging profiles, so a seeded or otherwise stale `LastContactedOn` value never outranks the actual
  interaction history. Include archived people in maintenance. Do not set `LastContactedOn` in ordinary
  person create/update requests.
- **Notes belong to one person.** `Relio.Domain.Note` has `PersonId`, up to 10,000 characters of plain
  text and an optional `IsPinned` flag. Pinned notes appear above the profile facts; all notes also
  appear in the timeline. A pin changes presentation only, never retention. Note reads and mutations
  are explicitly filtered by `OwnerId`; a missing or foreign note is indistinguishable. User-written
  text is rendered as text, never markup, and is never logged. Notes and interactions remain until the
  user deletes them or deletes their person; there is no automatic content-retention timer. Archiving
  hides a person from default list/picker flows, but keeps all their notes and interactions intact.
- **Timeline is bounded and chronological.** `IPersonTimelineService.GetPageAsync` returns at most 50
  entries by default (caller size is clamped to 1–100) from separate, bounded SQL keyset streams for
  interactions and notes; never load a person's complete history into memory. Merge those bounded
  streams by their user-calendar date, timestamp and a stable type/id tie-break. An interaction's date
  is its stored calendar date; a note's display date is `CreatedAtUtc` converted to the owner's zone.
  Neither is converted into a UTC calendar date. Filters are All, Interactions, Notes and Difficult
  moments. There is not yet a DifficultMoment model: its filter is intentionally empty as an extension
  seam, not placeholder data or a reason to add a model before that feature.
- **UI and checklists.** `PersonProfile` owns orchestration only: it renders `PinnedNotes`, the mixed
  timeline and inline `NoteEditor`/`InteractionEditor`; components reach the database only through
  `INoteService`, `IInteractionService` and `IPersonTimelineService`. Keep one generic page title and one
  `h1`; never put names, notes, descriptions, ids or search text into URLs or logs. Add every new person
  child to `PeopleService.RemoveDependentsAsync`, `PersonMergeService.MoveDependentsAsync`,
  `PersonDeleteChecklistTests`, `PersonMergeChecklistTests`, `PersonDeleteSqlServerTests` and
  `PersonMergeSqlServerTests`, and extend merge E2E coverage. Delete a person's note; remove their
  interaction participant and only delete the interaction when no participants remain. During merge,
  move notes preserving pin/text and participant links preserving one shared interaction when both
  profiles were listed. Add each service's read to `DatabaseLaneSqlServerTests` and profile composition
  to `SqlServerPageLoadTests`; unit InMemory tests do not prove SQL query translation or bounded paging.
- **Migration and tests.** The model changes land together in `AddInteractionsNotesAndTimeline`.
  `PersonTimelineSqlServerTests` must prove both note/interaction queries take only a bounded page from
  a long history and that a note crossing UTC midnight gets the right user-calendar date. Exercise
  last-contact transitions for create, date/participant edits and deletion, plus shared-interaction
  merge and person deletion (including when its last participant is removed).

## Reminders and follow-ups

Established by epic #36 (issues #37, #38, #39, #40, #41). Follows the user-scoped data pattern and calendar-date rules.

- **Entity & Storage**: `Reminder` (`Relio.Domain.Reminder`, inherits `OwnedEntity`, foreign key `PersonId` to `People` with `OnDelete(Cascade)`). Tracks `Title`, `DueDate` (`date`), `Frequency` (string), `CustomIntervalMonths`, `SnoozedUntilDate` (`date`), `IsCompleted`, `CompletedAtUtc` (audit timestamp), and `LastDeliveredDate` (`date`, ensures idempotent exact-once delivery). Indexes on `(OwnerId, PersonId)` and `(OwnerId, IsCompleted, DueDate)`.
- **Delete and Merge checklists**: `Reminder` is integrated into `PeopleService.RemoveDependentsAsync` and `PersonMergeService.MoveDependentsAsync` (reassigns `PersonId`), tested by `PersonDeleteChecklistTests`, `PersonMergeChecklistTests`, `PersonDeleteSqlServerTests`, and `PersonMergeSqlServerTests`. `Person` columns `StayInTouchCadenceDays`, `BirthdayReminderDisabled`, and `BirthdayReminderLeadDays` are integrated into `PersonMergeRules.Combine`.
- **Recurrence & Snooze**: `ReminderRecurrence.CalculateNextDueDate` advances completed recurring reminders based on `Frequency` and `CustomIntervalMonths`. One-off reminders simply mark completed (`IsCompleted = true`). Snoozing sets `SnoozedUntilDate`.
- **Birthday Reminders**: Derived dynamically from `Person.Birthday` via `BirthdayReminderCalculator.Calculate` using `UserCalendar.NextOccurrence` (observing Feb 28 in non-leap years for Feb 29 birthdays). Supports global enable/disable (`UserProfile.BirthdayRemindersEnabled`), default lead days (`UserProfile.DefaultBirthdayLeadDays`), and per-person overrides (`Person.BirthdayReminderDisabled`, `Person.BirthdayReminderLeadDays`).
- **Stay-in-Touch Cadence**: Optional per-person cadence in days (`Person.StayInTouchCadenceDays`). When elapsed days since `LastContactedOn ?? ToUserDate(CreatedAtUtc)` exceeds the cadence, the person is surfaced in "Reach out" on the Dashboard and Reminders page. "Mark contacted" updates `Person.LastContactedOn` to reset the cadence.
- **Email Delivery & Privacy Invariant**: `IReminderEmailSender` abstraction with `SmtpReminderEmailSender` (when `Email:Provider == Smtp`) and `NullReminderEmailSender` (no-op logger when unconfigured). User notification preferences at `/settings/reminders` (`ReminderEmailDelivery`: `None`, `Immediate`, `DailyDigest`). Unsubscribe links use cryptographically secure URL-safe tokens via `/unsubscribe?token=...`. **Email bodies strictly contain only person name, reminder title, and due date — NEVER notes, difficult moments, or sensitive details** (GDPR compliance).
- **Background Scheduler**: `ReminderSchedulerBackgroundService` periodically runs `IReminderSchedulerRunner.RunDueRemindersJobAsync`. Evaluates user time zones to determine local `today`. Queries due reminders and birthdays via indexed queries, dispatches emails according to preferences, and stamps `LastDeliveredDate = userToday` atomically, guaranteeing exact-once delivery across app restarts and multi-instance deployments. All time-sensitive logic uses injected `TimeProvider`.

## End-to-end tests

`Relio.Web.E2ETests` drives the real Relio.Web app (the `Program` entry point, via its trailing
`public partial class Program;` marker) with Playwright, in a real headless Chromium browser - not
bUnit's rendered-component model (`Relio.Web.Tests`), which never exercises JS interop, real HTTP,
or an actual SignalR circuit.

- **Database**: tests run the app with `Database:Provider=InMemory` (see "Current product
  decisions" below and `Relio.Data.DependencyInjection.ServiceCollectionExtensions`) - no SQL
  Server, no connection string, no migrations (`Database.EnsureCreatedAsync()` instead). Never the
  default; it is test/dev only and logs a startup warning when active.
  `SqlServerPageLoadTests` is the one exception. It starts a `VariantApp` with
  `Database__Provider=SqlServer` on a throwaway database, skipped unless `ConnectionStrings__Relio`
  is set (CI always sets it), because InMemory hides `DbContext` concurrency. `RelioWebAppFactory`
  only defaults `Database__Provider` to InMemory when it is unset, and `VariantApp` restores the
  variables it changes.
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
- **Asserting a page heading**: use `page.GetByRole(AriaRole.Heading, new() { Name = "...", Exact = true })`,
  not `GetByText`: the drawer link and the page title carry the same words (`NavigationTests`).
  Tests that add people register a fresh `NewEmail(...)` user and use unique names
  (`PeopleTests`); the shared demo user's people are never edited.
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
