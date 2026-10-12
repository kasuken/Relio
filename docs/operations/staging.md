# Staging deployment

Staging is a full, isolated copy of the hosted Relio service on Azure. It exists to prove a build
(application, database migrations, TLS, health checks) before production, without touching
production. It is deployed by `.github/workflows/staging.yml` from `infra/staging.bicep`.
Production is released by `.github/workflows/release.yml` and is not changed by anything here.

Hosted billing and product metrics are **off** in staging (`Billing__Provider=None`, no
`HostedFeatures__ProductMetrics__Enabled` setting, so the default `false` applies). Every feature
works with them off.

## Purpose and isolation

| Thing | Production | Staging |
|---|---|---|
| Resource group | `rg-relio-prod` | `rg-relio-staging` (West Europe) |
| App Service plan / web app | `asp-relio-prod-edff` / `app-relio-prod-edff`, B1 | `asp-relio-stg-edff` / `app-relio-stg-edff`, B1 (smallest SKU with Always On and WebSockets) |
| SQL server / database | `sql-relio-prod-edff` / `relio`, Basic | `sql-relio-stg-edff` / `relio`, Basic |
| Key Vault | `kv-relio-prod-edff` | `kv-relio-stg-edff` (its own `sql-connection-string` and `dp-cert-password`) |
| Log Analytics | `log-relio-prod-edff` | `log-relio-stg-edff` |
| TLS | `www.relio.club`, managed certificate | `*.azurewebsites.net` platform certificate; HTTPS only, TLS 1.2 minimum, FTPS disabled |
| Deploy identity | `id-relio-github-deploy` (Website Contributor on the web app) | `id-relio-github-deploy-staging` (Contributor on `rg-relio-staging`, plus role assignment rights limited to Key Vault Secrets User/Officer) |
| GitHub environment | `production` | `staging` (deployments from `main` only) |
| App settings | `Production`, registration open | `Staging`, registration invitation-only (the first account is always allowed and becomes the administrator), Data Protection application name `Relio-Staging` |

The staging identity has no role on production, and the production identity has none on staging.
The key ring and the wrapping certificate live on the app's own `/home` storage, so they are
separate as well. Staging serves `robots.txt` with `Disallow: /` and `X-Robots-Tag: noindex`
because the app only allows indexing in the `Production` environment.

Cost: roughly a B1 plan plus a Basic SQL database (5 DTU, 2 GB); Key Vault and Log Analytics are
negligible at this volume. Tear staging down when it is not needed (see Teardown).

The names above end in `edff`, as production's do. The suffix is `namingSuffix` in
`infra/staging.bicepparam`; change it together with the `env` block of the workflow if a name is
taken.

## What the workflow does

`staging.yml` runs on **workflow_dispatch** (inputs `infrastructure` and `restart-check`, both true
by default) and, once the repository variable `STAGING_AUTO_DEPLOY` is `true`, on every push to
`main`. It only runs on `kasuken/Relio` and only from `main`.

1. **Build** waits for the CI workflow to pass on the commit (the same gate as production), then runs
   `dotnet publish` and uploads the package.
2. **Deploy** (environment `staging`, OIDC login, no stored Azure secret):
   1. *Infrastructure what-if* of `infra/staging.bicep`; the counts per change type go to the run
      summary. The run **fails if the what-if contains a Delete**.
   2. *Deploy infrastructure* and check that the template's web app name equals the workflow's.
   3. *Deploy to App Service* with the published package. The app starts with `dotnet Relio.Web.dll`
      and applies EF Core migrations before it listens (`Database__ApplyMigrationsOnStartup=true`).
   4. *Assert platform configuration* from Azure: `httpsOnly`, `minTlsVersion` 1.2, `ftpsState`
      Disabled, `ASPNETCORE_ENVIRONMENT=Staging`, `Billing__Provider=None`, product metrics off,
      startup migrations on, no build on deploy. Only these named, non-secret settings are read.
   5. *Verify deployment* (`.github/scripts/verify-deployment.sh`): `/health/ready` (retrying for up
      to 10 minutes while a fresh database is migrated) and `/health/live`, the smoke pages, the
      HTTP to HTTPS redirect, HSTS, `X-Content-Type-Options` and a Content-Security-Policy, a TLS 1.2
      handshake with certificate validation (issuer and expiry are printed) and a refused TLS 1.1
      connection, and the noindex checks.
   6. *Restart and verify readiness*: restarts the app and waits for `/health/ready` again. This is a
      second startup with nothing to migrate.
   7. On failure, the status of the Key Vault references (names and statuses only).

`/health/ready` is healthy only when the database is reachable **and** no EF Core migration is
pending (`PendingMigrationsHealthCheck`), so a 200 proves the schema is current.

## One-time setup

Done once by the repository owner after the pull request that adds staging merges. Nothing here is
run by the workflow.

```bash
sub=da80e753-e223-4526-835c-6cc1ec387035; rg=rg-relio-staging; repo=kasuken/Relio
az account set -s $sub

# 1. Name availability (read-only)
az rest --method post --uri "https://management.azure.com/subscriptions/$sub/providers/Microsoft.Web/checknameavailability?api-version=2024-04-01" --body '{"name":"app-relio-stg-edff","type":"Microsoft.Web/sites"}'
az rest --method post --uri "https://management.azure.com/subscriptions/$sub/providers/Microsoft.Sql/checkNameAvailability?api-version=2021-11-01" --body '{"name":"sql-relio-stg-edff","type":"Microsoft.Sql/servers"}'
az keyvault check-name --name kv-relio-stg-edff

# 2. Resource group
az group create -n $rg -l westeurope --tags environment=staging application=relio

# 3. Staging deployment identity (OIDC). Use the immutable subject prefix GitHub reports for this repository.
prefix=$(gh api repos/$repo/actions/oidc/customization/sub --jq .sub_claim_prefix)
az deployment group create -g $rg -n relio-staging-github-identity --template-file infra/github-deployment-identity.bicep \
  --parameters identityName=id-relio-github-deploy-staging githubEnvironment=staging githubSubjectPrefix="$prefix" grantResourceGroupDeployment=true \
  --query properties.outputs -o json

# 4. GitHub environment "staging": deployments from main only
gh api -X PUT repos/$repo/environments/staging --input - <<'JSON'
{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}
JSON
gh api -X POST repos/$repo/environments/staging/deployment-branch-policies -f name=main -f type=branch

# 5. Environment secrets. The three Azure values are the outputs of step 3 (clientId, tenantId, subscriptionId).
#    `gh secret set` without --body prompts, so a value never lands in shell history.
gh secret set AZURE_CLIENT_ID --env staging --repo $repo
gh secret set AZURE_TENANT_ID --env staging --repo $repo
gh secret set AZURE_SUBSCRIPTION_ID --env staging --repo $repo
gh secret set SQL_ADMIN_PASSWORD --env staging --repo $repo   # e.g. "Rl1-$(openssl rand -hex 24)": no ; ' or " (it is concatenated into a connection string)
gh secret set DP_CERT_PASSWORD --env staging --repo $repo     # same generator; do not rotate it casually (see Secrets)

# Optional: Key Vault Secrets Officer for yourself, to inspect the vault
gh variable set DEPLOYER_OBJECT_ID --env staging --repo $repo --body "$(az ad signed-in-user show --query id -o tsv)"
```

Optional, later: `gh variable set STAGING_AUTO_DEPLOY --body true --repo $repo` deploys every green
`main` commit to staging.

The OIDC subject must be the one GitHub actually issues. This repository uses immutable subjects
(`sub_claim_prefix` is not `repo:kasuken/Relio`), which is why step 3 reads the prefix from the API
instead of assuming it.

## Secrets

| Where | What | Notes |
|---|---|---|
| GitHub `staging` environment secrets | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | Identifiers of the OIDC identity, not credentials. There is no Azure client secret. |
| GitHub `staging` environment secrets | `SQL_ADMIN_PASSWORD`, `DP_CERT_PASSWORD` | Generated once. They reach the Azure CLI only as environment variables read by `infra/staging.bicepparam`, and are stored in the staging Key Vault by the template. The app reads them through Key Vault references with its managed identity. |
| GitHub `staging` environment variable | `DEPLOYER_OBJECT_ID` (optional) | An object id, not a secret. |
| Key Vault `kv-relio-stg-edff` | `sql-connection-string`, `dp-cert-password` | Never in the repository, app settings in plain text or logs. |

Never commit a secret, key material or a filled parameters file.

**Do not rotate `DP_CERT_PASSWORD`.** The wrapping certificate at `/home/relio/certificate.pfx` was
generated by the app with that password on first start. A new password means deleting the
certificate and the key ring on the app's storage, which invalidates staging sessions and any
field-encrypted data in the staging database. That is acceptable for staging, but it is a deliberate
reset, never a routine rotation. See `docs/security/data-protection.md`.

## Running a deployment

```bash
gh workflow run staging.yml --ref main
gh run watch
```

The first run creates every resource and migrates an empty database; allow up to 15 minutes. If it
fails at readiness on the very first run, run it again: see Troubleshooting.

## Repeatability and migrations

- The app applies migrations on startup (`MigrateAsync`), before it accepts traffic, so a deployment
  needs no separate migration step and a failed migration keeps the app from becoming ready.
- Readiness includes "no pending migrations", so the workflow's readiness wait cannot pass on an old
  schema.
- The restart check starts the app a second time with nothing to migrate and requires readiness again.
- The infrastructure template is incremental and uses static tags and deterministic role assignment
  names, so a second run's what-if shows no Create and no Delete.

## Evidence for issue #120

Collect this from two runs (a fresh deployment and a repeat) and post it on the issue:

1. **Fresh deployment**: the run URL, the what-if counts (mostly Create), and each verification line
   (ready, live, smoke, HTTP to HTTPS, HSTS max-age, TLS issuer and expiry, minTls 1.2 and httpsOnly,
   Billing None, product metrics off, noindex, ready after the restart).
2. **Repeat deployment** with the same inputs: what-if with 0 Create and 0 Delete (only NoChange,
   Modify or Ignore), and ready again.
3. **Migration logs** in Log Analytics `log-relio-stg-edff`:

   ```kusto
   AppServiceConsoleLogs
   | where ResultDescription has "Applying migration" or ResultDescription has "No migrations were applied"
   | project TimeGenerated, ResultDescription
   | order by TimeGenerated asc
   ```

   The first run lists each migration; later starts report that no migrations were applied.

The verification script is read-only and can also be pointed at production for evidence:
`BASE_URL=https://www.relio.club bash .github/scripts/verify-deployment.sh`.

## Troubleshooting

- **`AADSTS70021` at Azure login**: the federated credential subject does not match the token. Check
  that step 3 used `sub_claim_prefix` from the API and that the job runs in the `staging` environment.
- **First run is not ready, Key Vault reference status is not `Resolved`**: the app's role on the vault
  had not propagated when it started. Run the workflow again.
- **The app does not start after a package deployment**: the Oryx build settings must be off
  (`SCM_DO_BUILD_DURING_DEPLOYMENT=false`, no `PROJECT`), and the startup command is
  `dotnet Relio.Web.dll`. The workflow asserts the first.
- **What-if reports a Delete**: the template and the resource group have drifted apart. The run stops
  before deploying; inspect the what-if output (`whatif.json` is not uploaded; run the same command
  locally with the secrets as environment variables).
- **A name is already taken**: change `namingSuffix` in `infra/staging.bicepparam` and `env` in the
  workflow together.

## Teardown

```bash
az group delete -n rg-relio-staging
az keyvault purge --name kv-relio-stg-edff   # permanent; Key Vault soft-deletes by default
```

Then delete the GitHub environment `staging`. Teardown is the repository owner's decision.
