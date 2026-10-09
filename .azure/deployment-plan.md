# Azure Deployment Plan

> **Status:** Validated

Generated: 2026-10-08

## 1. Project Overview

**Goal:** Open public registration on the existing production Relio App Service after the first account was created.

**Path:** Modify Existing

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | Production |
| Scale | Small |
| Budget | Cost-Optimized |
| Subscription | Microsoft Azure Sponsorship (`da80e753-e223-4526-835c-6cc1ec387035`) |
| Location | `westeurope` |

No new resources are being provisioned. This change adds no incremental Azure cost.

## 3. Components Detected

| Component | Type | Technology | Path |
|-----------|------|------------|------|
| Relio web app | Web application | .NET 10 Blazor Server | `Relio.Web` |

## 4. Recipe Selection

**Selected:** Bicep

**Rationale:** The production App Service is managed by the existing Bicep deployment. Keep configuration in IaC rather than changing the setting imperatively. Deploy only `infra/modules/app-service.bicep` at resource-group scope using values read from the existing resources; do not rerun the subscription-wide infrastructure deployment.

## 5. Architecture

**Stack:** App Service

| Component | Azure Service | SKU |
|-----------|---------------|-----|
| Relio web app | Existing Linux App Service | B1 |
| Relio database | Existing Azure SQL Database | Basic |

Supporting services (Key Vault and Log Analytics) remain unchanged.

**Configuration changes:** `Registration__Mode` changes from `Closed` to `Open` in `infra/modules/app-service.bicep`. SCM publishing credentials are also set to remain disabled, matching the live post-deployment security state. No application code or Azure resource topology changes.

## 6. Provisioning Limit Checklist

No resources are being provisioned or resized; resource quotas and capacity are unchanged. Existing production resources are reused.

**Status:** ✅ No new quota demand.

## 7. Execution Checklist

- [x] Existing deployment and production configuration inspected
- [x] User approved opening registration
- [x] Bicep app setting updated
- [x] Subscription and location retained from the approved production deployment
- [x] Plan prepared for validation
- [x] Core validation: Azure CLI auth, Bicep build, App Service module ARM validate, module what-if
- [x] Static RBAC review
- [x] Azure Policy assignment review
- [x] Record validation proof
- [x] Deploy the Bicep configuration update
- [x] Verify the live App Service setting and registration page

### All validation checks pass

- [x] 1. Core Validation (CLI, auth, build, validate, what-if) — validated the targeted `infra/modules/app-service.bicep` at resource-group scope; no deletes in the preview.
- [x] 2. Linting (optional) — Bicep build passed.
- [x] 3. Azure Policy Validation — reviewed subscription policy assignments; no additional blocking assignment observed.

## 8. Validation Proof

> Populated by the azure-validate skill before deployment.

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Bicep build | `az bicep build --file infra/main.bicep` | ✅ Pass; main template compiles | 2026-10-08 |
| Resource-group ARM validation | `validate-deployment.ps1 -Scope group -ResourceGroup rg-relio-prod -Template infra/modules/app-service.bicep -Parameters <temporary live-resource parameters>` | ✅ Pass; authenticated, compiled, validated, and completed what-if | 2026-10-08 |
| Resource-group what-if | `az deployment group what-if --no-pretty-print -o json` for `app-service.bicep` with parameters read from live resources | ✅ 2 modifies (the App Service and its diagnostic settings), 2 no-change publishing-policy children, 6 ignored existing resources, 0 creates, 0 deletes. The validation helper's summary counts were inconsistent with the raw JSON, so the resource-level JSON was used. | 2026-10-08 |
| Solution build and tests | `dotnet build Relio.slnx --configuration Release --no-restore`; `dotnet test Relio.slnx --configuration Release --no-build` | ✅ Pass; build succeeded, 2,330 passed, 212 skipped, 0 failed | 2026-10-08 |
| Vulnerability scan | `dotnet list Relio.slnx package --vulnerable --include-transitive` | ✅ No vulnerable packages reported | 2026-10-08 |
| EF model and whitespace | `dotnet ef migrations has-pending-model-changes ...`; `git diff --check` | ✅ No pending model changes; diff check passed | 2026-10-08 |
| Bicep deployment | `az deployment group create` (`relio-registration-open-20261008`) | ✅ Succeeded at 2026-10-08 13:14 UTC | 2026-10-08 |
| Live verification | App Service settings query and HTTPS requests to `/health/ready` and `/Account/Register` | ✅ `Registration__Mode=Open`; registration form present (HTTP 200); readiness HTTP 200; SCM publishing basic auth remains disabled | 2026-10-08 |
| Static RBAC review | Reviewed `infra/modules/role-assignments.bicep` | ✅ App system-assigned identity retains Key Vault Secrets User scoped to the vault; deployer role unchanged | 2026-10-08 |
| Azure Policy review | `az policy assignment list --subscription da80e753-e223-4526-835c-6cc1ec387035 --scope /subscriptions/da80e753-e223-4526-835c-6cc1ec387035` | ✅ `SecurityCenterBuiltIn` assignment only; no additional blocking assignments observed. Azure Policy MCP unavailable due tenant mismatch. | 2026-10-08 |
| Custom-domain DNS | `Resolve-DnsName www.relio.club -Type CNAME` and `Resolve-DnsName asuid.www.relio.club -Type TXT -Server 1.1.1.1` | ✅ CNAME targets `app-relio-prod-edff.azurewebsites.net`; ownership TXT resolves. | 2026-10-08 |
| Custom-domain Bicep build | `az bicep build --file infra/modules/app-service-custom-domain.bicep`; `az bicep build --file infra/main.bicep` | ✅ Pass; compiles with non-blocking BCP081 type-metadata warnings. | 2026-10-08 |
| Custom-domain ARM validation/what-if | `validate-deployment.ps1 -Scope group -ResourceGroup rg-relio-prod -Template infra/modules/app-service-custom-domain.bicep -Parameters <temporary app-name parameters>` | ✅ Overall PASS; direct resource-level preview shows one hostname-binding create, zero modifies/deletes. | 2026-10-08 |
| Custom-domain lint | `az bicep lint --file infra/modules/app-service-custom-domain.bicep` | ✅ Pass; only non-blocking BCP081 warnings. | 2026-10-08 |
| Custom-domain RBAC and policy review | Reviewed `infra/modules/app-service-custom-domain.bicep`; `az policy assignment list` | ✅ No RBAC added; one existing Azure Security Center subscription assignment. | 2026-10-08 |
| Managed certificate and SNI | `az webapp config ssl create`; `az webapp config ssl bind --ssl-type SNI` | ✅ Managed certificate issued for `www.relio.club` and bound with SNI; expiry 2027-04-08. | 2026-10-08 |
| Final custom-domain Bicep validation | `validate-deployment.ps1 -Scope group -ResourceGroup rg-relio-prod -Template infra/modules/app-service-custom-domain.bicep -Parameters <temporary app-name parameters>` | ✅ Overall PASS; final what-if is Create: 0, Modify: 0, Delete: 0. | 2026-10-08 |
| Final custom-domain deployment | `az deployment group create --name relio-custom-domain-https-20261008 --resource-group rg-relio-prod --template-file infra/modules/app-service-custom-domain.bicep` | ✅ Succeeded; applies the final HTTPS/SNI desired state. | 2026-10-08 |
| HTTPS endpoint | `curl.exe --resolve www.relio.club:443:20.105.232.51 https://www.relio.club/health/ready` | ✅ HTTP 200; TLS verification passed. Local DNS cache still had the old CNAME; public DNS resolvers return the new App Service CNAME. | 2026-10-08 |
| Final repository checks | `dotnet build Relio.slnx --configuration Release --no-restore`; `dotnet test Relio.slnx --configuration Release --no-build`; vulnerable-package scan; EF model check; `git diff --check` | ✅ Build passed; 2,330 tests passed, 211 skipped, 0 failed; no vulnerable packages or pending EF model changes; diff check passed. | 2026-10-08 |

**Validated by:** azure-validate workflow completed through `UpdateStatus`; full main-template ARM validation was not used because it is not the scoped target and waits on unrelated secure deployment parameters.
**Validation timestamp:** 2026-10-08

## Role Assignment Verification

- **Status:** Verified for the App Service and GitHub release deployment.
- **Identities checked:** App Service system-assigned identity, current deployment user, and `id-relio-github-deploy`.
- **Roles confirmed:** Key Vault Secrets User for the app identity and Key Vault Secrets Officer for the deployment user, both scoped to `kv-relio-prod-edff`; Website Contributor for the GitHub identity, scoped only to `app-relio-prod-edff`.

## Additional request: `www.relio.club` custom domain

**Status:** Validated — hostname and HTTPS deployed

**Goal:** Attach `www.relio.club` to the existing production App Service and enable HTTPS.

**Scope:** Add the hostname to `app-relio-prod-edff` in `rg-relio-prod`, then provision and bind an App Service managed certificate with SNI. Preserve the existing `relio.club` apex DNS records; the GoDaddy CNAME for `www` already points to `app-relio-prod-edff.azurewebsites.net`. Keep the hostname binding represented in the existing Bicep deployment where supported.

**Azure context:** Microsoft Azure Sponsorship (`da80e753-e223-4526-835c-6cc1ec387035`), West Europe, existing `rg-relio-prod`.

**Recipe:** The hostname binding was deployed through `infra/modules/app-service-custom-domain.bicep`, then the free App Service managed certificate was provisioned and bound with SNI. The module now references the existing managed certificate's current thumbprint so later deployments preserve HTTPS.

**Validation:** The GoDaddy CNAME and Azure ownership TXT record are publicly resolvable. Build and run a resource-group what-if for the targeted App Service module before deployment. No compute resources are being added or resized.

**Validation/deployment gate:** The Azure CLI is authenticated to the selected subscription. Validate the targeted change before applying it; do not rerun the subscription-wide infrastructure deployment.

**Validation steps for this request:**
- Resolve `www.relio.club` CNAME and `asuid.www.relio.club` TXT using public DNS.
- Build `infra/modules/app-service-custom-domain.bicep` and `infra/main.bicep`.
- Run a resource-group what-if for only the custom-domain module, with `appServiceName` set to the existing production app; confirm the live HTTPS/SNI binding is the desired state.
- Review the Bicep RBAC declarations; this change adds no role assignments.
- Confirm the hostname binding, managed certificate/SNI state and an HTTPS response from `https://www.relio.club`.

**All validation checks pass**
- [x] 1. Core Validation (CLI, auth, build, validate, what-if) — targeted resource-group validation passed; final what-if has no changes.
- [x] 2. Linting (optional) — Bicep lint passed with non-blocking type-metadata warnings.
- [x] 3. Azure Policy Validation — existing subscription assignment reviewed; no blocking policy observed for this change.
- **GitHub OIDC:** The managed identity trusts `repo:kasuken/Relio:environment:production` with audience `api://AzureADTokenExchange`. The GitHub `production` environment contains the three required Azure identifiers and permits only the `main` branch and `v*` tags.
- **Issues:** None identified. The release workflow has not been triggered.

## 9. Files to Generate

| File | Purpose | Status |
|------|---------|--------|
| `infra/modules/app-service.bicep` | Existing App Service settings; registration changed to Open | ✅ Updated |
| `infra/github-deployment-identity.bicep` | GitHub OIDC identity, federation, and app-scoped deploy role | ✅ Deployed |
| `.github/workflows/release.yml` | Shared, pinned release workflow targeting the live app | ✅ Configured |
| `.azure/deployment-plan.md` | This change plan | ✅ |

## 10. Next Steps

> Current: Complete. Registration is open and verified.

1. Registration and the GitHub OIDC release prerequisites are configured. The reusable release workflow is ready but has not been run.
