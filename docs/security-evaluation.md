# Security Evaluation — HslBikeDataAggregator

**Date:** 2025-07-17
**Scope:** Azure Functions service, Bicep infrastructure, GitHub Actions CI/CD

---

## Strengths

| Area | Detail |
|---|---|
| **OIDC authentication** | Deploy workflows use federated credentials (`azure/login` with `client-id` / `tenant-id` / `subscription-id`) — no long-lived secrets stored in GitHub. |
| **Minimal workflow permissions** | CI and deploy workflows declare `contents: read` and only add `id-token: write` where needed. |
| **Concurrency guards** | Deploy workflows use `concurrency.cancel-in-progress: false` preventing mid-flight overwrite. |
| **Prod gating** | `deploy-prod.yml` is manual (`workflow_dispatch`) with a separate `prod` environment, suitable for approval rules. |
| **TLS enforcement** | Bicep sets `httpsOnly: true`, `minTlsVersion: '1.2'`, `ftpsState: 'Disabled'`. |
| **Blob public access disabled** | `allowBlobPublicAccess: false` on the storage account. |
| **Secret isolation** | `DigitransitSubscriptionKey` is never exposed to the frontend; it is injected at deploy time from a GitHub environment secret. |
| **Flex Consumption deployment storage** | Deployment packages are stored in a dedicated blob container (`deployment-packages`) via `functionAppConfig.deployment.storage`, making the deployed artefact read-only and eliminating the Azure Files content share. |
| **Input validation** | `BikeDataBlobStorage` validates `stationId` with `ArgumentException.ThrowIfNullOrWhiteSpace`. |

---

## Critical findings

### 1. Storage account uses shared key access — not Managed Identity

**File:** `infra/main.bicep`

The Bicep template builds a connection string from `storageAccount.listKeys()` and injects it into `AzureWebJobsStorage`. The same connection string is used for Flex Consumption deployment storage authentication (`functionAppConfig.deployment.storage.authentication`). The Function App already has a System-Assigned Managed Identity, but it is not used for blob or deployment storage access.

> **Note (Flex Consumption migration):** The `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` setting and Azure Files content share have been eliminated — Flex Consumption uses dedicated blob storage for deployment packages instead.

**Risk:** A leaked connection string grants full account-level access. The `listKeys()` output is also visible in ARM deployment logs.

**Recommendation:**

- Set `allowSharedKeyAccess: false` on the storage account.
- Assign the `Storage Blob Data Contributor` and `Storage Account Contributor` roles to the Function App's managed identity.
- Use `AzureWebJobsStorage__accountName` (identity-based connection) instead of the full connection string.
- In application code, replace `new BlobContainerClient(connectionString, …)` with `new BlobContainerClient(new Uri(…), new DefaultAzureCredential())`.
- Switch Flex Consumption deployment storage authentication from `StorageAccountConnectionString` to `SystemAssignedIdentity`.

### 2. Configurable `TripHistoryUrlPattern` — potential SSRF

**File:** `src/HslBikeDataAggregator/Configuration/HistoryProcessingOptions.cs`

`TripHistoryUrlPattern` can be overridden via app settings. `ProcessStationHistoryService` uses `HttpClient.GetAsync(…)` against the formatted URL with no allow-list or scheme/host validation.

**Risk:** If an attacker gains write access to app settings (e.g. via a compromised deployment principal), they can redirect the service to hit internal Azure endpoints (IMDS at `169.254.169.254`, internal VNet resources) or exfiltrate data.

**Recommendation:**

- Validate the resolved URL in `BuildTripHistoryUrl` against an allowed host (`dev.hsl.fi`) and scheme (`https`).
- Consider hardcoding the base URL and only allowing the date portion to vary.

---

## High findings

### 3. HTTP Functions use `AuthorizationLevel.Anonymous` — no rate limiting or authentication

**File:** `src/HslBikeDataAggregator/Functions/StationsFunctions.cs`

All four HTTP endpoints are anonymous. There is no API key requirement, no JWT validation, and no Azure-level rate limiting.

**Risk:** Anyone can enumerate station data and abuse the endpoints, potentially inflating Flex Consumption per-execution costs.

**Recommendation:**

- Consider `AuthorizationLevel.Function` for read endpoints — the frontend can include the function key in requests.
- Alternatively, place Azure API Management or Azure Front Door in front of the Function App with rate-limiting and geo-filtering.
- At a minimum, consider response caching headers (`Cache-Control`) to limit upstream hits.

### 4. ~~Duplicated CORS header may conflict with platform CORS~~ — RESOLVED

**File:** `src/HslBikeDataAggregator/Functions/StationsFunctions.cs`

~~The function manually added `Access-Control-Allow-Origin` while the Bicep template also configured CORS at the platform level (`siteConfig.cors`). If both fired, the browser could receive duplicate headers.~~

**Resolution:** Manual CORS header injection removed from `CreateJsonResponseAsync`. The service now relies solely on the platform CORS configuration in `siteConfig.cors`, which supports multiple origins (production and localhost for development).

### 5. CI does not pin action versions to commit SHAs

**Files:** `.github/workflows/ci.yml`, `deploy-dev.yml`, `deploy-prod.yml`

Actions are pinned to `@v4` / `@v2` / `@v1` tags, not to immutable commit SHAs.

**Risk:** A compromised upstream action repository could push a malicious change to a mutable tag (tag re-pointing attack), executing arbitrary code in your workflow.

**Recommendation:** Pin to full commit SHAs, e.g.:

```yaml
- uses: actions/checkout@b4ffde65f46336ab88eb53be808477a3936bae11 # v4.1.7
```

### 6. Prod workflow accepts arbitrary `git-ref` input

**File:** `.github/workflows/deploy-prod.yml`

Any user with `workflow_dispatch` permission can deploy an unreviewed branch (or even a SHA from a fork PR) to production.

**Recommendation:**

- Restrict to only allow tags matching a semver pattern, or limit to `main`.
- Alternatively, validate the ref is an ancestor of `main` before proceeding.

---

## Medium findings

### 7. No Dependabot or dependency scanning configured

There is no `dependabot.yml` or GitHub Advanced Security (CodeQL) workflow. NuGet and GitHub Actions dependencies are not automatically monitored.

**Recommendation:** Add `.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
```

### 8. No `stationId` path-traversal guard in blob name construction

**File:** `src/HslBikeDataAggregator/Storage/BikeDataBlobNames.cs`

```csharp
public static string AvailabilityProfile(string stationId) => $"availability/{stationId}.json";
public static string DestinationProfile(string stationId) => $"destinations/{stationId}.json";
```

While `ArgumentException.ThrowIfNullOrWhiteSpace` checks for empty strings, a `stationId` of `../../secrets` would produce a valid blob path that escapes the intended prefix.

**Recommendation:** Sanitise `stationId` to allow only alphanumeric characters, hyphens, and underscores:

```csharp
private static string SanitiseStationId(string stationId)
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(stationId, @"^[\w\-]+$"))
        throw new ArgumentException("Station ID contains invalid characters.", nameof(stationId));
    return stationId;
}
```

### 9. Application Insights does not enforce workspace-based configuration

**File:** `infra/main.bicep`

The Application Insights resource uses the classic (non-workspace) mode. Microsoft is retiring classic Application Insights resources.

**Recommendation:** Add a Log Analytics workspace and set `WorkspaceResourceId` on the `Microsoft.Insights/components` resource.

### 10. No diagnostic / audit logging on the storage account

The Bicep template does not enable storage diagnostic settings (read/write/delete logging). If the storage account is compromised, there is no audit trail.

**Recommendation:** Add a `Microsoft.Insights/diagnosticSettings` resource on the storage account forwarding to the Log Analytics workspace.

---

## Summary

| # | Severity | Finding | Category |
|---|---|---|---|
| 1 | Critical | Storage access via shared key, not Managed Identity | Infrastructure |
| 2 | Critical | Configurable `TripHistoryUrlPattern` — SSRF risk | Application |
| 3 | High | Anonymous HTTP endpoints with no rate limiting | Application |
| 4 | ~~High~~ | ~~Duplicate CORS headers~~ — **Resolved** | Application |
| 5 | High | GitHub Actions pinned to mutable tags | CI/CD |
| 6 | High | Prod deploy accepts arbitrary `git-ref` | CI/CD |
| 7 | Medium | No Dependabot / dependency scanning | CI/CD |
| 8 | Medium | No `stationId` path-traversal sanitisation | Application |
| 9 | Medium | Classic Application Insights (retiring) | Infrastructure |
| 10 | Medium | No storage account diagnostic logging | Infrastructure |

The most impactful improvement would be **switching storage access to Managed Identity (#1)** — it eliminates the largest secret surface in the system. The **SSRF vector (#2)** should be closed in the same pass since it requires only a small allow-list validation.
