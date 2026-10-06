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
- A `Person`'s `Birthday` (and any future reminder/interaction date) is a `DateOnly` - never
  convert it to/from UTC. Feb 29 birthdays observe **Feb 28** in non-leap years (not Mar 1) - see
  `UserCalendar.NextOccurrence`.
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
  archived, one with a Feb 29 birthday) and a non-UTC time zone, writing directly through
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
