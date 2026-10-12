# ADR-003: Add Azure API Management gateway in front of HTTP endpoints

## Status

Accepted — updated 2026-10-12 (response caching removed, CORS preflight caching added)

## Date

2026-04-08

## Context

All HTTP functions used `AuthorizationLevel.Anonymous` with no server-side traffic control. CORS is enforced by browsers but easily bypassed by scripts, bots, or CLI tools. Every invocation on the Flex Consumption plan incurs per-execution charges, so unrestricted public endpoints present a cost-abuse risk.

Embedding a function key in the Blazor WASM frontend was considered but rejected — the key would be visible in browser network requests, offering only obscurity rather than real protection.

## Decision

Place an Azure API Management (APIM) Consumption-tier instance in front of all HTTP-triggered functions.

### Traffic control

- **Global rate limiting** (`rate-limit`): 200 requests per minute across all callers. The Consumption tier does not support per-IP `rate-limit-by-key` (requires Developer tier or above), and the `quota` policy is restricted to product scope so cannot be applied at the API level.
- **Response caching** (`cache-lookup` / `cache-store`): 30 seconds for live endpoints (`/stations`, `/snapshots`), 3,600 seconds for slow-changing endpoints (`/availability`, `/destinations`).

### Function App lock-down

- HTTP triggers switched from `AuthorizationLevel.Anonymous` to `AuthorizationLevel.Function`.
- APIM injects the auto-generated Function App host key via a `set-header` inbound policy (`x-functions-key`).
- The host key is extracted at deployment time using `listKeys()` in Bicep and stored as a secret APIM Named Value — no Key Vault required.
- Direct callers without the key receive `401 Unauthorized`.

### Caching alignment

- APIM caches responses at the gateway layer, reducing Function App invocations.
- `Cache-Control` headers on `/stations` reduced from 120 s to 30 s (aligned with APIM cache TTL).
- `Cache-Control` on `/snapshots` (900 s), `/availability` and `/destinations` (3,600 s) remain unchanged — these serve longer-term trend data.
- The in-memory `LiveStationCacheService` (2 min TTL) is retained to protect the upstream Digitransit API independently of APIM.

## Alternatives considered

| Option | Outcome |
|---|---|
| **IP allow-list on Function App** | APIM Consumption tier does not have fixed outbound IPs — unreliable |
| **VNet integration** | Requires Premium APIM tier (~£230/month) — excessive for a hobby project |
| **Function key in frontend** | Key visible in browser DevTools — security by obscurity only |

## Consequences

### Positive

- Server-side cost protection — rejected requests never reach the Function App.
- All callers share a global rate limit; casual abusers are throttled without needing a higher-tier APIM SKU.
- Response caching reduces Function App invocations significantly for repeated requests.
- Fully automated via Bicep — no manual portal steps or secret management.
- APIM Consumption tier adds minimal cost (~£3/month).

### Negative

- Additional infrastructure component to maintain and monitor.
- APIM Consumption tier has a cold-start delay (~1-2 seconds) on idle periods.
- Rate limiting is global (not per-IP) on Consumption tier — a burst of legitimate traffic can exhaust the shared allowance.
- Frontend must be updated to call the APIM gateway URL instead of the Function App URL directly.

## Update 2026-10-12

- **Response caching removed.** The internal (built-in) APIM cache is not available in the Consumption tier, so the `cache-lookup` / `cache-store` policies above never cached anything (see [API Management caching overview](https://learn.microsoft.com/azure/api-management/caching-overview)). Gateway caching would require an external Redis cache, which is not justified for this workload. The policies were removed to avoid implying protection that does not exist; responses still carry `Cache-Control` headers for browser caching.
- **CORS preflight caching added.** The frontend's `X-App-Source` header makes every request a non-simple CORS request. `preflight-result-max-age="3600"` on `<allowed-methods>` lets browsers reuse the `OPTIONS` result instead of repeating it before each call.
