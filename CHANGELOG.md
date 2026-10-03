# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

- User time zones for dates and reminders (issue #12): `Relio.Domain.UserProfile` (one per user,
  `TimeZoneId` defaulting to UTC), `Relio.Application.Time.IUserTimeZoneService` (current user's
  time zone/"today", set time zone, due-today/overdue checks) implemented by
  `Relio.Data.Time.UserTimeZoneService`, pure `UserCalendar` helpers (UTC-instant ↔ user-local
  conversion, next-birthday-occurrence with Feb 29 handled as Feb 28 in non-leap years), IANA time
  zone id validation (`TimeZoneIds`), a self-hosted browser-time-zone JS reader
  (`Relio.Web.Time.IBrowserTimeZoneReader`, for sign-up #15 to default to), and the `AddUserProfile`
  migration. Documented in the new "Dates and time zones" section of AGENTS.md.
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
