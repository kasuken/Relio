# Product metrics privacy contract

Relio's optional product metrics are an instance-wide, aggregate-only view. Collection is
disabled unless an operator explicitly sets
`HostedFeatures:ProductMetrics:Enabled=true`; the same setting applies to hosted and
self-hosted instances. Environment, hosting mode, and deployment type never enable it
implicitly. The default `false` path does not invoke the activity collector and creates no
metrics contribution.

## Collected data and access

When enabled, the interactive circuit records at most one contribution per authenticated,
non-disabled account per UTC calendar day. A contribution contains only:

- the owner's existing internal user id;
- the UTC date the account was first observed after metrics were enabled;
- the most recent UTC activity date;
- whether activity was observed from cohort age day 30 through day 59; and
- the fixed UTC expiry instant, cohort start plus 90 days.

This is owner-attributable pseudonymous metadata, not anonymous data. It contains no account
name or email, person name, note, description, reminder title, request or URL, IP address,
device/session identifier, visitor fingerprint, or independent event history. No metrics data
is sent to an external analytics or AI service, and the application adds no third-party
scripts or endpoints.

Only an active Administrator can request the report. The report service verifies the current
user's active account and Administrator role in the database for each request; a role in a
stale sign-in ticket is not sufficient. The report contains aggregate counts and ratios only.
It has no person-level or account-level rows, content drilldown, or arbitrary narrow filters.
The retention runner is a trusted background-only operation; it returns a deletion count and
does not expose contribution rows or owner ids.

## Observation cohorts and retention

A cohort begins on the first observed UTC activity date **after the operator enables
metrics**, not on account registration. Existing accounts therefore enter observation over
time; enabling metrics does not reconstruct account-registration cohorts or past activity.

A return qualifies only when that same account is observed on cohort age day 30 through day
59, inclusive. Day 29 does not qualify; days 30 and 59 do; day 60 does not. A completed
reporting cohort is one first observed 60 through 89 UTC days before the report date. The
retention percentage is:

`completed cohorts observed on days 30–59 / all completed cohorts aged 60–89 × 100`

An eligible cohort with no qualifying return contributes zero to the numerator. Before any
completed cohorts exist, the rate is **no data** (`null`), not `0%` or an estimate based on
`CreatedAtUtc`. The last-activity date and return flag do not extend retention. Each row
expires at midnight UTC at the start of cohort age day 90 and is removed by the daily cleanup
job, independently of report access. If collection is disabled, each process purges its
remaining contributions at startup and after its option changes, waiting for in-flight local
writers before purging; re-enabling starts fresh observation cohorts.

The collection gate is a process-local singleton. In a multi-instance deployment, it serializes
writers and cleanup only within each process; it is not a distributed lock or a configuration
propagation mechanism. A fleet-wide disable requires the off setting to reach every instance
and each instance's cleanup to complete. An instance still running with stale enabled
configuration could write until it observes the change, so the application does not claim an
atomic, fleet-wide purge at the instant one instance's flag changes.

## Other report definitions

- **Live accounts:** every existing, not-deleted Identity account row, including disabled
  accounts and accounts awaiting email confirmation.
- **People:** all stored owned people, including archived profiles. The average is this total
  divided by the count of all existing account rows. The total describes stored profiles and
  is not reduced for disabled accounts.
- **Interactions this month:** all stored interactions whose `OccurredOn` is between the
  first day of the current UTC calendar month (inclusive) and the first day of the next UTC
  month (exclusive). `OccurredOn` is a `DateOnly`; it is compared directly and never
  converted to an instant.
- **Saved reminders:** all stored reminders, including completed reminders, reminders for
  archived people, and reminders owned by disabled accounts.
- **Accounts with reminders:** the number of existing accounts with at least one stored
  reminder, divided by the count of all existing account rows. Completed reminders, reminders
  for archived people, and reminders owned by disabled accounts count as feature use.

An average or account proportion has no denominator when there are no existing account rows
and is reported as unavailable, not a fabricated zero. A count is zero when its query finds no rows.
No completed retention cohorts is separately reported as no retention data. An active
Administrator is itself an existing account, but the no-denominator behavior remains defined
for the report model.

## Operational checks

Keep the metrics options default disabled. Any activity hook must check the option before
invoking the collector, resolve authentication through the circuit's current-user abstraction,
and await the collector without fire-and-forget work. The recorder authenticates first, then
uses the singleton collection gate and rechecks the option before any database collection
query. The recorder and retention runner share that gate across their contexts; both continue
to use the per-context database lane. Admin report changes must remain SQL-translatable
aggregate projections and must not materialize narrative entities. The retention runner may
load only expired `ProductActivity` rows (or every `ProductActivity` row when disabled),
delete in bounded tracked batches, and leave all other data untouched.

### Classification for the cross-user isolation registry

These three Application interfaces have different security surfaces and should not be
misreported as interchangeable owner-scoped CRUD services:

| Interface | Registry classification | Evidence |
| --- | --- | --- |
| `IProductActivityService` | Authenticated current-user writer; no caller-supplied owner or payload | InMemory tests verify no database work while disabled, missing/disabled-account refusal, and attribution only to `ICurrentUser`; SQL Server test covers the owner-unique create race. |
| `IProductMetricsReportService` | Active-Administrator-only, instance-wide aggregate read; intentionally not owner-isolated | InMemory tests verify fresh database role/disabled checks, disabled static status, and aggregate denominators; SQL Server tests verify non-Administrator denial and that translated aggregate queries do not select narrative columns. |
| `IProductMetricsRetentionRunner` | Trusted background-only cross-owner cleanup; no user input or row-returning surface | InMemory tests verify bounded batches, disabled purge, and unrelated-data integrity; SQL Server test verifies the day-90 expiry query and keeps a day-89 row. |

The report is global because its purpose is instance-level measurement, but its authorization
does not grant an Administrator access to any person's or account's individual record. The
retention runner is intentionally cross-owner because it removes expired aggregate
contributions; it is not a user-facing service and must never be injected into a page or
HTTP endpoint.
