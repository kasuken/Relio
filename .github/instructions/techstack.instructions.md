---
description: Technical stack and coding guidelines for Relio.
applyTo: "**"
---

# Relio - Technical Stack Instructions

## Frontend

* .NET 10 Blazor Web App, Interactive Server rendering by default
* MudBlazor for all UI components; do not introduce other UI frameworks
* Responsive design for desktop, tablet and mobile
* Minimalist interface: notes, interactions and difficult moments must be visually distinct

## Backend

* ASP.NET Core (.NET 10), service-based architecture, dependency injection for all services
* Minimal APIs only when an HTTP endpoint is actually needed
* Background work (reminders, email) through hosted services, idempotent and safe to restart

## Database

* SQL Server with Entity Framework Core, code first, entity configurations, migrations
* Async database operations; index the columns used by timelines, dashboards and search
* Avoid Dapper, raw SQL and stored procedures unless justified

## Authentication

* ASP.NET Core Identity with local accounts (works for SaaS and self-hosted)
* Access the current user through an abstraction, never `HttpContext` in services

## Architecture

* `Relio.Domain`: entities and invariants, no dependencies
* `Relio.Application`: use cases and service interfaces
* `Relio.Data`: EF Core DbContext, configurations, migrations
* `Relio.Web`: Blazor UI, Identity, hosting, health checks

## Privacy

* All data is scoped to its owner; tests must cover cross-user access
* Never log the content of notes, interactions or difficult moments
* No secrets in `appsettings*.json`; use user secrets locally and app settings in hosting
