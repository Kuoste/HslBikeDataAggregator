# HslBikeDataAggregator

C# Azure Functions backend for Helsinki city bike data aggregation.

This service polls the HSL Digitransit API, stores aggregated bike station data in Azure Blob Storage, and serves read-optimized JSON endpoints for `HslBikeApp`.

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

## Issue delivery workflow

- Keep code changes linked to an open GitHub issue.
- Use an issue branch named `issue-<number>-<short-description>`.
- If an issue was closed before the code was pushed, reopen the issue before continuing.
- Add or update automated tests for delivered behavior or repository configuration changes.
- Run `dotnet build HslBikeDataAggregator.slnx` and the relevant tests before treating the issue as complete.
- Do not consider an issue done until the code is committed on the issue branch and ready to merge.

## Next milestone

The next implementation step is issue `#2`: connect `PollStations` to the HSL Digitransit API, persist the latest station list and rolling snapshots to Blob Storage, and then wire the HTTP functions to real stored data.
