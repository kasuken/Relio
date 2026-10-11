# Account erasure

Account deletion requires the signed-in account's current password and an explicit confirmation.
The data service rechecks the account and administrator state, then removes the account graph in
one save. It selects keys and concurrency metadata rather than loading protected notes, narratives,
Identity-token values, or profile verifiers.

Every user-owned row has a non-cascading foreign key to its Identity account. This is the database
backstop for a circuit that attempts to write after deletion: the late write or account deletion
fails as one transaction instead of leaving a partial erasure or an orphan row. Existing rows whose
owner id has no Identity account must be resolved before the owner foreign keys are applied; the
schema change intentionally does not adopt or delete orphaned rows.

The instance-wide, SQL session-owned `Relio.AccountLifecycle` lock is acquired before fresh
lifecycle reads and held through the single transactional save. It serializes erasure and
administrator disable operations across application processes, independently of per-owner restore
locks. The last active administrator cannot be deleted or disabled.

Active circuits on the same process receive an account-erasure revocation notification. Every
instance checks account existence and disabled status before each inbound circuit activity and
rejects a missing or disabled account's cookie on its next HTTP request. A completely idle circuit
on another instance may continue displaying its already-rendered page until it next becomes active;
the owner foreign key still rejects any write after erasure. Operators should account for that
idle-display limitation when assessing their deployment's session requirements.

With hosted billing on, erasure first cancels every live Relio subscription of the account's
Stripe customer, before any row is removed: the subscription row holds the only link to that
customer, and deleting it first would leave Stripe charging an account that no longer exists. If the
cancellation fails, nothing is deleted and the user is asked to try again. The subscription row is
then deleted in the same save as the account (its foreign key to the account is `NO ACTION`, like
every owner reference). With billing off there is no provider to call, and the operator cancels any
leftover subscription in the Stripe Dashboard. See [billing.md](billing.md).

The confirmation email is sent only after the database commit. It contains no names or Relio
content and says that backups are handled under the instance operator's retention policy. Deleting
the live account does not delete backups; backup retention and disposal are operated separately.
