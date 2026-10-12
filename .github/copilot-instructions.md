# HslBikeDataAggregator — Copilot Instructions

> **This file is the canonical source of instructions for this repository.** `CLAUDE.md` at the repository root imports this file — edit here, not in `CLAUDE.md`.

## System Overview

Helsinki city bike data backend. Part of a two-repo system:

| Repo | Role | Tech | Hosting |
|---|---|---|---|
| **HslBikeApp** | Blazor WASM frontend | .NET 10, Blazor WebAssembly, Leaflet.js | GitHub Pages |
| **HslBikeDataAggregator** (this repo) | C# backend service | .NET 10, Azure Functions (isolated worker), Azure API Management | Azure Functions (Flex Consumption) |

## Architecture

This service is the **only component** that holds the HSL Digitransit API key. The Blazor frontend calls this service's REST API — it never calls HSL directly.

### Functions

| Function | Trigger | Purpose |
|---|---|---|
| `PollStations` | Timer (`%PollIntervalCron%`, default every 15 min) | Fetch live bike data from Digitransit, append snapshot to rolling time series blob |
| `ProcessStationHistory` | Timer (`%HistoryProcessingCron%`, default 02:00 UTC daily) | Fetch newest HSL open-data trip CSV, aggregate per-station monthly statistics |
| `PollOpenData` | Timer (`%OpenDataPollIntervalCron%`, default every 15 min) | Poll configured external open data sources, append value to per-source rolling time series blob |
| `GetStations` | HTTP GET `/api/stations` | Return latest station availability (via in-memory cache, falls back to Digitransit) |
| `GetSnapshots` | HTTP GET `/api/snapshots` | Return rolling snapshot time series for trend arrows |
| `GetStationStatistics` | HTTP GET `/api/stations/{id}/statistics` | Return monthly demand profile and top destinations for a station |
| `GetOpenData` | HTTP GET `/api/open-data` | Return rolling time series for all configured open data sources |

### Storage

- Azure Blob Storage for all aggregated data (rolling snapshot time series, monthly station statistics, open data time series).
- Data format: compact JSON blobs optimised for frontend reads.
- Blob paths: `bike-data/snapshots/recent.json`, `bike-data/monthly-stats/{stationId}.json`, `open-data/{sourceId}/recent.json`.

### API Gateway

- Azure API Management (Consumption tier) sits in front of all HTTP endpoints.
- APIM enforces global rate limiting (200 req/min). Consumption tier does not support per-IP by-key policies or the built-in response cache, so do not add `cache-lookup` / `cache-store` policies.
- APIM injects the Function App host key — HTTP functions use `AuthorizationLevel.Function`, so direct calls without the key are rejected.
- The Blazor frontend calls the APIM gateway URL, never the Function App URL directly.

## API Contract

- `GET /api/stations` — current bike availability for all stations
- `GET /api/snapshots` — compact rolling snapshot time series for trend arrows
- `GET /api/stations/{id}/statistics` — monthly demand profile and top destinations
- `GET /api/open-data` — rolling time series for all configured open data sources (e.g. venue fill levels)

All endpoints return JSON. Requests route through the APIM gateway which handles CORS, rate limiting, and function key injection.

## Configuration and Secrets

| Setting | Where | Description |
|---|---|---|
| `DigitransitSubscriptionKey` | GitHub secret / Azure app setting | HSL Digitransit API key — **never** exposed to the frontend |
| `AzureWebJobsStorage` | Azure app setting (managed identity) | Blob storage access via `AzureWebJobsStorage__accountName` + `clientId` |
| `PollIntervalCron` | Bicep / app setting | NCRONTAB for `PollStations` (default `0 */15 * * * *`) |
| `HistoryProcessingCron` | GitHub secret / app setting | NCRONTAB for `ProcessStationHistory` (default `0 0 2 * * *`) |
| `OpenDataPollIntervalCron` | GitHub secret / app setting | NCRONTAB for `PollOpenData` (default `0 */15 * * * *`) |
| `OpenData:VenueFillLevelSources:*` | Azure app setting (double-underscore notation) | List of venue fill level source configs |

## Key Models (compatible with HslBikeApp)

- `BikeStation` — id, name, lat/lon, capacity, bikesAvailable, spacesAvailable, isActive
- `SnapshotTimeSeries` — intervalMinutes, timestamps[], rows[] (columnar: stationId + int counts)
- `MonthlyStationStatistics` — month, demand profile, destination table
- `OpenDataTimeSeries` — sourceId, displayName, lat, lon, attributionUrl, optional unit, optional description, timestamps[], values[] (double; `-1` = unavailable)
- `IOpenDataSource` — interface for pluggable open data source implementations; `FetchAsync` returns `double?` (`null` = out of season/unavailable)

## Conventions

- Azure Functions isolated worker model (.NET 10).
- Records for data models, classes for services.
- File-scoped namespaces, nullable enabled, implicit usings.
- Return empty collections on failure, never null from services; `null` is acceptable for optional single values (e.g. statistics not yet available).
- Use `ReadFromJsonAsync<T>()` / `System.Text.Json` for serialisation.
- Use British English consistently in identifiers, including function, method, variable, parameter, and local naming where practical, while preserving required external API, framework, library, and contract names.
- `-1` sentinel in time series arrays signals a missing or unavailable value (consistent across snapshots and open data).

## Delivery Workflow

- Always start by pulling the latest from the remote (`git pull`) before beginning any work to prevent merge conflicts.
- Keep implementation work tied to an open GitHub issue.
- Use an issue branch named `issue-<number>-<short-description>` for delivery.
- If an issue was closed before its code was pushed, reopen the issue before continuing work.
- Add or update automated tests for each delivered behaviour or repository-level configuration change.
- Run `dotnet build HslBikeDataAggregator.slnx` and then `dotnet test HslBikeDataAggregator.slnx` before considering the issue complete.
- Do not treat an issue as done until the branch is pushed, the pull request is open, and CI is passing.
- Explicitly link pull requests to their GitHub issue using closing keywords such as `Closes #<issue>` to ensure the issue is automatically closed when the PR is merged.
- Continue to use Architecture Decision Records (ADRs) for significant architectural decisions.
- When reorganising files in this repository, use moves/renames that preserve Git history instead of recreating files at new paths.

## Language Preferences

- Use British English consistently in responses, code comments, documentation, commit and pull request text, and GitHub content.
- Avoid non-English or stray foreign text in responses.

## Corrections & Lessons Learned

### Configuration format

- Local `local.settings.json` uses colon notation (`OpenData:VenueFillLevelSources:0:SourceId`).
- Azure app settings and GitHub Actions `az functionapp config appsettings set` use double-underscore notation (`OpenData__VenueFillLevelSources__0__SourceId`).
- Both are valid for `IConfiguration` binding.

### VenueFillLevelSource response shape

- The jaskaretail fill level API returns `{ "result": { "fill_level": 185, ... }, "isError": false }`.
- The field is `fill_level` inside `result` — not a flat `currentAmount` field.
- Always verify actual API response shapes against `VenueFillLevelSource.cs` before assuming field names.

## Domain Logic Considerations

- Station stock changes are influenced not only by rider journeys but also by bike rebalancing vans that move bikes from full stations to empty ones.
- Treat snapshot-based flow metrics separately from trip-history demand metrics to ensure accurate data representation.
- Open data sources (e.g. Uimastadion fill level) may be seasonal — a `null` return from `IOpenDataSource.FetchAsync` and a `-1` sentinel in the stored time series are expected outside the operating season.
