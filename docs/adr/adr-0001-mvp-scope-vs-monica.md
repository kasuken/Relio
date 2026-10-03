---
title: "ADR-0001: MVP scope versus Monica's feature set"
status: "Accepted"
date: "2026-10-03"
authors: "kasuken (Emanuele Bartolesi), Claude (discovery assistant)"
tags: ["architecture", "decision", "scope"]
supersedes: ""
superseded_by: ""
---

# ADR-0001: MVP scope versus Monica's feature set

## Status

**Accepted**

## Context

Relio is inspired by Monica (monicahq/monica), an open-source "personal CRM".
Issue #8 asks us to compare Monica's feature set against the Relio MVP backlog
(epics #7, #14, #21, #30, #36, #42, #46, #51, #54, #63) before further build-out,
so the backlog stays focused on a private relationship memory tool rather than
drifting toward a CRM, a social network, a team tool or an AI advisor.

Monica's feature set (contacts, relationships, activities, notes, reminders,
life events, journal, gifts, debts, tasks, calls, documents/photos, pets,
addresses, contact fields, labels/tags, groups, important dates, "how you met",
food preferences, vaults/sharing, and a public API) was reviewed field-by-field
against Relio's PRD and current epics.

## Decision

Classify every Monica feature area as **MVP** (mapped to an existing issue),
**Later** (valuable but deferred past MVP), or **Non-goal** (explicitly out of
scope because Relio is not a CRM, a social network, a team tool or an AI
product). The classification below is the scope contract for the current
backlog; epics are not changed by this ADR except where a genuine MVP gap is
called out as a follow-up.

### MVP — covered by existing issues

- **MVP-001**: Contacts / people profiles — #22 (create), #23 (list), #24
  (edit), #26 (archive/restore/delete), #27/#28 (duplicate detection and
  merge), #29 (vCard/CSV import). Epic #21.
- **MVP-002**: Relationship types and tags/labels — #25 (manage relationship
  types and tags), #24 (assign on edit). Epic #21.
- **MVP-003**: Contact fields (phone, email, other contact methods) — #24.
  Epic #21.
- **MVP-004**: Activities / interactions — #31 (log), #35 (one interaction
  with several people), #34 (last contacted). Epic #30.
- **MVP-005**: Notes — #32. Epic #30.
- **MVP-006**: Person timeline (Monica's "contact journal" view) — #33.
  Epic #30.
- **MVP-007**: Reminders (generic + birthdays) — #37 (reconnect), #38
  (birthdays), #39 (scheduler), #40 (email delivery/preferences). Epic #36.
- **MVP-008**: Stay-in-touch frequency ("stay in touch" in Monica) — #41.
  Epic #36.
- **MVP-009**: Important dates — birthdays are explicitly covered (#38); other
  custom important dates are folded into reminders scope (#37) rather than a
  separate Monica-style "special dates" feature.
- **MVP-010**: Journal / difficult moments (Relio's reframing of Monica's
  broader journal, narrowed to emotionally significant moments) — #43, #44,
  #45. Epic #42.
- **MVP-011**: Search and filtering by name, tags and relationship type — #52,
  #53. Epic #51.
- **MVP-012**: Dashboard and quick capture (Monica's dashboard/"quick add") —
  #47, #49. Epic #46.
- **MVP-013**: Data export and account/data deletion (Monica's data export and
  account closure) — #58, #59. Epic #54.
- **MVP-014**: Self-hosting (Monica ships as self-hosted first) — #64, #65.
  Epic #63.

### Later — valuable, deferred past MVP

- **LAT-001**: Gifts (ideas/tracking, given/received, budget) — no Relio epic
  covers this; it is a well-scoped Monica feature but not required for the
  core "remember people" loop. Candidate for a post-MVP epic.
- **LAT-002**: Debts between the user and a contact — niche, deferred.
- **LAT-003**: Tasks/to-dos tied to a person, beyond reminders — deferred;
  reminders (#36 epic) already cover the "don't forget" need.
- **LAT-004**: Calls log as a distinct activity type — folded conceptually
  into interactions (#31) today; a dedicated call-log UI is deferred.
- **LAT-005**: Documents and photo attachments on a person or interaction —
  deferred; meaningful storage/privacy work (encryption at rest, quotas) makes
  this a larger follow-up, not an MVP item.
- **LAT-006**: Pets — deferred, low value relative to effort for v1.
- **LAT-007**: Addresses as structured, geocoded records — deferred; a free
  text contact field is sufficient for MVP.
- **LAT-008**: Life events / timeline milestones beyond interactions and
  difficult moments (e.g. job changes, moves) — deferred; covered loosely by
  freeform notes until there's demand for structure.
- **LAT-009**: "How you met" and food/gift preference fields — deferred;
  low-cost additions that can ride on the existing person-profile edit screen
  (#24) later without a new epic.
- **LAT-010**: Public/third-party API — deferred past MVP; no external
  integrations are in scope yet.
- **LAT-011**: Groups of contacts (distinct from relationship types/tags) —
  deferred; tags (#25) cover the near-term need to group people.

### Non-goal — explicitly out of scope

- **NON-001**: Vaults / multi-user sharing of contacts — Relio has no shared
  or team data in the MVP (AGENTS.md, guardrails on epics #21, #30, #36, #42,
  #51, #54); every query and mutation is scoped to a single signed-in user.
- **NON-002**: CRM fields — companies, deals, pipelines, sales stages. Epic
  #21's guardrail states Relio is not a CRM.
- **NON-003**: Social/activity feeds, gamification, streaks — explicitly ruled
  out by epic #46's guardrail ("no gamification, streaks or social feeds").
- **NON-004**: AI-generated advice, sentiment analysis or suggested actions on
  relationships — ruled out by epic #42's guardrail ("no AI advice or
  analysis") and the repository-wide "No AI features in the MVP" rule.
- **NON-005**: Social login / OAuth identity providers — epic #14 restricts
  MVP auth to ASP.NET Core Identity with local accounts only.
- **NON-006**: Monica's contact "species"/organization-as-contact modeling —
  not applicable; Relio models people only, consistent with "not a CRM."

## Consequences

### Positive

- **POS-001**: The backlog now has an explicit, traceable mapping from every
  Monica feature area to an MVP issue, a deferred item, or a stated non-goal,
  reducing scope debate during implementation of epics #21, #30, #36 and #42.
- **POS-002**: Reviewers and contributors can cite this ADR when a PR proposes
  a Monica-inspired feature (e.g. gifts, pets, vaults) to explain why it is
  deferred or rejected, without re-litigating product scope each time.
- **POS-003**: The "Later" list doubles as a pre-groomed backlog for the first
  post-MVP planning pass.

### Negative

- **NEG-001**: Some "Later" items (gifts, documents/photos, life events) are
  common enough in relationship-memory use cases that deferring them risks
  user disappointment if a post-MVP epic is not scheduled promptly.
- **NEG-002**: This ADR is a snapshot of Monica's feature set as of this
  review; Monica continues to evolve, so future re-reviews may find new gaps.
- **NEG-003**: No new GitHub issues were created as part of this ADR itself;
  the single proposed follow-up below must be turned into a tracked issue
  separately before work can start on it.

## Alternatives Considered

### Match Monica feature-for-feature

- **ALT-001**: **Description**: Build full parity with Monica, including
  gifts, debts, tasks, pets, documents, vaults and a public API, inside the
  current MVP phase.
- **ALT-002**: **Rejection Reason**: Contradicts the product brief ("not a
  CRM, a social network or an AI advisor") and would significantly delay
  shipping the core people/interactions/reminders loop that the PRD treats as
  phase 1 and phase 2 priorities.

### No formal scope review (keep relying on ad hoc PRD interpretation)

- **ALT-003**: **Description**: Skip a dedicated comparison and let each epic
  owner decide feature-by-feature whether a Monica feature belongs in scope.
- **ALT-004**: **Rejection Reason**: Produces inconsistent decisions across
  epics and repeated scope debates; a single recorded comparison is cheaper
  and matches the acceptance criteria on issue #8.

## Implementation Notes

- **IMP-001**: No epics are modified by this ADR. The "MVP" classifications
  above confirm existing issues already cover the relevant Monica feature;
  no new MVP issues are required except the gap noted below.
- **IMP-002**: One MVP gap was identified: Monica treats "important dates"
  other than birthdays (anniversaries, custom recurring dates) as first-class
  reminders, while Relio's current issues (#37, #38) only name reconnect
  reminders and birthdays explicitly. This is noted as a proposed follow-up
  issue rather than created directly, per the task constraints; see the pull
  request description for the suggested title, body and target epic (#36).
- **IMP-003**: Success is this ADR being linked from future PRs that touch
  epics #21, #30, #36, #42, #46 or #51 whenever a Monica-inspired feature
  request comes up, instead of re-deriving the scope decision each time.

## References

- **REF-001**: Issue #8 — Review Monica's feature set against the Relio MVP
  scope.
- **REF-002**: Epics #7, #14, #21, #30, #36, #42, #46, #51, #54, #63 and their
  child issues (see repository issue tracker).
- **REF-003**: Monica (monicahq/monica), an open-source personal CRM —
  https://github.com/monicahq/monica.
- **REF-004**: `AGENTS.md` — product and architecture guardrails (not a CRM,
  not a social network, no AI, no shared/team data in the MVP).
