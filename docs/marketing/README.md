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
