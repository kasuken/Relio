# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

- Hosted billing with Stripe, following the LearnStack implementation. Off by default
  (`Billing:Provider=None`): a self-hosted instance has no plans and no limits. With
  `Billing:Provider=Stripe`, accounts are on a Free plan for up to 25 active people (archived people
  don't count) and can subscribe to Relio Pro, with unlimited people, for $2 a month or $12 a year.
  - `/Account/Manage/Plan` shows the plan and usage, starts Stripe Checkout (monthly or yearly),
    confirms a purchase as soon as the user returns, and opens the Stripe customer portal to change
    payment details, see invoices or cancel. Settings links to it when billing is on.
  - The limit is enforced in the services that add or restore people (create, restore, import, merge
    and JSON restore); nothing is deleted when an account returns to Free.
  - Signature-verified webhooks at `/api/webhooks/billing`, applied once per event id in the same save
    as their ledger row, ignoring out-of-order and other products' events on a shared Stripe account.
    A failed payment keeps Relio Pro through Stripe's retries.
  - Account deletion cancels the subscription at Stripe before deleting anything; account email
    changes are passed on to the Stripe customer.
  - `/pricing`, the landing page and the features page show the plans and prices when billing is on.
  - Migration `AddHostedBilling` adds `UserSubscriptions` and `ProcessedBillingEvents`. Setup and the
    privacy boundaries are in `docs/security/billing.md`; the Bicep templates take `billingProvider`
    and the two price ids, with the secrets in Key Vault.

- SaaS and self-hosted deployment (epic #63; issues #64–#67). Multi-stage Dockerfile
  running as a non-root user with an in-container `/dev/tcp` health check against `/health/live`.
  `docker-compose.yml` stack with SQL Server 2022, persistent data and key volumes, and automated
  startup migrations via `Database:ApplyMigrationsOnStartup`. Self-hosted Data Protection
  auto-provisions wrapping certificates on first boot via `DataProtection:AutoGenerateIfMissing`.
  Comprehensive self-hosting guide (`docs/self-hosting.md`) detailing setup, configuration reference,
  reverse proxy TLS termination, and ADR-0002 backup, retention, and post-erasure guidelines.
  Automated Azure App Service release pipeline (`release.yml`) using OIDC authentication and
  publishing container images to GitHub Container Registry. Provider-agnostic billing subsystem
  (`IBillingProvider`, `BillingOptions`) defaulting to `Billing:Provider=None`, ensuring all
  relationship features run unconstrained with no billing UI displayed while allowing hosted
  Stripe billing via configuration without code changes.

- Search and filtering (epic #51; issues #52–#53). Instant name and nickname search
  across active people, combined with multi-select filtering by tags and relationship types.
  Two-way URL query synchronization (`q`, `tag`, `type`) for bookmarkable and shareable views.
  Global search shortcut in the application bar with responsive mobile adaptation. Compound
  index on `(OwnerId, IsArchived, Nickname)` for SQL Server query efficiency.

- Public marketing pages (epic #85): a static anonymous landing page, verified feature overview,
  hosting information without unimplemented paid offers, and locally embedded release notes.
  The authenticated workspace now starts at `/dashboard`; default sign-in and account
  continuations use that route while explicit safe local return URLs remain unchanged.
  Public metadata uses a validated configured origin, never a request Host header. A local social
  preview and a fictional relationship illustration use Relio's tokens and self-hosted fonts.
  Indexing remains off by default; production opt-in lists only fixed public and enabled reviewed
  policy routes. Changelog Markdown disables raw HTML, validates links and permits local raster
  images only. Difficult moments, search/filtering and hosted billing are explicitly not
  advertised as delivered.

- Privacy, trust and data ownership (epic #54; issues #55-#62). Purpose-separated authenticated
  encryption protects private narratives, reminder titles and Identity/unsubscribe credentials,
  backed by mandatory durable external protected keys and restartable legacy-data backfill.
  Settings offers full-fidelity JSON export and atomic fresh-account restore, contact-only vCard
  export, and password-confirmed permanent account erasure with an export-first prompt. Required
  owner-to-Identity foreign keys reject stale writes; serialized lifecycle changes preserve an
  active administrator, and erased sessions are rejected on their next request or circuit activity.
  Hardened account endpoints add configurable rate limits, nonce-based script CSP, security headers
  and framework-log privacy filters. An always-available source link and optional reviewed policy
  hosting ship without generated legal text; public indexing and content-free aggregate metrics
  remain off by default. Metrics retain only a bounded per-owner contribution for 90 days.
  Real SQL Server ownership, ciphertext, transaction, lifecycle and retention proofs extend the
  fast unit and browser coverage. Migrations `ProtectSensitiveFieldsAndAddProductMetrics` and
  `EnforceOwnedAccountLifetimes`; protected-storage downgrade is refused in favor of restoring a
  pre-upgrade backup with matching keys. Difficult moments are not yet modeled.

- Dashboard, first-run onboarding and global quick log (epic #46; issues #47–#50).
  The dashboard shows upcoming reminders and birthdays, people to reach out to, recent interactions
  and recently added people, with helpful empty states and up to five entries per section. Upcoming
  reminders cover the next 30 days, including overdue reminders; archived people and completed
  reminders are excluded. Queries use owner-scoped projections and database limits rather than
  loading full profiles. New accounts get an optional guide to set their time zone, add a person
  and record an interaction; completing or skipping it is remembered across devices. Existing
  accounts are not enrolled on upgrade. **Log an interaction** in the dashboard, people list and
  app bar opens an active-person picker and the existing interaction editor, keeping the person's
  timeline and last-contact date in sync. Responsive layouts support 360px phones, tablet and
  desktop widths, with keyboard flows, a skip-to-content link, clearer action labels, wrapping
  for long user text and visible focus. Migration `AddOnboardingState` adds only the per-account
  dismissal flag; no analytics, external requests or new packages.

- Record interactions and notes in each person's timeline (epic #30; issues #31, #32, #33, #34 and #35).
  **Log an interaction** records a calendar date, type, description and up to 20 people; the same
  interaction appears in every participant's timeline, and edits or deletion affect all of them. New
  participants are active people by default, while an archived participant already on an interaction
  stays attached when it is edited. **Add a note** stores up to 10,000 characters for one person, with
  an optional pin near the top of that profile. Notes can be edited, unpinned or permanently deleted.
  The mixed timeline is filterable and loads 50 entries at a time from bounded database queries; note
  dates are shown in the owner's time zone, while interaction dates remain the calendar dates entered.
  `LastContactedOn` is now derived from the latest surviving interaction for each participant, including
  when a shared interaction is added, edited, removed or moved in a profile merge. Deleting a person
  removes their notes and participant links, but preserves a shared interaction while someone else is
  still listed; merging moves notes and deduplicates shared participation. Timeline text is private,
  never logged, and remains until the user deletes it or its person. Difficult moments are not yet
  modeled; the timeline has an empty filter seam for that future feature. Migration
  `AddInteractionsNotesAndTimeline`; no external requests or new packages.

- Reminders and follow-ups (epic #36, issues #37, #38, #39, #40, #41):
  - Reconnect reminders (issue #37): schedule one-off or recurring reminders (`Weekly`, `Monthly`, `EveryThreeMonths`, `EverySixMonths`, `Yearly`, or `CustomMonths`) for any person. View, quick-complete, snooze (tomorrow, 3 days, 1 week, 1 month, or custom date), edit and delete on `/reminders`, on the Dashboard (`/`), and on the person profile. Completed reminders move to the Completed tab with completion timestamp.
  - Birthday reminders (issue #38): automatic birthday reminders derived from each person's birthday (year optional, with Feb 29 observed on Feb 28 in non-leap years). Configurable global and per-person lead time (on the day, 1 day, 3 days, 1 week, 2 weeks before) and per-person disable toggle. Displayed on the Dashboard, Reminders page, and Person profile.
  - Stay-in-touch cadence per person (issue #41): configurable cadence setting per person (`StayInTouchCadenceDays`: e.g. weekly, every 2 weeks, monthly, quarterly, or custom days). Automatically surfaces overdue people in "Reach out" on the Dashboard and Reminders page when elapsed days exceed the cadence. Quick "Mark contacted" action resets the cadence by updating `LastContactedOn`.
  - Email delivery and notification preferences (issue #40): `IReminderEmailSender` abstraction supporting SMTP and Null providers. User notification preferences at `/settings/reminders` allowing selection between `None` (dashboard only), `Immediate` (email per reminder), or `DailyDigest` (consolidated daily email). Cryptographically secure one-click unsubscribe links and `/unsubscribe?token=...` endpoint. Strict privacy invariant: reminder emails contain only person name and reminder title; no notes or sensitive content.
  - Background reminder scheduler (issue #39): `ReminderSchedulerBackgroundService` runs periodically, evaluates each user's local day in their configured time zone, queries due reminders and birthdays via indexed queries, delivers emails according to user preferences, and atomically stamps `LastDeliveredDate` to guarantee idempotent exact-once delivery across restarts and multiple instances. Fully tested with `TimeProvider`.
  - Migration `AddRemindersAndPreferences`: adds `Reminders` table (`(OwnerId, PersonId)`, `(OwnerId, IsCompleted, DueDate)` indexes), `Person` columns (`StayInTouchCadenceDays`, `BirthdayReminderDisabled`, `BirthdayReminderLeadDays`), and `UserProfile` columns (`BirthdayRemindersEnabled`, `DefaultBirthdayLeadDays`, `ReminderEmailDelivery`, `UnsubscribeToken`).

- Import people from a vCard or CSV file (issue #29), completing epic #21. **Import** in the people list's
  header (and "Or import people from a file" under the empty state) opens `/people/import`. Choose a vCard
  (`.vcf`, versions 2.1, 3.0 and 4.0, as exported by iPhone, Android and Google Contacts) or a CSV file (Google
  Contacts and Outlook exports are recognised, and any other file with a header row can be matched column by
  column, with the field names, an example value and the date order you can change). Names, nickname,
  birthdays (including birthdays without a year), notes and email, phone, address and social contact details
  are imported; a vCard export from a phone brings its name, birthday and contact methods. **Nothing is imported
  until you confirm a preview** of every person found: who has a problem (a missing name blocks the row; a
  birthday that can't be read, a too-long note or an email address that doesn't look right is left out or kept
  as other details, with a note), who may already be in your list or repeats an earlier row of the file
  (those start unticked and link to the existing profile in a new tab), and what each would be created with.
  Importing creates everyone you ticked in a single save, all or nothing, and opens the list sorted by
  recently added. Files up to 1 MB and 2,000 people are read; the file is read in memory and is never stored
  or logged, and nothing about it leaves the app. vCard and CSV are read by Relio's own code (no new package);
  UTF-8, UTF-16 and Windows-1252 files and quoted-printable text are handled, and dates are read strictly (a
  two-digit year or a month name is reported, not guessed). Tags and relationship types are not imported
  (follow-up: import groups as tags). No migration.
- Merge duplicate person profiles (issue #28), epic #21. A profile's **More** menu has **Merge with…**,
  and the possible-duplicate warning in edit mode has **Merge instead**; both open
  `/people/{id}/merge`, where the profile you came from is the one you **keep**. First you choose the other
  profile (the suggested duplicates first, then a search over all your people, archived ones included, that
  ignores case and accents), then you see the two side by side and choose what to keep **only where they
  disagree** (name, nickname, relationship, birthday, how you met, details, status; the texts also offer
  **Keep both**; a profile that is archived while the other is active defaults to active). Contact methods
  are united with repeats combined (the same email in different capitals is one email, and a label the kept
  one lacked is taken from the repeat), tags are united, and the last-contact date is recomputed from the
  surviving interaction history. Notes move with their text and pin state; interaction participants move
  without copying a shared interaction. A preview
  shows exactly what you will get. A confirmation names both people and says it can't be undone; then
  everything recorded about the other profile moves to the one you keep and the other is removed, in a
  single transaction (`IPersonMergeService`, one save), and you land on the merged profile ("Profiles
  merged"). Merging profiles that together have more than 20 contact methods or tags, or two texts too long
  to keep both, is refused with an explanation and changes nothing. A profile that isn't yours, or that
  disappeared meanwhile, is reported like one that doesn't exist. No migration. Future things that belong to
  a person (reminders, difficult moments) must add a line to
  `PersonMergeService.MoveDependentsAsync`: `PersonMergeChecklistTests` and a SQL Server foreign-key test fail
  until they do, and `Every_person_column_has_a_merge_rule` does the same for a new column on `Person`.
- Detect possible duplicate people (issue #27), epic #21. Adding a person, or renaming one, now checks
  your own list when you press Save and warns when up to five profiles look like the same person: the
  same or a similar name (ignoring case, accents, punctuation and spacing - "Jon Smith" finds "John
  Smith", "José García" finds "Jose Garcia", a missing last name or a matching nickname counts), the
  same email address, or the same phone number (the last eight digits, so a country code does not
  matter). Archived people are included and labelled. Each match opens in a new tab so the form you
  were filling in survives, and **Save anyway** carries on; changing the name or contact details and
  saving again checks again. The check is deliberately stricter for short names ("Mark" and "Mary" do
  not warn). Other users' people are never compared, names are compared in memory only, and nothing
  new is stored or logged. There is no migration. Follow-ups: checking on blur, a nickname dictionary
  (Bob/Robert), and "Merge instead" from the warning (issue #28).
- Archive, restore and delete people (issue #26), epic #21. Each profile has a **More** menu next to
  Edit with **Archive** (**Restore** for an archived person) and **Delete**. Archiving and restoring are
  reversible, so they happen at once and say "Person archived" / "Person restored"; an archived person
  shows a quiet note under their name ("Archived on 3 March", in your time zone, "Hidden from your lists
  and reminders. Everything you recorded is kept.") with a **Restore** button, and archiving never touches
  anything recorded about them. **Delete** asks first ("Delete [person]? ... It can't be undone.",
  button "Delete permanently") and is permanent: `IPeopleService.DeleteAsync` removes the person, their
  contact methods, notes and interaction participation in one save, deleting a shared interaction only
  when no participant remains. It keeps your tags (and other people's links to them) and relationship
  types, and works on archived people too. Afterwards you land on the people list and Back
  does not return to the deleted profile. No migration: the foreign keys already cascade, and the service
  also removes the dependents explicitly because the InMemory provider (unit tests) would otherwise leave
  orphans. `PersonDeleteChecklistTests` fails when a new entity references a person without being added to
  `PeopleService.RemoveDependentsAsync`; a SQL Server test proves every foreign key to `People` cascades.
  AGENTS.md now spells out which features exclude archived people and which include them.
- Manage relationship types and tags (issue #25), epic #21. Settings has a new "Relationship types and
  tags" section with two pages: `/settings/relationship-types` and `/settings/tags`. Each lists your
  labels with how many people have them (archived people included), lets you **add** one (Enter works),
  **rename** one in a dialog (changing only the casing is fine; a rename shows for everyone who has it)
  and **remove** one. Removing a relationship type that people have asks what they should have instead:
  move them to another type or leave it empty (everyone is moved and the type removed in one save);
  removing a tag takes it off everyone and says how many people that is. A name is trimmed, at most 50
  characters and unique ignoring case, for both kinds; a clash that two requests race to create is
  refused calmly by the database's unique index. You can remove every relationship type (the person form
  then says where to add them), and removing a default never brings it back. No migration. Follow-ups:
  reordering relationship types, restoring the defaults, and an administrator-visible count limit if
  ever needed. The shared `SqlServerErrors.IsUniqueIndexViolation` now backs the tag-name race from #24 too.
- Edit person details: contact methods, tags and relationship type (issue #24), epic #21. Each
  profile has an **Edit** button opening `/people/{id}/edit`, the same form as "Add a person" started
  from what is saved. It now carries **contact methods** (email, phone, address, social, other, each
  with an optional label such as Work or Mobile, up to 20 per person) and **tags** (type to find one
  or to create it; created only when the person is saved, matched ignoring case, up to 20 per person),
  on both pages. The profile shows tags as labels and contact methods in the order they were
  arranged; only emails (`mailto:`) and phone numbers (`tel:`) become links, built from validated
  data. Migration `AddContactMethods` adds the `ContactMethods` table (kind stored as text with a
  check constraint, cascade delete with the person, indexes on `(OwnerId, PersonId)` and
  `(OwnerId, NormalizedValue)` for duplicate detection in #27). Saving replaces the person's whole
  contact method list by id; it is **last write wins**, but a form left open after a contact method was
  deleted elsewhere says "This profile changed in another tab or window" instead of putting it back.
  The form no longer submits on Enter (Enter chooses a tag); use the **Save** button. Follow-up: clickjacking
  protection (`frame-ancestors`) belongs to issue #60.
- People list with sorting and archived view (issue #23), epic #21. `/people` now sorts by **name**,
  **recently added** or **last contacted** (a "Sort by" select), has a **Show archived** switch that
  mixes archived people into the list with an "Archived" label, shows a count line ("12 people",
  "5 people, 2 archived") and pages through 50 people at a time with a pagination control. Each row
  gets a second line with the relationship type and when you last made contact ("Last contacted 12
  days ago", "Not contacted yet"). The view lives in the address - `/people?sort=contacted&archived=true&page=2`,
  defaults left out, anything invalid falling back to the default - so Back restores it and a reload
  keeps it; only a sort token, `true` and a page number ever go in the address, never a name. When
  everyone is archived the page says so and offers to show them. `IPeopleService.ListPageAsync`
  returns one page of lightweight rows (`PersonListItem`: no how-we-met text, details, birthday or
  nickname) plus the user's active and archived totals; every ordering ends with the id so a paged
  list never repeats or skips a row, and never-contacted people are explicitly sorted last. `Person.LastContactedOn`
  is maintained from each person's latest surviving interaction, as a calendar date in that user's time zone
  (issue #34). Demo profiles still include examples for every sort state.
  Migration `AddPeopleListSorting`: adds the nullable `People.LastContactedOn` (`date`) and replaces
  `IX_People_OwnerId_IsArchived` with three composite indexes (`OwnerId, IsArchived` plus first and
  last name / `CreatedAtUtc` / `LastContactedOn`); no backfill. `DateDisplay.FormatRelative` is the
  shared "Today / Yesterday / 12 days ago / 3 March" helper. Unit, bUnit, SQL Server integration
  (every sort, case-insensitive names, nulls last, tie-stable paging, the indexes and the column) and
  Playwright end-to-end coverage. See the "People" section of AGENTS.md.
- Create a person profile (issue #22), the first issue of epic #21. `/people` is now a real list of
  your active people (monogram, name and relationship type, each row a link; an empty state with an
  "Add a person" button when there is no one yet), `/people/new` adds a person and
  `/people/{id}` shows their profile. Only the first name is required; a profile can also hold a last
  name, a nickname, a relationship type, a birthday whose **year is optional** ("14 March"), how you
  met and free-form details. Text is trimmed, blank optional text is stored as nothing, and what is
  rejected (over-long text, a birthday without a day or month, one that does not exist such as 31
  April, one with a year after today in *your* time zone) comes back from the service as error codes
  (`PersonValidationException`) that the form words next to the field. Relationship types are a
  per-user list (`RelationshipType`, unique per user, six defaults: Family, Partner, Friend,
  Colleague, Acquaintance, Other) seeded when an account is created and by the migration for accounts
  that already exist; a person may only point at one of its owner's types (a foreign id, so another
  user's id is refused with the same message as an unknown one). A profile that does not exist and one
  that belongs to someone else look identical, and browser tab titles never contain a name.
  Migration `AddPersonProfile`: adds `RelationshipTypes` and the new `People` columns, **splits
  `People.Birthday` into `BirthdayYear`/`BirthdayMonth`/`BirthdayDay`** (existing birthdays are
  copied), gives every existing account the default relationship types and adds check constraints on
  the birthday columns. Downgrading restores birthdays that have a year and **drops those that do not**
  (a `date` column cannot hold them). The people service now reads untracked and clears the change
  tracker after every write, because the scoped `DbContext` of an interactive page lives as long as
  the circuit. `IPeopleService`'s request records are now property records with the new fields, and
  `UserCalendar.NextOccurrence` gained a `Birthday` overload (29 February without a year is observed on
  28 February in non-leap years too). Unit, bUnit, SQL Server integration (check constraints, unique
  names, delete rule, and the migration run against a database holding real rows) and Playwright
  end-to-end coverage. See the "People" section and the "User-scoped data pattern" section of
  AGENTS.md.
- Optional two-factor authentication with an authenticator app (issue #20), completing epic #14.
  Turn it on under *Sign-in and security* in settings: `/Account/Manage/EnableAuthenticator` shows a
  QR code (generated on the server as an inline SVG with the new `Net.Codecrete.QrCodeGenerator`
  package - no script, no third-party request) and the key for manual entry, and asks for a code
  from the app plus the current password before anything is turned on; ten recovery codes are then
  shown once, in the response that created them. Signing in then asks for a code
  (`/Account/LoginWith2fa`) or, instead, a recovery code (`/Account/LoginWithRecoveryCode`); the
  return URL and "Remember me" survive the extra step and both pages validate the return URL again.
  There is deliberately no "remember this device". Turning it off, switching to a different
  authenticator app (turn off, then set up again with a new key) and generating new recovery codes
  each ask for the current password, and turning it on or off ends the account's other sessions.
  Wrong authenticator codes, wrong recovery codes and wrong passwords share one lockout budget
  (`Account:Lockout`): `RelioSignInManager` now overrides Identity's recovery-code sign-in, which
  neither checked lockout nor counted failures, and also refuses an account an administrator
  disabled between the password and the code. A password reset does not turn two-factor
  authentication off. The two-factor cookies are `HttpOnly`, `SameSite=Lax` and `Secure` outside
  Development like the sign-in cookie. `/settings` shows whether it is on and warns calmly when three
  or fewer recovery codes are left (`ITwoFactorStatusService`, read untracked so a long-lived circuit
  never shows a stale answer). No migration (Identity's `TwoFactorEnabled` column and `AspNetUserTokens`
  already exist); the authenticator key and recovery codes are stored the way Identity stores them, in
  plain text - protecting them at rest is a follow-up. The README documents how an operator recovers
  an account that lost its phone and every recovery code. New design tokens `qr-ink`/`qr-ground` (dark
  on light in both themes, since scanners cannot read an inverted code). Unit, bUnit, SQL Server
  integration and Playwright end-to-end coverage (the end-to-end tests compute the app's codes with a
  small RFC 6238 helper). See the "Two-factor authentication" bullet in the "Accounts and
  authentication" section of AGENTS.md.
- Self-hosted administration (issue #19): the first account created on an instance becomes its
  **Administrator** (an ASP.NET Core Identity role seeded in the migration; only an account that is
  the *only* one right after its own insert is promoted, guarded by a process-wide lock plus a
  post-insert count, so concurrent sign-ups can never produce two administrators and an existing
  instance without an administrator can't be claimed by whoever registers next). `Registration:Mode`
  (`Open` by default, `InviteOnly`, `Closed`; a misspelt value stops the app at startup) controls who
  can sign up, and the first account is always allowed so a fresh Closed or invitation-only instance can
  still get its administrator; the service enforces the mode, not just the page (a closed instance
  answers `/Account/Register` with 403 and a calm explanation, and the login page hides "Create an
  account"). In invitation-only mode an administrator creates single-use, email-bound,
  `Registration:InvitationLifetime` (7 days) invitation links from `/admin/users` - Relio stores only a
  hash of the token, never puts the email in the link and does not email it. The same page lists
  accounts and lets an administrator disable and re-enable them: a dedicated `IsDisabled` flag (not
  lockout, which a password reset clears) that also rotates the security stamp, so open sessions end
  within the new `Account:Session:ValidationInterval` (30 minutes by default, also used for the circuit
  revalidation); the sign-in message "This account has been disabled" is only shown after the correct
  password (no account enumeration) through the new `RelioSignInManager`. An administrator can't
  disable themselves, a disabled administrator can't administer, and administrators never see any
  user's people, notes or moments (the administration services can't even express them - a reflection
  test guards it). `Administration:AdministratorEmail` promotes an existing account at startup for
  instances that predate this change. The demo account is an Administrator. Adds migration
  `AddSelfHostedAdministration` (`AspNetUsers.IsDisabled`, `RegistrationInvitations`, the Administrator
  role row - an operator-created role named "Administrator" must be removed first) and unit, bUnit, SQL
  Server integration and Playwright end-to-end coverage. See the "Self-hosted administration" bullet in
  the "Accounts and authentication" section of AGENTS.md.
- Account settings (issue #18): `/settings` is now the account hub - a display name (optional, up to
  100 characters, stored on `UserProfile.DisplayName` and read/written through the new
  `IUserProfileService`), the time zone (a searchable picker over `TimeZoneIds.GetAvailableIds()`
  that also accepts a typed IANA id, and *suggests* the browser's time zone when it differs from the
  saved one - never saved without pressing Save), links to change email and password, a disabled
  placeholder for reminder email preferences (arrives with #40), and the existing appearance
  control. Changing the password (`/Account/Manage/ChangePassword`) requires the current password,
  keeps the session that changed it signed in (`SignInManager.RefreshSignInAsync`) and signs out
  every other session. Changing the email (`/Account/Manage/Email`) requires the current password
  too: with `Email:Provider=Smtp` the new address is stored as `RelioUser.PendingEmail` and a
  single-use confirmation link (carrying only a user id and an opaque token, never an email address)
  is sent to it, and the address only changes once `/Account/ConfirmEmailChange` is followed - a newer
  request invalidates an older link, and an address that is already registered gets the identical
  response as a usable one (no account enumeration); with `Email:Provider=None` no confirmation is
  possible, so the change applies immediately and the page says so. The signed-in email in the app
  bar now links to `/settings`, and the SMTP confirmation email's wording is neutral so it serves
  both sign-up and email changes. Adds two migrations (`AddUserProfileDisplayName`,
  `AddRelioUserPendingEmail`), `EmailOptions.CanSendEmail`, and unit, bUnit, SQL Server integration
  and Playwright end-to-end coverage. `App.razor` now chooses the `<Routes>` render mode per request
  (`HttpContext.AcceptsInteractiveRouting()`), which pages marked `[ExcludeFromInteractiveRouting]`
  need to stay static SSR once a circuit can start on them (the signed-in change email/password
  pages are the first such pages). See the "Account settings" bullet in the "Accounts and
  authentication" section of AGENTS.md.
- Password reset by email (issue #17): `/Account/ForgotPassword` → `/Account/ResetPassword` →
  `/Account/ResetPasswordConfirmation`, linked from "Forgot your password?" on the login page. The
  forgot-password page always shows the same confirmation message regardless of whether the
  submitted email matches an account or is confirmed (no account enumeration); the two code paths
  also do near-identical work (both generate a password reset token, one against a throwaway
  never-persisted user) so the response time does not become an enumeration oracle either. With
  `Email:Provider=None` it shows an instance-wide note that password reset by email isn't available,
  without revealing anything about any particular account. Reset links expire after
  `Account:PasswordReset:TokenLifespan` (1 hour by default) via a dedicated
  `Relio.Web.Identity.PasswordResetTokenProvider`, so this lifespan can be configured independently
  of email confirmation's token lifespan (both previously shared Identity's "Default" provider).
  Links are single-use: a successful reset rotates the account's security stamp (Identity's own
  password-change behaviour), which the token is bound to, and also clears any lockout
  (`ResetAccessFailedCountAsync`/`SetLockoutEndDateAsync`) and signs out every other active session
  at its next circuit revalidation (within 30 minutes). An invalid, expired or already-used link
  shows the same calm generic message with a link to request a new one. See the "Accounts and
  authentication" section of AGENTS.md.
- Login, logout and session persistence (issue #16): hardens #15's minimal login/logout into the
  real thing. Account lockout (`IdentityOptions.Lockout`, configurable via the new
  `Account:Lockout` section - 5 failed attempts, 15 minutes, enabled for new accounts) with a calm,
  non-specific message; "Remember me" (persistent vs. session sign-in cookie, `Account:Cookie:ExpireTimeSpan`,
  14 days); a hardened application cookie (`HttpOnly`, `SameSite=Lax`, `Secure` outside
  Development); a single generic "Email or password is incorrect" message for both an unknown
  email and a wrong password (no account enumeration); open-redirect protection for `returnUrl`
  (`Relio.Web.Security.ReturnUrlValidator`, rejecting a different host, a protocol-relative
  `//evil.example`, and the `/\evil.example` backslash variant); `Cache-Control: no-store` on every
  authenticated response, so the back button after signing out never reveals a cached page; and
  `Relio.Web.Security.RelioRevalidatingAuthenticationStateProvider`, which re-checks a connected
  Blazor Server circuit's security stamp every 30 minutes so a revoked session is actually noticed
  instead of staying "signed in" for the rest of a long-lived circuit. Sign-in
  successes/failures/lockouts/sign-outs are logged with the user id only, never the email or
  password. See the "Accounts and authentication" section of AGENTS.md.
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
  Production), seeding a synthetic demo account with fictional sample people, for exploring
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

- Settings (`/settings`), and the administration page when sign-up is invitation-only, failed on SQL
  Server with EF Core's "A second operation was started on this context instance before a previous
  operation completed", both when the page was first rendered and again once it became interactive.
  The page's sections load their data side by side, Blazor starts the next section's load while the
  previous one is still waiting for the database, and every component on a page shares one
  `DbContext`. Every Relio data service now runs one database operation at a time per `DbContext`
  (a "database lane" that `AddRelioData` applies to each service), so loads and saves started close
  together queue instead of colliding. This also covers the people pages and pages still to come. It
  went unnoticed because the end-to-end tests use the InMemory provider, whose queries finish
  synchronously and never overlap. New SQL Server regression tests start several services' reads and
  saves together on one context, and an end-to-end test loads the settings, administration and
  add-person pages against SQL Server. Saving a display name or time zone now also clears the change
  tracker afterwards, like every other write on a circuit's long-lived `DbContext`. See "One
  database operation at a time" in AGENTS.md.
- The app bar's appearance (System/Light/Dark) menu never opened when actually clicked in a
  browser: `ThemeModeMenu.razor` wrapped a `MudIconButton` in `MudMenu`'s `ActivatorContent`, and
  that two-element activator (a wrapping `div` plus an independently-interactive button) did not
  trigger the menu's open state. Switched to `MudMenu`'s own `Icon`/`AriaLabel` parameters, which
  render and wire up a single activator button. Found by `Relio.Web.E2ETests.ThemeTests` - the
  first time this app had been driven in an actual browser.
- Signed-out visitors got no CSS or JavaScript: `app.MapStaticAssets()` fell under the fallback
  authorization policy, so every asset request (app.css, MudBlazor, `_framework/blazor.web.js`,
  fonts, `js/timezone.js`, favicon) was redirected to `/Account/Login` and the sign-in, register and
  password-reset pages rendered unstyled. Static assets are now `AllowAnonymous`. Because
  `blazor.web.js` now loads on those pages, enhanced navigation applies between them; the
  "Create an account" link opts out (`data-enhance-nav="false"`) so `Register.razor`'s inline
  time-zone script still runs. Covered by `AnonymousAssetsTests` and
  `AnonymousEnhancedNavigationTests`.
