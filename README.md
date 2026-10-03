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

Email is optional. By default (`Email:Provider=None`) Relio sends no email at all and new accounts
do not need to confirm their address - this is what a self-hosted instance with no mail server
gets out of the box. Set `Email:Provider=Smtp` and `Email:Smtp:Host`/`Port`/`Username`/`FromAddress`
(and `Email:Smtp:Password` via user secrets or an environment variable, never in
`appsettings*.json`) to require email confirmation and send real account emails.

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
