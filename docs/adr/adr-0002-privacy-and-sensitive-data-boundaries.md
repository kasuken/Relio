---
title: "ADR-0002: Privacy and sensitive-data boundaries"
status: "Accepted"
date: "2026-10-07"
authors: "Relio maintainers"
tags: ["architecture", "privacy", "security", "data-ownership"]
supersedes: ""
superseded_by: ""
---

# ADR-0002: Privacy and sensitive-data boundaries

## Status

**Accepted**

This accepts an engineering and product boundary, not a legal conclusion. It is not legal advice,
a GDPR certification, or evidence of legal approval. Review of hosted processing roles, retention,
sub-processors, and final policy wording remains outstanding.

## Context

Relio is a private relationship memory, not a CRM or shared workspace. A user records personal
information about people who may never have a Relio account, as well as private reflections. The
stakeholders are Relio maintainers, hosted and self-hosted operators, users, and the third parties
represented in users' journals. Household use does not by itself settle the legal role or exemptions
of a hosted operator.

Relio is a .NET 10 Blazor Web App using ASP.NET Core Identity, EF Core, and SQL Server. Owned data
is isolated through `ICurrentUser.RequireUserId()`, explicit owner predicates, and foreign-ID
validation; administrator roles do not bypass those boundaries. The scoped `RelioDbContext` is
serialized by the database lane, data services use untracked reads, and mutations save once and
clear tracking in `finally`. These boundaries remain mandatory when encryption, export, or erasure
is implemented.

The current persisted narrative fields are `Person.HowWeMet`, `Person.Details`, `Note.Text`,
`Interaction.Description`, and `Reminder.Title`. (`Note` has `Text`, not `Content`.) Identity's
authenticator key and recovery-code value are currently stored in `AspNetUserTokens`; the current
application does not configure a persisted, shared Data Protection key ring. `RegistrationInvitation`
stores a hash of its invitation token, while `UserProfile.UnsubscribeToken` is a persisted bearer
token. There is no `DifficultMoment` entity or persisted difficult-moment narrative yet: issue #43 is
open.

## Decision

Use **application-level authenticated encryption with ASP.NET Core Data Protection** for private
narrative fields and persisted bearer secrets. Give each data category/field a stable,
purpose-separated protector. Persist the key ring durably outside both the repository and the
Relio database, and protect it at rest with a supported operating-system or certificate mechanism.
All instances that must read the same data must use the same correct key ring and stable application
identity. Provisioning, access, backup, recovery, and rotation are operator-controlled operations.

A missing, inaccessible, corrupt, or wrong key ring must fail clearly. Do not fall back to
plaintext, empty values, a new ephemeral key ring, or best-effort decryption. Existing plaintext
records require an explicit, tested backfill before encrypted-field support is considered complete.

### Data classification and at-rest boundary

| Classification | Current data | Boundary |
| --- | --- | --- |
| Account identifiers | Identity user name/email and normalized lookup values; `RelioUser.PendingEmail`; `RegistrationInvitation.Email`, `NormalizedEmail`, and `CreatedByUserId` | Identifying account/administration data. Keep readable where Identity lookup, uniqueness, or invitation administration requires it; do not include it in unrelated users' views or logs. Invitation recipient data is removed with its invitation. |
| Authentication and bearer secrets | Identity password hash and security state; authenticator/recovery values in `AspNetUserTokens`; `UserProfile.UnsubscribeToken`; invitation link token | Passwords remain Identity-managed one-way hashes, never reversible application-encrypted passwords. Protect the authenticator and recovery values at rest after durable keys are available; protect the persisted unsubscribe token at rest (or replace it with a safe non-reversible verifier). Invitation tokens remain hash-only in storage. Never export or log secrets, links, codes, or token-bearing query strings. |
| Person/profile and relationship identifiers | Person names, nickname and birthday components; contact-method kind, label, value and `NormalizedValue`; tag and relationship-type names; `UserProfile.DisplayName`, `TimeZoneId` and preferences | Private, potentially identifying data about users and third parties. Apply owner scoping and minimal projections. These fields are outside #57's minimum field-encryption set and remain readable in SQL, protected by SQL/volume encryption and TLS as defense in depth. Fields needed for current identity, lookup, uniqueness, ordering, scheduling, or duplicate matching—including indexed names and contact comparison keys—must remain readable database metadata. Do not describe these fields as field-encrypted or anonymous. |
| Private narrative | `Person.HowWeMet`, `Person.Details`, `Note.Text`, `Interaction.Description`, and `Reminder.Title` | Field-encrypt at rest with the purpose-separated Data Protection scheme. Decrypt only in an owner-scoped application path. Do not add a searchable plaintext mirror, log the value, or put it in a URL. |
| Ownership, dates, audit and usage metadata | `OwnerId`, entity/participant links, user-calendar dates, audit timestamps, statuses and aggregate counts | Operational and relational metadata remains readable where needed for filtering, joins, ordering, scheduling, and audit. It can still identify or be linked to a user; it is not anonymous and remains access-controlled and minimized. |
| Future difficult-moment narratives | Issue #43 proposes date, description, trigger and resolution; lessons learned may be added later | No such entity or field exists today. Before adding one, classify and field-encrypt every narrative field, and add owner-scope, export, erasure, retention, and person-child lifecycle coverage. |

Field encryption is deliberately not encryption of the whole database. A database dump can still
reveal account identifiers; person names, birthdays, contact values and labels; indexed contact
comparison keys; owner/relationship structure; dates; and other readable metadata. The accepted
minimum for #57 is every current private narrative field above and at-rest protection for the
authenticator/recovery values once the durable key ring is available; the persisted unsubscribe
bearer token is also in scope for the secret-at-rest boundary. SQL Server or volume encryption and
TLS are additional layers, not substitutes for these application-level protections.

### Communications and observability

Reminder emails may contain only the already-established person name, reminder title, and due date;
never include notes, interaction descriptions, difficult moments, or other private narratives.
Authentication emails carry only the credential necessary for their specific flow. Never log an
email body, address, confirmation/reset/change link, unsubscribe link, or token. Application and
platform/EF logs may contain internal IDs, event metadata, counts, error codes, and exception types
only—not names, email addresses, private content, authentication material, invitation/recovery/2FA
secrets, query tokens, or request/response bodies.

Optional product metrics are disabled by default. Disabled means zero new collection, including no
authentication/activity writes for metrics. When explicitly enabled, reports expose aggregate counts
only and never expose per-user records. A minimal owner-attributable activity/cohort contribution
may be stored solely to calculate retention. It is pseudonymous and access-controlled, not anonymous;
store no names, notes, descriptions, other content, or IP/device/session identifiers. Include that
contribution in the user's export and erasure. Assign it a defined TTL of no more than 90 days and
expire it automatically; disabling collection stops new writes and immediately purges existing
contributions. Run the daily purge independently of report requests. Document the exact 30-day cohort
definition separately from the TTL. The PRD's 30-day-returning-user measure is a measurement window,
not a data-retention period.

### Retention, portability, and erasure

Relationship memory remains at the user's direction until the user removes it, deletes its person,
or closes the account. Do not add silent inactive-account expiry or an automatic diary-retention
period. Operational logs need an explicit bounded retention definition. Metrics contributions use a defined TTL of no more than 90 days, are purged daily whether or not a report is requested, and are purged immediately when collection is disabled.

Export is an authenticated, versioned JSON document of the current user's complete present owned
data, portable product settings, and any owner-attributable metrics activity/cohort contribution
stored while metrics are enabled, preserving graph references; people can also be exported as vCard.
It excludes password hashes, security stamps, authenticator/recovery secrets, session state,
unsubscribe/invitation tokens, and data belonging to other users. JSON import targets a fresh
destination account, remaps owner and entity IDs, validates and encrypts locally, and applies as one
all-or-nothing operation. It never restores authentication credentials or silently enables mail.
The downloaded export is decrypted sensitive data; the user is responsible for protecting it after
download.

Account deletion requires re-authentication, an explicit confirmation, and an export offer, then
hard-deletes all of that user's owned data and Identity dependents, revokes active sessions, removes
that user's attributable metrics/activity and administrative invitation records, and sends only a
minimal confirmation if email is configured. It includes `ContactMethod` rows and interaction
participant links. It does not retain a shadow profile, remove unrelated users, or delete the global
Administrator role. It must not remove the last active administrator or create an administrator
takeover path; require a safe handoff to an already-authorized account where applicable.

Deletion from the live application is distinct from expiry of historical backups. Operators define
backup schedule, retention, restore, and key-ring recovery in #65. Do not promise that every backup
copy disappears immediately after account deletion. Key-ring backups are separate, protected
artifacts; losing all keys that protect stored values makes those values irrecoverable.

### Public policies and legal review

Hosted privacy/terms/acceptable-use pages stay disabled by default and may be enabled only with
operator-supplied, reviewed text that accurately describes verified behavior. Do not publish
unreviewed legal assertions or imply that unfinished #57–#62 features are already delivered. The
configurable source-code link remains visible on every instance, independently of hosted policy
pages. Coordinate only the minimal static-layout and metadata support needed from #86/#87, with
availability checks in #92; this decision does not deliver the marketing epic or move the protected
`/` route.

Qualified legal review must determine the hosted service's processing/controller and
sub-processor roles, exact hosted retention, and policy claims. This ADR makes no claim that the
household context automatically exempts a hosted operator and does not certify GDPR compliance.

## Consequences

### Positive

- **POS-001**: Private narratives and persisted authentication secrets receive protection beyond SQL Server/volume encryption, while owner-scoped access remains the application authorization boundary.
- **POS-002**: A durable external key ring supports restarts and correctly configured shared deployments without placing encryption keys in source control or the Relio database.
- **POS-003**: Explicit data classification makes the remaining readable identifiers and operational metadata visible instead of implying that field encryption hides an entire database.
- **POS-004**: Export, deletion, metrics, mail, future difficult-moment fields, and operator backups share one documented lifecycle boundary.
- **POS-005**: Self-hosted and hosted deployments have the same product privacy defaults; hosted-only policies and metrics do not become implicit requirements for self-hosted instances.

### Negative

- **NEG-001**: Durable external key storage, cross-instance configuration, backup, recovery, and rotation add operator responsibilities; loss of all usable keys can make encrypted narratives and two-factor secrets unreadable.
- **NEG-002**: Encrypted narratives cannot be queried or searched as plaintext. Querying them would require a separately reviewed design; a plaintext mirror is not an acceptable workaround.
- **NEG-003**: Indexed names, contact comparison keys, account identifiers, dates, ownership links, and other necessary metadata remain readable. A database dump can still expose identifying or linkable information.
- **NEG-004**: Backfill, restart/rotation compatibility, export/import validation, and Identity token-store behavior require SQL Server integration coverage; InMemory tests alone cannot prove stored ciphertext or key persistence.
- **NEG-005**: Deletion from the live database does not instantly remove retained backup copies or records held under third-party mail-provider retention; the exact hosted position needs operator documentation and legal review.
- **NEG-006**: The exported JSON contains decrypted relationship data and can be exposed after it leaves Relio's control.

## Alternatives Considered

### SQL Server or volume encryption only

- **ALT-001**: **Description**: Rely on database TDE, encrypted disks/backups, TLS, and existing application authorization, with sensitive fields stored as ordinary plaintext values.
- **ALT-002**: **Rejection Reason**: These controls protect transport and storage media but do not meet the accepted application-level boundary: a privileged SQL reader or plaintext database dump can still read narrative columns and token values.

### Custom per-record envelope encryption backed by a KMS

- **ALT-003**: **Description**: Build a separate DEK/KEK envelope-encryption system, with per-record or per-table keys managed by a KMS and custom EF/Identity integration.
- **ALT-004**: **Rejection Reason**: It introduces a second custom cryptographic and key-lifecycle subsystem, and a mandatory KMS dependency that does not fit both hosted and self-hosted operation. ASP.NET Core Data Protection supplies authenticated protection and purpose isolation within the existing stack; its durable key storage still remains an explicit operator requirement.

### No application-level encryption

- **ALT-005**: **Description**: Keep private narratives and two-factor secrets readable in SQL Server and rely on explicit owner predicates, restricted database access, and operator policy.
- **ALT-006**: **Rejection Reason**: It leaves the current plaintext database exposure in place and does not satisfy the accepted decision to protect private narratives and persisted two-factor secrets beyond database access controls.

## Implementation Notes

- **IMP-001**: **#56 — Isolation**: Prove every Application data service against SQL Server: another user cannot read, list, mutate, delete, or link owned entities. Cover every supplied foreign ID and preserve the rule that administrator status never bypasses ownership.
- **IMP-002**: **#57 — Encryption**: Configure durable external Data Protection keys and a stable shared application identity; use stable purpose-separated protectors; backfill existing narrative rows; protect Identity authenticator/recovery values and the stored unsubscribe bearer token. Fail closed on missing/corrupt keys. Test raw SQL values are unreadable and service reads round-trip after restart/key rotation. Preserve Identity sign-in and `TwoFactorStatusService` recovery-code counts without reading/logging plaintext token values.
- **IMP-003**: **#58 — Portability**: Implement the versioned JSON graph and people vCard export, plus fresh-account JSON import with remapped IDs, destination ownership, local encryption and validation, and one all-or-nothing save. Add a model/service coverage check so each current owned field and any stored owner-attributable metrics contribution is exported; add difficult moments once #43 exists. Exclude Identity credentials, tokens, and other owners' data.
- **IMP-004**: **#59 — Erasure**: Require re-authentication, offer export, confirm explicitly, and hard-delete every owned table, including contact methods and participant links, plus Identity dependents and this user's attributable metrics/activity and invitations. Revoke sessions and send a minimal configured-email confirmation. Prove no owner rows remain; protect the last active administrator and do not delete global roles or unrelated users. Document that operator-managed backups expire separately.
- **IMP-005**: **#60 — Security hardening**: Enforce the logging boundary in application and built-in hosting/EF categories: no sensitive EF parameter values, request/response bodies, personal values, or query-string bearer tokens. Log only IDs, event metadata, counts, error codes, or exception types. Add regression coverage for content and token-bearing request paths; the separate security/middleware implementation owns its files.
- **IMP-006**: **#61 — Policies and source link**: Keep policy routes off by default; require operator-supplied text and legal review before enabling. State only verified behavior and keep the source link visible on every instance whether policy pages are enabled or not. Reuse only needed #86 static-layout and #87 metadata support, test variants in #92, and do not move `/` as an incidental policy-page change.
- **IMP-007**: **#62 — Metrics**: Disabled means zero new collection, including no authentication/activity writes for metrics. When explicitly enabled, expose aggregate reports only; if needed to calculate retention, store only a minimal owner-attributable activity/cohort contribution, with no names, notes, descriptions, other content, or IP/device/session identifiers. Keep it access-controlled and explicitly pseudonymous, not anonymous; include it in that user's export and erasure. Assign a defined TTL of no more than 90 days, run automatic daily expiry even when no report is requested, and immediately purge existing contributions when collection is disabled. Document the exact 30-day cohort definition separately from the TTL.
- **IMP-008**: **#65 — Operator and backup guidance**: Document key-ring location/configuration, protection, shared-instance identity, permissions, rotation, separate protected key backups, recovery, and data-loss consequences. Define database backup schedule, retention/expiry, restore, and post-erasure behavior; never claim live deletion instantly purges retained copies.
- **IMP-009**: **#43 — Future difficult moments**: Before creating any model or storage field, apply the narrative encryption, no-log/no-email/no-metrics boundary and owner checks. Include description, trigger, resolution and any later lessons field in export/erasure; add the person-child delete/merge checklist and integration coverage. Do not imply a difficult-moment model is delivered by this ADR.
- **IMP-010**: **#61 legal publication gate**: Obtain qualified review of hosted processing roles, retention, sub-processors and final text before publishing policies. Do not infer an exemption or claim certification from this engineering record.

## References

- **REF-001**: Epic #54, *Privacy, trust and data ownership*, and child #55, *Define privacy and sensitive-data boundaries*.
- **REF-002**: Issues #56–#62 for isolation, encryption, portability, erasure, security hardening, policy pages, and metrics.
- **REF-003**: Issue #65, *Self-hosting documentation*, for deployment, Data Protection key configuration, backups, restore, and upgrades.
- **REF-004**: Issue #43, *Record a difficult moment*; the feature remains open and no entity exists yet.
- **REF-005**: `AGENTS.md` — user-scoped data pattern, accounts/authentication, interactions/notes, reminders, and lifecycle checklists.
- **REF-006**: `docs/adr/adr-0001-mvp-scope-vs-monica.md` — accepted product scope and MVP boundary.
- **REF-007**: Microsoft ASP.NET Core Data Protection documentation — https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview.
- **REF-008**: Issues #86, #87 and #92 — minimal marketing layout, SEO metadata, and public-surface verification dependencies for #61; this ADR does not implement them.
