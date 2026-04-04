# HslBikeDataAggregator

C# Azure Functions backend for Helsinki city bike data aggregation.

This service is the only component that holds the HSL Digitransit API key. It polls the Digitransit API, stores aggregated data in Azure Blob Storage, and serves read-optimised JSON endpoints consumed by `HslBikeApp`.

## Architecture

- **Runtime:** Azure Functions v4, isolated worker model
- **Language:** C# / .NET 10
- **Storage:** Azure Blob Storage
- **Frontend consumer:** [`HslBikeApp`](https://github.com/Kuoste/HslBikeApp)

### Write/read separation

A timer-triggered function polls HSL every few minutes and writes JSON blobs to Azure Blob Storage. HTTP-triggered functions read those blobs directly, giving sub-second responses regardless of polling cadence.

### Functions

| Function | Trigger | Route | Purpose |
|---|---|---|---|
| `PollStations` | Timer (every 2–5 min) | — | Poll Digitransit, write station list, rolling snapshots, and hourly availability profiles to blob storage |
| `ProcessStationHistory` | Timer (daily) | — | Fetch HSL open history data and write per-station destination blobs |
| `GetStations` | HTTP GET | `/api/stations` | Return latest station availability |
| `GetSnapshots` | HTTP GET | `/api/snapshots` | Return recent rolling snapshots for trend calculation |
| `GetStationAvailability` | HTTP GET | `/api/stations/{id}/availability` | Return hourly availability profile (24 buckets) |
| `GetStationDestinations` | HTTP GET | `/api/stations/{id}/destinations` | Return popular destinations for a station |

All HTTP endpoints return JSON. CORS is enabled for `https://kuoste.github.io`.

## API response shapes

### `GET /api/stations` → `BikeStation[]`

```json
[
  {
    "id": "001",
    "name": "Kaivopuisto",
    "lat": 60.155,
    "lon": 24.950,
    "capacity": 20,
    "bikesAvailable": 5,
    "spacesAvailable": 15,
    "isActive": true
  }
]
```

### `GET /api/snapshots` → `StationSnapshot[]`

```json
[
  {
    "timestamp": "2026-04-03T12:00:00+03:00",
    "bikeCounts": { "001": 5, "002": 3 }
  }
]
```

### `GET /api/stations/{id}/availability` → `HourlyAvailability[]`

```json
[
  { "hour": 8, "averageBikesAvailable": 5.2 }
]
```

### `GET /api/stations/{id}/destinations` → `StationHistory[]`

```json
[
  {
    "departureStationId": "001",
    "arrivalStationId": "023",
    "tripCount": 42,
    "averageDurationSeconds": 360.5,
    "averageDistanceMetres": 1250.3
  }
]
```

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
- Explicitly link pull requests to their GitHub issue using closing keywords such as `Closes #<issue>` to ensure the issue is automatically closed when the PR is merged.
