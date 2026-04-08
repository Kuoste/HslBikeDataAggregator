# Azure Preparation Plan

## Status
Ready for Validation

## Issue
- `#7` — `Set up Azure deployment (Consumption plan + Blob Storage)`

## Mode
- `MODIFY`

## Application Summary
- Existing Azure Functions isolated worker backend
- Target framework: `.NET 10`
- Runtime: Azure Functions v4
- Current responsibilities: timer-triggered polling plus HTTP read endpoints
- Required secrets/settings: `AzureWebJobsStorage`, `DigitransitSubscriptionKey`, `PollIntervalCron`, `SnapshotHistoryLimit`

## Requirements
- Add infrastructure-as-code for Azure hosting
- Add GitHub Actions CI for pull requests
- Add automated Azure deployment for a non-production environment on merges to `main`
- Add a separate production deployment workflow with an approval gate
- Keep the setup simple and appropriate for a small hobby project while following production-style practices

## Selected Recipe
- `Bicep` for infrastructure
- `GitHub Actions` for CI/CD
- `OIDC` federated Azure authentication for deployments

## Environment Strategy
### Development
- Purpose: automatic deployment target for `main`
- Trigger: push to `main`
- GitHub Environment: `dev`
- Azure resources: dedicated `dev` resource group and Function App

### Production
- Purpose: protected deployment target
- Trigger: manual workflow dispatch
- GitHub Environment: `prod`
- Azure resources: dedicated `prod` resource group and Function App
- Protection: GitHub environment approval before deployment

## Planned Azure Architecture
- One resource group per environment
- One storage account per environment
- One Application Insights resource per environment
- One Azure Functions hosting plan per environment
- One Azure Function App per environment
- One Azure API Management Consumption instance per environment (rate limiting, response caching, function key injection)
- App settings applied from IaC where appropriate
- Sensitive settings supplied from GitHub environment secrets at deployment time

## Planned Repository Changes
- `infra/` Bicep templates for the Function App stack
- `.github/workflows/ci.yml` for pull request validation
- `.github/workflows/deploy-dev.yml` for automatic dev deployment from `main`
- `.github/workflows/deploy-prod.yml` for manual production deployment with approval
- `README.md` updates for deployment prerequisites and operational flow

## Deployment Behaviour
### Pull Requests
- Restore
- Build
- Test
- Validate Bicep templates
- No Azure changes

### Main Branch
- Deploy/update `dev` infrastructure idempotently
- Publish and deploy the Functions app to `dev`
- Apply non-secret app settings
- Apply secret app settings from GitHub environment secrets

### Production
- Manual deployment only
- Reuse the same IaC templates with `prod` parameters
- Require GitHub environment approval
- Deploy infrastructure first, then application package

## Security Decisions
- Use Azure login via OIDC, not publish profiles
- Keep `DigitransitSubscriptionKey` in GitHub environment secrets and Azure app settings
- Separate `dev` and `prod` secrets and resource names
- Use least-privilege Azure role assignments for the deployment principal

## Assumptions
- Azure deployment target will be Azure Functions rather than Container Apps
- Separate `dev` and `prod` Azure environments are acceptable even for a hobby project
- The repository owner can create Azure federated credentials for GitHub Actions

## Open Inputs Needed Before Execution
- Azure subscription to use
- Azure region
- Preferred Function App names for `dev` and `prod`
- Preferred resource group names for `dev` and `prod`
- Whether to use Consumption or Flex Consumption for the Function App plan

## Execution Steps After Approval
1. Create the Bicep infrastructure layout and parameters
2. Add pull request CI workflow
3. Add `dev` deployment workflow
4. Add `prod` deployment workflow with approval environment
5. Update documentation for setup and required GitHub secrets
6. Validate build and test flows
