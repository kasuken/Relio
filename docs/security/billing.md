# Hosted billing (Stripe)

Billing is a hosted-only feature and is **off by default** (`Billing:Provider=None`). With it off
there are no plans, no limits and no payment UI, the webhook endpoint refuses every delivery, and
Relio makes no request to any payment provider. Every self-hosted instance runs like this unless its
operator configures Stripe. The implementation follows the one used by LearnStack: hosted Stripe
Checkout to subscribe, the hosted Stripe customer portal for self-service changes, and
signature-verified webhooks that drive the plan.

## Plans

| Plan | Price | What it allows |
|---|---|---|
| Free | $0 | Up to 25 **active** people. Archived people don't count. |
| Relio Pro | $2 / month, or $12 / year ($1 / month) | Unlimited people. |

The values live in `Relio.Application/Billing/PlanCatalog.cs`; the pricing page, the plan page and
the limit all read them from there. The amount actually charged is the Stripe price you configure,
so keep the two in step.

The limit is enforced inside the data services, never only in the UI: creating a person, restoring
an archived one, importing a file, merging two archived profiles into an active one, and restoring a
JSON export into a fresh account all check it (`Relio.Data.Billing.PlanLimits`) and save nothing when
it is exceeded. Nothing is ever deleted or hidden when an account goes back to Free: an account above
the limit keeps everyone and simply can't add or restore more until it archives someone or subscribes.
It is a soft limit; two saves racing from two tabs can both pass.

## What Stripe receives

- The Relio user id (an opaque GUID) as `client_reference_id` and as the `relio_user_id` metadata on
  the Checkout Session and the subscription.
- The account email, to create the Stripe customer on the first checkout and to keep it in step when
  the account email changes.

Never a person, a note, an interaction, a reminder or any other relationship content. Payment details
are entered on Stripe's own pages and never reach Relio. Stripe's error messages can quote submitted
values, so Relio logs Stripe failures by exception type, HTTP status and Stripe error code only.

What Relio stores, in `UserSubscriptions` (one row per user who has had a billing event): the tier,
renewal / cancellation / grace-period dates, the time of the last applied event, and the Stripe
customer and subscription ids. `ProcessedBillingEvents` records which Stripe event ids were applied
(id, type, time) and nothing about the user. Neither table holds relationship content.

- **Export** does not include the subscription row: the billing history lives at Stripe and is
  available through the customer portal.
- **Restore** never creates or changes a subscription, so a restore can never grant a plan.
- **Account deletion** cancels every live Relio subscription of the customer at Stripe *before*
  deleting anything, then deletes the subscription row in the same save as the account. If Stripe
  can't be reached, nothing is deleted and the user is asked to try again. With billing turned off
  there is no provider to call: cancel any leftover subscription in the Stripe Dashboard yourself.

## Setting it up

### In the Stripe Dashboard

1. **Product catalog**: create a product "Relio Pro" with two recurring prices in USD: **$2 per
   month** and **$12 per year**. Note both price ids (`price_...`).
2. **Developers → Webhooks**: add an endpoint at `https://<your-host>/api/webhooks/billing` listening
   for `checkout.session.completed`, `customer.subscription.created`,
   `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.paid` and
   `invoice.payment_failed`. Note its signing secret (`whsec_...`).
3. **Settings → Billing → Customer portal**: allow customers to update payment methods, view invoices
   and cancel. Choose **cancel at the end of the billing period** (the pricing page says the plan runs
   to the end of the period paid for). To let people switch between monthly and yearly, add both
   prices under *Subscriptions → Customers can switch plans*.
4. **Settings → Billing → Subscriptions and emails**: choose what happens after all payment retries
   fail. *Cancel the subscription* or *Mark as unpaid* both return the account to Free. With *Leave
   as-is*, Relio Pro is withheld 7 days after the last failed retry (and comes back if a later payment
   succeeds).

The Stripe account may be shared with other products: Relio ignores every event and subscription that
carries neither its `relio_user_id` metadata nor one of its two configured prices.

### Configuration

| Setting | Environment variable | Value |
|---|---|---|
| `Billing:Provider` | `Billing__Provider` | `Stripe` |
| `Billing:ApiKey` | `Billing__ApiKey` | Secret key (`sk_live_...`, or `sk_test_...` for staging). **Secret.** |
| `Billing:WebhookSigningSecret` | `Billing__WebhookSigningSecret` | The endpoint's `whsec_...`. **Secret.** |
| `Billing:ProMonthlyPriceId` | `Billing__ProMonthlyPriceId` | The $2 / month price id |
| `Billing:ProYearlyPriceId` | `Billing__ProYearlyPriceId` | The $12 / year price id |
| `Billing:CheckoutSuccessUrl` | `Billing__CheckoutSuccessUrl` | `https://<your-host>/Account/Manage/Plan` |
| `Billing:CheckoutCancelUrl` | `Billing__CheckoutCancelUrl` | `https://<your-host>/Account/Manage/Plan` |
| `Billing:PortalReturnUrl` | `Billing__PortalReturnUrl` | `https://<your-host>/Account/Manage/Plan` |

Never put the two secrets in `appsettings*.json`: use user secrets locally and environment variables
or a secret store in production. The URLs must be absolute `https` (plain `http` is accepted for
`localhost` only). Relio adds `session_id={CHECKOUT_SESSION_ID}` to the success URL and
`checkout=cancelled` to the cancel URL itself, so the plan page confirms a purchase the moment the
user returns, without waiting for the webhook. If any setting is missing or malformed, startup fails
with one message naming every problem (never a value).

With billing on, the Content Security Policy's `form-action` also allows `https://checkout.stripe.com`
and `https://billing.stripe.com`, because the plan page answers its form posts with a redirect there.
No Stripe script or other third-party resource is ever loaded by a page.

### Azure (the Bicep templates in `infra/`)

1. Add two Key Vault secrets: `stripe-api-key` and `stripe-webhook-signing-secret`.
2. Deploy with `billingProvider=Stripe`, `stripeProMonthlyPriceId=price_...` and
   `stripeProYearlyPriceId=price_...`. The template adds the `Billing__*` app settings, with the two
   secrets as Key Vault references and the return URLs built from the public origin.
3. Apply the `AddHostedBilling` migration before the new version serves traffic (startup applies it
   when `Database:ApplyMigrationsOnStartup=true`).

### Trying it locally

```bash
stripe listen --forward-to http://localhost:5224/api/webhooks/billing
```

Use the `whsec_...` that `stripe listen` prints as `Billing:WebhookSigningSecret` in user secrets,
set the three return URLs to `http://localhost:5224/Account/Manage/Plan`, and pay with the test card
`4242 4242 4242 4242`.

## How it works

- **Checkout** (`ISubscriptionService.StartCheckoutAsync`): refused while the user already has a
  subscription, including one whose access is suspended (that is fixed in the portal; a second
  checkout would create a second subscription). Stripe is also asked whether the customer already has
  a live Relio subscription, so a second tab or a double submit can't start another.
- **Webhooks** (`POST /api/webhooks/billing`, `IBillingWebhookProcessor`): the signature is checked
  with the endpoint secret and Stripe's five-minute timestamp tolerance; the event is then applied and
  its id recorded in **one** save, so a failure leaves neither and Stripe's retry applies it again. A
  repeated delivery is a no-op, and a concurrent duplicate loses on the unique event-id index. Events
  carry their creation time, and an event older than the last applied one changes nothing, because
  Stripe does not deliver in order. Every accepted delivery gets 200; only a failed signature gets 401.
- **Payment failure**: Relio Pro continues until Stripe's next retry (plus a day for its webhook to
  arrive), or 7 days when no retry is scheduled. After that the free limit applies until a payment
  succeeds or the subscription ends. This is evaluated on read; there is no background job.
