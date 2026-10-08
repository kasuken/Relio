# Azure Deployment Plan

> **Status:** Ready for Validation

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

**Rationale:** The production App Service is managed by the existing Bicep deployment. Keep configuration in IaC rather than changing the setting imperatively.

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
- [ ] Core validation: Azure CLI auth, Bicep build, ARM validate, ARM what-if
- [ ] Static RBAC review
- [ ] Azure Policy assignment review
- [ ] Record validation proof
- [ ] Deploy the Bicep configuration update
- [ ] Verify the live App Service setting and registration page

## 8. Validation Proof

> Populated by the azure-validate skill before deployment.

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Core validation | Pending | Pending | Pending |
| Static RBAC review | Pending | Pending | Pending |
| Azure Policy review | Pending | Pending | Pending |

## 9. Files to Generate

| File | Purpose | Status |
|------|---------|--------|
| `infra/modules/app-service.bicep` | Existing App Service settings; registration changed to Open | ✅ Updated |
| `.azure/deployment-plan.md` | This change plan | ✅ |

## 10. Next Steps

> Current: Validation

1. Validate the Bicep update and preview its impact.
2. Deploy the reviewed configuration and verify public registration.
