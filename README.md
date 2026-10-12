# HslBikeDataAggregator

C# Azure Functions backend for Helsinki city bike data aggregation.

This service is the **only component** that holds the HSL Digitransit API key. It polls the Digitransit GraphQL API for live station availability, downloads open trip-history CSVs for monthly demand statistics, stores all aggregated data in Azure Blob Storage, and serves read-optimised JSON endpoints consumed by the [`HslBikeApp`](https://github.com/TmiKuoste/HslBikeApp) Blazor frontend.

## Architecture

| Concern | Technology |
|---|---|
| Runtime | Azure Functions v4, isolated worker model, .NET 10 |
| Language | C# with nullable, implicit usings, file-scoped namespaces |
| Hosting plan | Flex Consumption (FC1) — pay-per-execution with scale-to-zero |
| Storage | Azure Blob Storage (identity-based access via managed identity) |
| API gateway | Azure API Management, Consumption tier |
| Observability | Application Insights backed by Log Analytics workspace |
| Auth (Azure) | User-assigned managed identity — no connection strings in production |
| Auth (GitHub) | OpenID Connect federation — no long-lived secrets |
| Frontend | [`HslBikeApp`](https://github.com/TmiKuoste/HslBikeApp) (Blazor WebAssembly, GitHub Pages) |

### Data flow

The backend has two **independent** data pipelines that share no data with each other:

```
  ┌─────────────────────────────────────────────────────────────────┐
  │  PIPELINE 1 — Live station availability (Digitransit API)      │
  │                                                                │
  │  HSL Digitransit GraphQL API ◄── real-time bike/space counts   │
  │          │                                                     │
  │          ├───── timer ────►  PollStations                      │
  │          │                      │                              │
  │          │                      ▼                              │
  │          │               snapshots/recent.json  (Blob Storage) │
  │          │                      │                              │
  │          │                      ▼                              │
  │          │               GetSnapshots (HTTP)                   │
  │          │                                                     │
  │          └───── on demand ► LiveStationCacheService (in-memory)│
  │                                 │                              │
  │                                 ▼                              │
  │                          GetStations (HTTP)                    │
  └─────────────────────────────────────────────────────────────────┘

  ┌─────────────────────────────────────────────────────────────────┐
  │  PIPELINE 2 — Historical trip statistics (HSL open data CSV)   │
  │                                                                │
  │  dev.hsl.fi ◄── monthly CSV files of completed bike journeys   │
  │          │       (departure/arrival station, time, distance)    │
  │          │                                                     │
  │          └───── timer ────►  ProcessStationHistory             │
  │                                 │  aggregates per-station      │
  │                                 │  demand profiles and         │
  │                                 │  destination tables          │
  │                                 ▼                              │
  │                          monthly-stats/{id}.json (Blob Storage)│
  │                                 │                              │
  │                                 ▼                              │
  │                          GetStationStatistics (HTTP)           │
  └─────────────────────────────────────────────────────────────────┘

  ┌─────────────────────────────────────────────────────────────────┐
  │  PIPELINE 3 — External open data (venue fill levels, etc.)     │
  │                                                                │
  │  External APIs ◄── venue fill levels, future sources           │
  │          │                                                     │
  │          └───── timer ────►  PollOpenData                      │
  │                                 │  per IOpenDataSource;        │
  │                                 │  -1 sentinel on failure      │
  │                                 ▼                              │
  │                    open-data/{sourceId}/recent.json (Blob)     │
  │                                 │                              │
  │                                 ▼                              │
  │                          GetOpenData (HTTP)                    │
  └─────────────────────────────────────────────────────────────────┘

                     All HTTP endpoints
                             │
                             ▼
                    Azure API Management
                     (rate limit, cache,
                      function key injection)
                             │
                             ▼
                   HslBikeApp (Blazor WASM)
```

### Write/read separation

Timer-triggered functions write precomputed JSON blobs. `GetSnapshots` and `GetStationStatistics` return those blobs directly, giving sub-second responses even on cold start. See [ADR-001](docs/adr/001-cold-start-mitigation.md).

**Live station cache** — `GetStations` does **not** read from blob storage. Instead, `LiveStationCacheService` calls the Digitransit API on the first request and caches the result in-memory for 2 minutes. This provides fresher data than the timer's 15-minute polling interval while still protecting the upstream API from excessive calls.

### Functions

| Function | Trigger | Route | Purpose |
|---|---|---|---|
| `PollStations` | Timer (`%PollIntervalCron%`, default every 15 min) | — | Poll Digitransit GraphQL API and append a snapshot to the rolling time series blob |
| `ProcessStationHistory` | Timer (`%HistoryProcessingCron%`, default 02:00 UTC daily) | — | Discover the newest available HSL open-data trip-history CSV, aggregate per-station monthly statistics, and write one blob per station |
| `PollOpenData` | Timer (`%OpenDataPollIntervalCron%`, default every 15 min) | — | Poll all configured `IOpenDataSource` implementations and append a value to each source's rolling time series blob |
| `GetStations` | HTTP GET | `/api/stations` | Return live station availability (via in-memory cache, falls back to Digitransit) |
| `GetSnapshots` | HTTP GET | `/api/snapshots` | Return the compact rolling snapshot time series for trend arrows |
| `GetStationStatistics` | HTTP GET | `/api/stations/{stationId}/statistics` | Return monthly demand profile and top destinations for a station |
| `GetOpenData` | HTTP GET | `/api/open-data` | Return rolling time series for all configured open data sources |

All HTTP triggers use `AuthorizationLevel.Function`. Azure API Management injects the function host key so the frontend never needs to know it. Direct calls without the key receive `401 Unauthorized`. See [ADR-003](docs/adr/003-apim-gateway.md).

### Blob layout

| Blob path | Content | Written by |
|---|---|---|
| `bike-data/snapshots/recent.json` | Rolling snapshot time series (up to `SnapshotHistoryLimit` entries) | `PollStations` |
| `bike-data/monthly-stats/{stationId}.json` | Monthly demand buckets and destination table for one station | `ProcessStationHistory` |
| `open-data/{sourceId}/recent.json` | Rolling time series for one open data source (up to `OpenData:HistoryLimit` entries); `-1` = unavailable | `PollOpenData` |

### APIM gateway policies

- **Rate limiting**: 200 requests/minute globally (Consumption tier does not support per-IP keyed limits).
- **No gateway response caching**: the Consumption tier has no built-in cache, so responses are cached by browsers via `Cache-Control` only.
- **CORS**: preflight results are cacheable for one hour (`preflight-result-max-age`).
- **Function key injection**: host key stored as a secret Named Value and set via `x-functions-key` header.
- **CORS**: allows the configured frontend origins.

## API response shapes

### `GET /api/stations` → `BikeStation[]`

```json
[
  {
    "id": "smoove:0001",
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

### `GET /api/snapshots` → `SnapshotTimeSeries`

The time series uses a columnar layout to minimise payload size. Each station row contains the station ID followed by one bike count per timestamp. A count of `-1` indicates no data for that interval (e.g. a newly added station).

```json
{
  "intervalMinutes": 15,
  "timestamps": ["2026-04-03T09:00:00Z", "2026-04-03T09:15:00Z"],
  "rows": [
    ["smoove:0001", 5, 4],
    ["smoove:0002", 3, 2]
  ]
}
```

### `GET /api/open-data` → `OpenDataTimeSeries[]`

Each element in the array is a rolling time series for one configured source. A value of `-1` indicates the source was unavailable (e.g. out of season or a transient fetch failure).

`unit` and `description` are optional and let the frontend render the value generically across heterogeneous source types (visitor counts, temperatures, percentages, etc.) without per-source code. Both are refreshed from configuration on every successful poll.

```json
[
  {
    "sourceId": "uimastadion",
    "displayName": "Uimastadion",
    "lat": 60.1857,
    "lon": 24.9282,
    "attributionUrl": "https://helsinki.jaskaretail.com/current-fill-level/info/uimastadion",
    "unit": "visitors",
    "description": "Live visitor count at Uimastadion outdoor swimming pool",
    "timestamps": ["2026-05-18T10:00:00Z", "2026-05-18T10:15:00Z"],
    "values": [185, 192]
  }
]
```

### `GET /api/stations/{stationId}/statistics` → `MonthlyStationStatistics`

Demand arrays contain 24 integers, one per hour of the day. The destinations table uses a columnar layout.

```json
{
  "month": "2026-04",
  "demand": {
    "departuresByHour": [0, 0, 0, 0, 0, 0, 0, 0, 12, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    "arrivalsByHour": [0, 0, 0, 0, 0, 0, 0, 0, 8, 14, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    "weekdayDeparturesByHour": [0, 0, 0, 0, 0, 0, 0, 0, 12, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    "weekendDeparturesByHour": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    "weekdayArrivalsByHour": [0, 0, 0, 0, 0, 0, 0, 0, 8, 14, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    "weekendArrivalsByHour": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
  },
  "destinations": {
    "fields": ["arrivalStationId", "tripCount", "averageDurationSeconds", "averageDistanceMetres"],
    "rows": [
      ["smoove:0023", 42, 361, 1250]
    ]
  }
}
```

## Project layout

```
HslBikeDataAggregator.slnx
├── src/HslBikeDataAggregator/
│   ├── Configuration/       Options classes (PollStationsOptions, HistoryProcessingOptions, OpenDataOptions)
│   ├── Functions/            HTTP and timer Azure Functions
│   ├── Models/               Shared API/storage DTOs (records)
│   ├── Services/             Business logic (polling, history processing, caching)
│   ├── Storage/              Blob naming conventions and read/write helpers
│   ├── Program.cs            DI composition root
│   └── host.json             Functions host configuration
├── tests/HslBikeDataAggregator.Tests/
│   ├── Configuration/        Scaffold and deployment workflow config tests
│   ├── Functions/             Function-level integration tests
│   ├── Services/              Service-level unit tests
│   └── Storage/               Blob naming tests
├── infra/                     Bicep templates and parameter files
├── docs/adr/                  Architecture decision records
└── .github/workflows/         CI and deployment workflows
```

## Local development

### Prerequisites

- .NET SDK `10.0.201` or later (pinned in `global.json` with `latestFeature` roll-forward)
- Azure Functions Core Tools v4
- Azurite or another storage emulator for local blob storage

### Configuration

Local settings live in `src/HslBikeDataAggregator/local.settings.json` (git-ignored).
Copy `src/HslBikeDataAggregator/local.settings.example.json` to `local.settings.json` for local development.

Expected values:

| Setting | Description | Default |
|---|---|---|
| `AzureWebJobsStorage` | Storage connection string (use `UseDevelopmentStorage=true` for Azurite) | — |
| `FUNCTIONS_WORKER_RUNTIME` | Must be `dotnet-isolated` | `dotnet-isolated` |
| `DigitransitSubscriptionKey` | HSL Digitransit API subscription key | — |
| `PollIntervalCron` | NCRONTAB expression for `PollStations` | `0 */15 * * * *` |
| `SnapshotHistoryLimit` | Maximum number of snapshots retained in the time series blob | `60` |
| `HistoryProcessingCron` | NCRONTAB expression for `ProcessStationHistory` | `0 0 2 * * *` |
| `OpenDataPollIntervalCron` | NCRONTAB expression for `PollOpenData` | `0 */15 * * * *` |
| `OpenData:HistoryLimit` | Maximum number of values retained per open data source | `60` |
| `OpenData:VenueFillLevelSources:0:SourceId` | Source ID for the first venue fill level source | `uimastadion` |
| `OpenData:VenueFillLevelSources:0:*` | Additional fields per source: `DisplayName`, `Lat`, `Lon`, `AttributionUrl`, `LocationId`, `LocationUrlName`, optional `Unit`, optional `Description` | — |

In Azure, the storage connection uses managed identity (`AzureWebJobsStorage__accountName` + `AzureWebJobsStorage__clientId`) rather than a connection string.

### Build

```bash
dotnet build HslBikeDataAggregator.slnx --configuration Release
```

### Run tests

```bash
dotnet test HslBikeDataAggregator.slnx --configuration Release
```

The test project uses **xUnit v3** with **Moq** for mocking.

### Run locally

From `src/HslBikeDataAggregator`:

```bash
func start
```

## Azure deployment

Deployment is split into `dev` and `prod` environments.

- Pull requests run **CI only** (build, test, Bicep validation).
- Pushes to `main` deploy automatically to the **dev** Azure environment.
- Production deploys run manually through the **Deploy prod** workflow (restricted to the `main` branch) and should be protected with a GitHub environment approval.

### Infrastructure

Azure infrastructure is defined in `infra/main.bicep` with environment parameter files:

- `infra/dev.bicepparam`
- `infra/prod.bicepparam`

The template provisions:

- **Flex Consumption hosting plan** (FC1) — scale-to-zero, pay-per-execution
- **User-assigned managed identity** for secure, passwordless resource access
- **Azure Function App** (Linux, .NET 10 isolated worker, identity-managed storage)
- **Storage account** (shared-key access disabled, Blob Data Owner + Queue Data Contributor RBAC for the managed identity)
- **Application Insights** backed by a Log Analytics workspace
- **Storage diagnostic settings** (blob read/write/delete logs and transaction metrics)
- **Azure API Management** Consumption instance with:
  - API operations for all HTTP endpoints
  - Inbound policy: function key injection, CORS (preflight cached for 1 hour), global rate limiting (200 req/min)

### Resource provider prerequisites

Before the first deployment, ensure the required resource providers are registered on the target subscription:

```bash
az provider register --namespace Microsoft.ApiManagement --wait
az provider show --namespace Microsoft.ApiManagement --query registrationState -o tsv
```

Registration is a one-time operation per subscription. Deployment will fail with a "subscription is not registered" error if this step is skipped.

### GitHub environments

Create two GitHub environments: **dev** and **prod**.

#### Environment variables

| Variable | Description | Example (`dev`) | Example (`prod`) |
|---|---|---|---|
| `AZURE_LOCATION` | Azure region | `northeurope` | `northeurope` |
| `AZURE_RESOURCE_GROUP` | Target resource group | `rg-hsl-bike-data-aggregator-dev` | `rg-hsl-bike-data-aggregator-prod` |
| `AZURE_FUNCTION_APP_NAME` | Function App name | `func-hsl-bike-data-aggregator-dev-flex` | `func-hsl-bike-data-aggregator-prod-flex` |
| `AZURE_APIM_SERVICE_NAME` | API Management instance name | `apim-hsl-bike-data-aggregator-dev` | `apim-hsl-bike-data-aggregator-prod` |
| `HISTORY_PROCESSING_CRON` | (Optional) Override daily processing schedule | `0 0 2 * * *` | `0 0 2 * * *` |

#### Environment secrets

| Secret | Description |
|---|---|
| `AZURE_CLIENT_ID` | Entra application (service principal) client ID |
| `AZURE_TENANT_ID` | Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `DIGITRANSIT_SUBSCRIPTION_KEY` | HSL Digitransit API key |
| `OPEN_DATA_POLL_INTERVAL_CRON` | (Optional) Override open data poll schedule |

### Azure authentication

GitHub Actions uses Azure OpenID Connect federation via `azure/login`. This avoids publish profiles and long-lived client secrets.

One-time Azure setup:

1. Create an Entra application or service principal for GitHub Actions.
2. Add a federated credential for this repository and the target GitHub environment.
3. Pre-create the target resource group (e.g. `rg-hsl-bike-data-aggregator-prod`).
4. Grant the identity **Contributor** and **Role Based Access Control Administrator** on the target resource group.
   - The `Role Based Access Control Administrator` role is required so the Bicep deployment can assign RBAC roles to the managed identity (e.g. `Storage Blob Data Owner`).

### Workflows

| Workflow | File | Trigger | Purpose |
|---|---|---|---|
| CI | `.github/workflows/ci.yml` | Push/PR to `main` | Build, test, validate Bicep templates |
| Deploy dev | `.github/workflows/deploy-dev.yml` | Push to `main` | Deploy infrastructure and app code to `dev` |
| Deploy prod | `.github/workflows/deploy-prod.yml` | Manual (`workflow_dispatch`) | Deploy infrastructure and app code to `prod` |

Each deploy workflow: restores → builds → tests → publishes → signs in to Azure → creates/updates the resource group → purges any soft-deleted APIM instance → deploys Bicep infrastructure → configures app settings → deploys the Function App zip package.

## Architecture decision records

| ADR | Title |
|---|---|
| [001](docs/adr/001-cold-start-mitigation.md) | Cold start mitigation via write/read separation |
| [002](docs/adr/002-runtime-model.md) | Use Azure Functions isolated worker model |
| [003](docs/adr/003-apim-gateway.md) | Add Azure API Management gateway in front of HTTP endpoints |
| [004](docs/adr/004-open-data-polling-framework.md) | Generic open data polling framework |

## Issue delivery workflow

- Keep code changes linked to an open GitHub issue.
- Use an issue branch named `issue-<number>-<short-description>`.
- If an issue was closed before the code was pushed, reopen the issue before continuing.
- Add or update automated tests for delivered behaviour or repository configuration changes.
- Run `dotnet build HslBikeDataAggregator.slnx` and the relevant tests before treating the issue as complete.
- Do not consider an issue done until the branch is pushed, the pull request is open, and CI is passing.
- Explicitly link pull requests to their GitHub issue using closing keywords such as `Closes #<issue>` to ensure the issue is automatically closed when the PR is merged.
