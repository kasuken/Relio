# Data export and restore

Relio's signed-in **Settings → Your data** page offers a versioned JSON export,
a people-only vCard export, and JSON restore to a registration-only account.
JSON is the full-fidelity format. It includes active and archived people, profile
settings, relationship types, tags, ordered contact methods, interactions and
their participant links, pinned notes, and reminder configuration and delivery
history. The export preserves source record identifiers for references within
the file and preserves valid record audit timestamps, including archived people
whose archive timestamp is absent in legacy data; it does not include an owner
identifier.

Treat downloaded files as private, decrypted data. Protect them in transit and
at rest, share them only with the intended recipient, and delete temporary
copies securely when they are no longer needed. Relio does not store an export
file on the server.

## Restore boundaries

Restore accepts an export only when the destination account has no people,
tags, interactions, notes, reminders, or non-default relationship types.
Registration-created profile and default relationship-type rows are allowed.
The import is validated as a whole and saved in one database operation; an
invalid export or an unsuccessful save does not leave a partial restore. Source
identifiers are remapped, and restored records belong to the destination account.

SQL Server restores force one implicit transaction and take an exclusive transaction-owned
application lock for the exact destination owner (`Relio.UserDataRestore.v1.` plus uppercase
SHA-256 of its UTF-8 id). Freshness is rechecked before any write in that transaction: competing
restores cannot both commit. Unrelated owners and account-lifecycle locks do not share this resource.
Ordinary writes still use required account-owner foreign keys to reject an erased destination.

The destination's sign-in email, password, roles, sessions, and other
authentication data are never changed or copied. Reminder email delivery is
reset to **None**, and the destination receives fresh local unsubscribe
credentials. Other portable profile and calendar settings and the reminder
history are restored. Email delivery must be configured again by the account
holder.

The optional product-activity contribution is present in a JSON export for
access and erasure portability, but is not restored. It is instance-local
analytics and must not alter the destination instance's cohort measurements.
Collection remains governed by the destination's own configuration.

## vCard boundaries

vCard is for moving contact details, not backing up a Relio account. It contains
people's names, birthdays, and supported contact methods, including archived
people. It does not include notes, interactions, reminders, relationship types,
tags, profile settings, or analytics. In particular, narrative content is never
silently added to a person's vCard.

The current product has no difficult-moment entity; this export does not claim
to include difficult moments.
