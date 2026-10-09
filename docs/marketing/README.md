# Public marketing content

Pages use static SSR in `Relio.Web`, with the shared marketing layout and `SeoHeadContent`.
They never query relationship services. The workspace remains protected at `/dashboard`.
Legal wording is operator-supplied and review-gated; see
[policy hosting](../security/policy-hosting.md).

## Feature claim checklist

Verified against the implementation and issue states when writing the pages. Recheck the
corresponding behavior before changing a claim; an open roadmap is not delivered functionality.

| Source epic | Status | Public claim and implementation evidence |
|---|---|---|
| #21 | Shipped | Profiles, imports, duplicates, merge, archive and delete: `Components/Pages/People.razor`, `PersonProfile.razor`, `ImportPeople.razor`, `MergePeople.razor`; `Relio.Data/People/` |
| #30 | Shipped | Shared interactions, notes and paged timeline: `Components/Interactions/`, `Components/Notes/`, `Relio.Data/Interactions/`, `Relio.Data/Timeline/` |
| #36 | Shipped | Follow-ups, birthday reminders and cadence: `Components/Reminders/`, `Relio.Data/Reminders/`; email needs configured SMTP and notification preferences |
| #42 | Planned | Difficult moments/reflection are explicitly unavailable; the current page is an empty-state placeholder, not a recording workflow |
| #46 | Shipped | Dashboard, optional onboarding and quick log: `Home.razor`, `Onboarding.razor`, `QuickLogInteraction.razor`, `Relio.Data/Dashboard/` |
| #51 | Planned | Name search and tag/type filtering are explicitly planned; existing list sorting/paging/archive visibility are shipped |
| #54 | Shipped | Account-scoped storage, partial field encryption, export/restore and account erasure: `docs/security/`; policies require operator configuration and human review |

## Local visuals

The relationship thread is a deliberately fictional illustration, not a screenshot of an account
or a promise of an exact screen layout. Morgan Silva and the garden/book examples are synthetic.
The source capture is in `screenshots/relationship-thread.png`; optimized assets are served from
`Relio.Web/wwwroot/img/`. Their alt text and captions identify them as fictional.

To regenerate the social preview and thread image, install Pillow, fonttools and brotli in a
development-only Python environment and run `python docs/marketing/generate-preview.py`.
The generator reads the design tokens and the app's self-hosted font files; nothing is downloaded
by the published site. The social preview is 1200 × 630; the thread illustration is 960 × 680.

## Hosting and pricing

`/pricing` reads the plans from `PlanCatalog` - the same values the plan page shows and
`PlanLimits` enforces - and shows them only when the instance has a billing provider
(`Billing:Provider=Stripe`). With the documented default `Billing:Provider=None` it says the
instance has no paid plans. No other configuration (a plan name, a price, a limit) is read by the
page, so prospective settings can never publish an offer, and the page emits no `offers` structured
data. The claims it makes and their evidence:

| Claim | Evidence |
|---|---|
| Free for up to 25 active people; archived people don't count | `PlanCatalog.FreeActivePeopleLimit`, `PlanLimits`, `PlanLimitsTests` |
| Relio Pro is $2 / month or $12 / year ($1 / month) | `PlanCatalog`, the two configured Stripe prices ([billing.md](../security/billing.md)) |
| Nothing is deleted when a subscription ends | `SubscriptionStateRules` only changes the tier; no service deletes on a downgrade |
| Change or cancel under Settings > Plan and billing > Manage billing | `/Account/Manage/Plan` and the Stripe customer portal |
| Relio sends Stripe only the account email and an opaque account id | `StripeBillingProvider`, [billing.md](../security/billing.md) |

The "Subscribe to Relio Pro" call to action links to `/Account/Manage/Plan`; a visitor who is not
signed in is sent through sign-in and back. Self-hosting links point to the existing setup and
protected-key documentation, as well as the [self-hosting guide](../self-hosting.md).

## Changelog publication

Root `CHANGELOG.md` is embedded as `Relio.ReleaseNotes.md` by `Relio.Web.csproj`.
Rebuild and redeploy to publish an update; editing a file beside a running published app
does not change the page. No GitHub requests or repository files are needed at runtime.
Keep delivered-but-not-released changes under `Unreleased` and use actual version/date headings
when a release is made. Do not add personal data, credentials or internal operational details.

The restricted Markdig renderer disables raw HTML. Links accept HTTP(S), local paths or anchors
without credentials, control characters or backslashes; relative repository-file links should
be written as explicit HTTPS links. Images must be PNG/WebP under `/img/`, without traversal,
encoded paths, query strings or fragments. Unsafe links/images become readable text with an
omission notice. The page owns its h1; repository release headings start at h2. A missing resource
logs a warning and renders a calm message instead of exposing a stack trace.
