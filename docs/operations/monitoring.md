# Monitoring and structured logging

How an operator finds out that Relio is failing, without Relio ever writing private content to a log.
It works on hosted and self-hosted instances, and with `HostedFeatures:ProductMetrics:Enabled=false`
(the default): nothing here depends on product analytics, and Relio makes no outbound request to
send logs anywhere.

## 1. What is observable

Relio reports operational failures as log events with stable ids. The ids are a contract (the alert
query below depends on them); they are never renumbered or reused.

| Id | Event name | Level | Emitted when | Payload |
|---|---|---|---|---|
| 9001 | `RelioUnhandledHttpFailure` | Error | An exception escaped an HTTP request (static pages, endpoints, the login and account pages). | `ExceptionType` |
| 9002 | `RelioCircuitFailure` | Error | An interactive (Blazor Server) circuit failed with an unhandled exception and was terminated. The user sees the reload banner. | `ExceptionType`, `FrameworkEvent` (e.g. `CircuitHost.CircuitUnhandledException`), `CircuitRef` |
| 9003 | `RelioReminderJobFailure` | Error | A reminder scheduler run failed. The service keeps running and tries again at the next interval. | `ExceptionType` |
| 9004 | `RelioProductMetricsRetentionFailure` | Error | The product metrics retention run failed. | `ExceptionType` |
| 9005 | `RelioBackupFailure` | Error | Reserved for backup automation (#119). Nothing emits it yet. | `ExceptionType` |

The payload is only the exception **type name**, the framework event name and `CircuitRef`, a 12
character one-way hash of the circuit id that lets you correlate several lines from one circuit without
revealing the id itself. It never contains an exception message, a stack trace, form values, notes,
difficult moments, reminder titles, names, emails, routes or user ids.

Definitions live in `Relio.Web/Diagnostics/RelioOperationalEvents.cs`.

## 2. Privacy guarantees

- Framework categories that can carry private values (Blazor Server, SignalR, Kestrel, EF Core
  commands, exception middleware, ...) stay forced to `None` for every provider, whatever the
  configuration says. See `Relio.Web/Security/PrivacyLoggingBuilderExtensions.cs`.
- Blazor's own circuit logs cannot simply be switched on: the exception message is a structured
  property of its "Unhandled exception rendering component" event, so even the formatted text can
  contain a note or a difficult moment. Instead one dedicated provider
  (`CircuitFailureSignalLoggerProvider`) is allowed to see Error and above from those categories. It
  never formats the message and never reads the exception beyond its type, and re-emits event 9002. This
  is the single carve-out from the suppression rule, and tests prove that note and difficult-moment
  sentinels never reach any provider, including in a real circuit.
- Do not enable `Logging:Console:FormatterOptions:IncludeScopes` (scopes carry request paths and
  connection ids), `DetailedErrors`, EF Core `EnableSensitiveDataLogging` or HTTP logging. The
  suppression rules will not protect values you opt into in other categories.

## 3. Health checks

- `/health/live`: the process is up. Used by the App Service `healthCheckPath` and by the Docker image
  `HEALTHCHECK`.
- `/health/ready`: the process is up and the database is reachable.

## 4. Hosted (Azure)

The Bicep templates set `Logging__Console__FormatterName=json`, so every console line is one JSON
object, and the existing diagnostic setting ships `AppServiceConsoleLogs` to the Log Analytics
workspace. Failure events show up there a few minutes after they happen.

Alerting is optional and off by default. To create an email action group and one alert rule, pass the
operator address at deployment time (it is intentionally not stored in `main.parameters.json`):

```bash
az deployment sub create --location <region> --template-file infra/main.bicep \
  --parameters infra/main.parameters.json alertEmailAddress=ops@example.com
```

With the parameter empty (the default) the monitoring module deploys nothing. The rule runs every five
minutes over a 15 minute window and raises one alert per event name when any of 9001-9005 occurred. It
uses this query (identical to `infra/modules/monitoring.bicep`), which you can also run by hand in Log
Analytics:

```kusto
let relioEvents = datatable(EventId:int, EventName:string) [
    9001, 'RelioUnhandledHttpFailure',
    9002, 'RelioCircuitFailure',
    9003, 'RelioReminderJobFailure',
    9004, 'RelioProductMetricsRetentionFailure',
    9005, 'RelioBackupFailure'
];
AppServiceConsoleLogs
| where ResultDescription contains '"EventId":90'
| extend entry = parse_json(ResultDescription)
| extend EventId = toint(entry.EventId), ExceptionType = tostring(entry.State.ExceptionType)
| join kind=inner relioEvents on EventId
| project TimeGenerated, EventName, ExceptionType
```

To check the pipeline end to end, make a harmless failure happen (for example stop the SQL database
briefly: the reminder job reports 9003 at its next run), then look for the row in Log Analytics and the
alert email.

## 5. Self-hosted (Docker Compose)

By default the container writes human-readable console lines, where the event id appears in brackets
after the category. Set `RELIO_LOG_FORMAT=json` in `.env` for one JSON object per line, which log
shippers can parse. `docker-compose.yml` rotates container logs (5 files of 10 MB).

```bash
# json format
docker compose logs relio | grep -E '"EventId":900[1-5]'
# default (simple) format
docker compose logs relio | grep -E '\[900[1-5]\]'
# health, as Docker sees it
docker inspect --format '{{.State.Health.Status}}' relio-app
```

Collecting those logs centrally (Loki and Promtail, Vector, journald, a hosted service, ...) is the
operator's choice; Relio itself sends nothing to third parties.

## 6. Known limits

- Debug-level framework plumbing failures (for example a JS interop call that throws, or an exception
  inside a circuit handler's inbound-activity check) are not translated into events. Only failures the
  framework reports at Error level count, which includes every unhandled render, lifecycle and event
  handler exception that ends a circuit.
- Failures during page rendering on static pages surface as 9001, not 9002.
- Event 9005 stays reserved for in-app backup reporting. The shell-based backup sidecar (#119) runs
  outside the web app and does not emit it: its failure signal is the `relio-backup` container health,
  `relio-backup status` and the optional ping URLs, and for the hosted service the failing
  `hosted-backup-check.yml` run. See [backup-restore.md](backup-restore.md). Route those signals into
  the same alert channel as 9001-9005.
