# HslBikeDataAggregator

C# Azure Functions backend for Helsinki city bike data aggregation.

This service polls the HSL Digitransit API, stores aggregated bike station data in Azure Blob Storage, and serves read-optimised JSON endpoints for `HslBikeApp`.

## Current status

The repository now contains the initial Azure Functions scaffold for issue `#1`:

- Azure Functions isolated worker project
- `.NET 10` target with current isolated worker packages
- shared models for station, snapshot, history, and hourly availability payloads
- starter HTTP functions for stations, snapshots, availability, and destinations
- starter timer function for station polling
- local configuration placeholders for storage, Digitransit API key, polling cadence, and snapshot retention

The read-side functions currently return empty collections until the storage-backed implementation is added.

## Architecture

- **Runtime:** Azure Functions v4, isolated worker model
- **Language:** C# / .NET 10
- **Storage:** Azure Blob Storage
- **Frontend consumer:** `HslBikeApp`

### Planned functions

- `GET /api/stations` - latest station availability
- `GET /api/snapshots` - recent rolling snapshots for trend calculation
- `GET /api/stations/{id}/availability` - hourly availability profile
- `GET /api/stations/{id}/destinations` - popular destinations based on history data
- `PollStations` timer trigger - polls HSL and writes aggregate blobs

## Project layout

- `src/HslBikeDataAggregator/Functions/` - HTTP and timer functions
- `src/HslBikeDataAggregator/Models/` - shared API/storage DTOs
- `src/HslBikeDataAggregator/Services/` - aggregation and orchestration services
- `src/HslBikeDataAggregator/Storage/` - blob naming and storage helpers
- `docs/adr/` - architecture decision records

## Local development

### Prerequisites

- .NET SDK `10.0.201` or later compatible with `global.json`
- Azure Functions Core Tools v4
- Azurite or another storage emulator for local blob storage work

### Configuration

Local settings live in `src/HslBikeDataAggregator/local.settings.json` and are not committed.
Copy `src/HslBikeDataAggregator/local.settings.example.json` to `local.settings.json` for local development.

Expected values:

- `AzureWebJobsStorage`
- `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`
- `DigitransitSubscriptionKey`
- `PollIntervalCron`
- `SnapshotHistoryLimit`

### Build

`dotnet build HslBikeDataAggregator.slnx --configuration Release`

### Run locally

From `src/HslBikeDataAggregator`:

`func start`

## Azure deployment

Deployment is split into `dev` and `prod` environments.

- Pull requests run `CI` only.
- Pushes to `main` deploy automatically to the `dev` Azure environment.
- Production deploys run manually through the `Deploy prod` workflow and should be protected with a GitHub environment approval.

### Infrastructure

Azure infrastructure is defined in `infra/main.bicep` with environment parameter files:

- `infra/dev.bicepparam`
- `infra/prod.bicepparam`

The template provisions:

- Azure Functions hosting plan on the Consumption tier
- Azure Function App
- Storage account
- Application Insights

### GitHub environments

Create two GitHub environments:

- `dev`
- `prod`

Recommended names for this repository:

- `dev`
  - `AZURE_RESOURCE_GROUP=rg-hsl-bike-data-aggregator-dev`
  - `AZURE_FUNCTION_APP_NAME=func-hsl-bike-data-aggregator-dev`
- `prod`
  - `AZURE_RESOURCE_GROUP=rg-hsl-bike-data-aggregator-prod`
  - `AZURE_FUNCTION_APP_NAME=func-hsl-bike-data-aggregator-prod`

Set these environment variables in each environment:

- `AZURE_LOCATION`
- `AZURE_RESOURCE_GROUP`
- `AZURE_FUNCTION_APP_NAME`

Set these environment secrets in each environment:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `DIGITRANSIT_SUBSCRIPTION_KEY`

### Azure authentication

GitHub Actions uses Azure OpenID Connect federation via `azure/login`. This avoids publish profiles and long-lived client secrets.

One-time Azure setup:

1. Create an Entra application or service principal for GitHub Actions.
2. Add a federated credential for this repository and the target GitHub environment.
3. Grant the identity `Contributor` access to the target resource group.

### Workflows

- `.github/workflows/ci.yml` - build, test, and validate Bicep
- `.github/workflows/deploy-dev.yml` - deploy infrastructure and app code to `dev`
- `.github/workflows/deploy-prod.yml` - manually deploy infrastructure and app code to `prod`

## Issue delivery workflow

- Keep code changes linked to an open GitHub issue.
- Use an issue branch named `issue-<number>-<short-description>`.
- If an issue was closed before the code was pushed, reopen the issue before continuing.
- Add or update automated tests for delivered behaviour or repository configuration changes.
- Run `dotnet build HslBikeDataAggregator.slnx` and the relevant tests before treating the issue as complete.
- Do not consider an issue done until the branch is pushed, the pull request is open, and CI is passing.

## Next milestone

The next implementation step is issue `#2`: connect `PollStations` to the HSL Digitransit API, persist the latest station list and rolling snapshots to Blob Storage, and then wire the HTTP functions to real stored data.
