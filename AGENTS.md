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
- The real `ICurrentUser` is `Relio.Web.Security.HttpContextCurrentUser`, reading the
  `ClaimTypes.NameIdentifier` claim off `HttpContext` via `IHttpContextAccessor`. Registered in
  `Program.cs`. Until epic #14 lands, there is no Identity middleware populating that claim, so it
  resolves as unauthenticated - that's expected for now.

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
  no third-party script), for sign-up (#15) to default a new user's time zone instead of leaving it
  at UTC. Web-only; Application/Data services never depend on it.
- Tests must cover users far from UTC (e.g. `Pacific/Kiritimati`, UTC+14; `Pacific/Pago_Pago`,
  UTC-11) around the UTC midnight boundary, and a DST transition (e.g. `America/New_York`,
  `Europe/Rome`), using `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` for
  deterministic "now". See `Relio.Application.Tests/Time/UserCalendarTests.cs`.

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
