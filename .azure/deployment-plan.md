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

**Validated by:** azure-validate workflow completed through `UpdateStatus`; full main-template ARM validation was not used because it is not the scoped target and waits on unrelated secure deployment parameters.
**Validation timestamp:** 2026-10-08

## Role Assignment Verification

- **Status:** Verified for the App Service and GitHub release deployment.
- **Identities checked:** App Service system-assigned identity, current deployment user, and `id-relio-github-deploy`.
- **Roles confirmed:** Key Vault Secrets User for the app identity and Key Vault Secrets Officer for the deployment user, both scoped to `kv-relio-prod-edff`; Website Contributor for the GitHub identity, scoped only to `app-relio-prod-edff`.
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
