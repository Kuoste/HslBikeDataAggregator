# HslBikeDataAggregator — Copilot Instructions

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
| `PollStations` | Timer (every 2-5 min) | Fetch live bike data from Digitransit, store snapshot |
| `GetStations` | HTTP GET | Return latest station availability |
| `GetStationAvailability` | HTTP GET | Return aggregated hourly availability profile for a station |
| `GetStationDestinations` | HTTP GET | Return popular destinations from HSL open history data |
| `GetSnapshots` | HTTP GET | Return recent snapshots for trend calculation |

### Storage

- Azure Blob Storage for aggregated data (snapshots, hourly profiles, destination data).
- Data format: JSON blobs, one per station for availability profiles.

### API Gateway

- Azure API Management (Consumption tier) sits in front of all HTTP endpoints.
- APIM enforces global rate limiting (200 req/min) and response caching. Consumption tier does not support per-IP by-key policies, and the quota policy is restricted to product scope.
- APIM injects the Function App host key — HTTP functions use `AuthorizationLevel.Function`, so direct calls without the key are rejected.
- The Blazor frontend calls the APIM gateway URL, never the Function App URL directly.

## API Contract

- `GET /api/stations` — current bike availability for all stations
- `GET /api/stations/{id}/availability` — aggregated hourly availability (24 buckets)
- `GET /api/stations/{id}/destinations` — top destinations by trip count
- `GET /api/snapshots` — last N snapshots for trend arrows

All endpoints return JSON. Requests are routed through the APIM gateway, which handles CORS, rate limiting, and function key injection. Direct calls to the Function App require a valid function key.

## Secrets

- `DigitransitSubscriptionKey` — stored in Azure Functions app settings, NEVER exposed to frontend.
- `AzureWebJobsStorage` — connection string for blob storage.

## Key Models (compatible with HslBikeApp)

- `BikeStation` — id, name, lat/lon, capacity, bikesAvailable, spacesAvailable, isActive
- `StationSnapshot` — timestamp + dictionary of stationId -> bikeCount
- `StationHistory` — departure/arrival station pair, tripCount, avg duration/distance
- `HourlyAvailability` — hour (0-23) + average bike count for a station

## Conventions

- Azure Functions isolated worker model (.NET 10).
- Records for data models, classes for services.
- File-scoped namespaces, nullable enabled, implicit usings.
- Return empty collections on failure, never null.
- Use `ReadFromJsonAsync<T>()` / `System.Text.Json` for serialisation.
- Use British English consistently in identifiers, including function, method, variable, parameter, and local naming where practical, while preserving required external API, framework, library, and contract names.

## Delivery Workflow

- Keep implementation work tied to an open GitHub issue.
- Use an issue branch named `issue-<number>-<short-description>` for delivery.
- If an issue was closed before its code was pushed, reopen the issue before continuing work.
- Add or update automated tests for each delivered behaviour or repository-level configuration change.
- Run `dotnet build HslBikeDataAggregator.slnx` and the relevant tests before considering the issue complete.
- Do not treat an issue as done until the branch is pushed, the pull request is open, and CI is passing.
- Explicitly link pull requests to their GitHub issue using closing keywords such as `Closes #<issue>` to ensure the issue is automatically closed when the PR is merged.

## Language Preferences

- Use British English consistently in responses, code comments, documentation, commit and pull request text, and GitHub content.
- Avoid non-English or stray foreign text in responses.

## Domain Logic Considerations

- Station stock changes are influenced not only by rider journeys but also by bike rebalancing vans that move bikes from full stations to empty ones. 
- Treat snapshot-based flow metrics separately from trip-history demand metrics to ensure accurate data representation.

