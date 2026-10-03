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

## Contributing

Contributions are welcome. Please read the
[contributing guide](https://github.com/kasuken/.github/blob/main/CONTRIBUTING.md).
You will be asked to sign the [CLA](https://github.com/kasuken/.github/blob/main/CLA.md)
on your first pull request.

## License

Relio is licensed under the [GNU Affero General Public License v3.0 only](LICENSE).
