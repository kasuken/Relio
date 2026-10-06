# Relio

Relio is a private relationship memory system. It helps you remember the important
details about the people in your life: what you talked about, what matters to them,
when to get back in touch, and what you learned from difficult moments.

Relio is not a CRM and not a social network. It is a calm, private place to look
after your relationships, available as a hosted service or self-hosted.

> **Status:** early development. The MVP scope is tracked in the
> [epics](https://github.com/kasuken/Relio/issues?q=is%3Aissue+label%3Aepic).

## Features (MVP)

- **People**: profiles with relationship type, contact methods, tags and birthdays
- **Timeline**: interactions and notes per person, in chronological order
- **Reminders**: reconnect follow-ups and birthday reminders, by email and on the dashboard
- **Difficult moments**: record tensions, triggers, resolutions and lessons learned
- **Dashboard**: upcoming reminders, recent interactions and recently added people
- **Search**: find people by name and filter by tag or relationship type

## Tech stack

- .NET 10, Blazor Web App (Interactive Server)
- MudBlazor
- Entity Framework Core and SQL Server
- ASP.NET Core Identity

## Getting started

Prerequisites: the .NET 10 SDK (see `global.json`) and a SQL Server instance (local, Docker or
Azure SQL).

Relio reads its database connection string from the `ConnectionStrings:Relio` configuration key.
It is never committed; set it locally with user secrets:

```bash
dotnet user-secrets set ConnectionStrings:Relio "Server=localhost;Database=Relio;Trusted_Connection=True;TrustServerCertificate=True;" --project Relio.Web
```

When hosting, set the `ConnectionStrings__Relio` environment variable instead.

```bash
dotnet restore Relio.slnx
dotnet build Relio.slnx
dotnet run --project Relio.Web
```

In Development, `Relio.Web` applies pending EF Core migrations automatically on startup. In every
other environment, applying migrations is an explicit, separate step:

```bash
dotnet tool restore
dotnet ef database update --project Relio.Data --startup-project Relio.Web
```

`/health/live` reports whether the process is running; `/health/ready` also checks the database
and returns unhealthy when it cannot be reached.

### Accounts

Relio uses ASP.NET Core Identity with local accounts only (no social login). Register at
`/Account/Register`; a strong password is required (at least 12 characters, with upper and lower
case, a digit and a symbol - see AGENTS.md for the rationale). Every page except the account pages
and the health endpoints requires sign-in.

Sign in at `/Account/Login`; "Remember me" issues a persistent cookie that survives closing the
browser, otherwise the session cookie ends when the browser does. Five failed sign-ins in a row
lock the account out for 15 minutes (`Account:Lockout`, configurable - see AGENTS.md's "Accounts
and authentication" section).

Forgot your password? `/Account/ForgotPassword` emails a reset link (`Account:PasswordReset:TokenLifespan`,
1 hour by default) that works once; the page always shows the same message regardless of whether
the email matches an account, so it never reveals who has registered.

Email is optional. By default (`Email:Provider=None`) Relio sends no email at all, new accounts
do not need to confirm their address, and the forgot-password page says so - this is what a
self-hosted instance with no mail server gets out of the box. Set `Email:Provider=Smtp` and
`Email:Smtp:Host`/`Port`/`Username`/`FromAddress` (and `Email:Smtp:Password` via user secrets or an
environment variable, never in `appsettings*.json`) to require email confirmation and send real
account emails, including password resets.

Account settings live at `/settings` (also reachable from your email address in the app bar): set a
display name and your time zone (the browser's is suggested when it differs from the saved one),
change your password (every other signed-in device is signed out) and change your email address.
Changing either asks for your current password. With email configured, a new address only takes
effect once you follow the confirmation link sent to it; with `Email:Provider=None` there is no way
to send that link, so the change applies immediately and the page says so.

**Two-factor authentication** is optional and per account. Turn it on under *Sign-in and security* in
settings (`/Account/Manage/TwoFactorAuthentication`): scan the QR code with any authenticator app
(or type the key shown next to it), enter the code the app shows and your current password, and save
the ten recovery codes Relio then shows - once, never again. From then on signing in asks for a code
from the app after the password; if you lose your phone, a recovery code works instead (each one
once). Wrong codes count towards the same lockout as wrong passwords. Turning it off, switching to a
different app and generating new recovery codes all ask for your current password. Resetting a
forgotten password does **not** turn two-factor authentication off. Relio does not offer "remember this
device": every sign-in asks for a code.

If someone loses their phone *and* every recovery code, there is no way to recover in the app, by
design. The person who runs the instance can turn it off for that account directly in the database
(the person can then sign in with their password and set it up again):

```sql
DECLARE @id nvarchar(450) = (SELECT Id FROM AspNetUsers WHERE NormalizedEmail = N'PERSON@EXAMPLE.COM');
DELETE FROM AspNetUserTokens WHERE UserId = @id;  -- the authenticator key and the recovery codes
UPDATE AspNetUsers SET TwoFactorEnabled = 0, SecurityStamp = CONVERT(nvarchar(36), NEWID()) WHERE Id = @id;
```

The authenticator key and recovery codes are stored in `AspNetUserTokens` as Identity keeps them, in
plain text, so protect the database and its backups the way you protect the password hashes.

### Administration and sign-up control

On a new instance the **first account you create becomes the administrator**. The administrator gets
an "Administration" link in the drawer (`/admin/users`) to see who has an account and to disable or
re-enable accounts. An administrator never sees anyone's people, notes or moments - only the list of
accounts and whether they can sign in.

Who may sign up is `Registration:Mode`: `Open` (the default - anyone who can reach the instance),
`InviteOnly` or `Closed`. Set it as an environment variable, e.g. `Registration__Mode=InviteOnly`.
**For a self-hosted instance that is reachable from the internet, set `InviteOnly` or `Closed` once
you have created your own account** - the first account can always be created, whatever the mode. In
`InviteOnly` mode the administrator enters an email address on the Administration page and gets a
single-use link (valid for `Registration:InvitationLifetime`, 7 days by default) to send to that person
themselves; Relio does not email invitations, shows the link once, and the link only works for that
address. An unknown `Registration:Mode` value stops the app at startup rather than leaving sign-up open.

Disabling an account stops it from signing in and ends its open sessions within
`Account:Session:ValidationInterval` (30 minutes by default; lower it to end sessions sooner).
Nothing the account owns is changed, and enabling it again restores access. You can't disable your
own account.

Upgrading an instance that already had accounts? Nobody is an administrator yet (the first-account
rule deliberately does not hand the role to whoever registers next). Set
`Administration__AdministratorEmail` to the email address of the account that should be the
administrator and restart; sign out and in again to see the Administration link. The same setting
recovers an instance that has no administrator. With `DemoData:Enabled=true` the demo account is the
administrator and accounts you register are not.

### Run locally without SQL Server

For a quick look at the app with no database to set up, run against the EF Core InMemory provider
with sample data seeded (a demo account and ~8 example people - see
`Relio.Data.Seeding.DemoDataSeeder`):

```bash
Database__Provider=InMemory DemoData__Enabled=true dotnet run --project Relio.Web
```

Sign in at `/Account/Login` with `demo@relio.local` / `Relio-Demo#2026` (test/demo only - never
reuse this password for anything real). `DemoData:Enabled` only ever seeds outside the Production
environment; it refuses (and logs an error) if set in Production.

## Testing

```bash
dotnet test Relio.slnx
```

Most tests need nothing extra. `Relio.Data.IntegrationTests` runs the same cross-user scenarios
against a real SQL Server database and is skipped automatically when no server is available (it
always runs in CI). To run it locally, start a disposable server and set the connection string
for that one command:

```bash
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="Your_password123!" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
ConnectionStrings__Relio="Server=localhost,1433;Database=Relio;User Id=sa;Password=Your_password123!;Encrypt=False;TrustServerCertificate=True;" dotnet test
```

## Contributing

Contributions are welcome. Please read the
[contributing guide](https://github.com/kasuken/.github/blob/main/CONTRIBUTING.md).
You will be asked to sign the [CLA](https://github.com/kasuken/.github/blob/main/CLA.md)
on your first pull request.

## License

Relio is licensed under the [GNU Affero General Public License v3.0 only](LICENSE).
