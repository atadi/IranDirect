# Phase 32.7 — Prefix Update and DNS Telemetry

Instruments two network workflows with OpenTelemetry-compatible `Activity`
tracing (via `System.Diagnostics`) and `Metrics` — no OpenTelemetry packages, no
exporters, no hosting or `appsettings` changes:

- **A. Official prefix update checking** — `OfficialIranPrefixUpdateChecker.CheckAsync`.
- **B. Custom-route DNS cache and resolution** — `CustomRouteResolver.ResolveAsync`.

This is the fifth telemetry slice. Both workflows are instrumented at their
narrowest real owner; persistence (the separate update/download + persist flow in
the controller / `PrefixSourceUpdateHistoryService`) is deliberately *not*
re-instrumented here, so no `Prefix.PersistMetadata` span is emitted by this
slice.

## Prefix workflow scope — persistence boundary

`OfficialIranPrefixUpdateChecker.CheckAsync` does **not** own metadata
persistence. It only:

- reads the *current* local metadata via
  `IPrefixSourceMetadataService.GetCurrentAsync` (a read, not a write); and
- probes the remote source (HEAD, optional GET fallback) and compares the
  remote comparison metadata (`ETag` / `Last-Modified` / `Content-Length`)
  against the current metadata.

It never writes metadata, never calls a persist/update method, and never
references `JsonStore` directly. The separate fetch + persist flow lives in the
controller (`UpdatePrefixesAsync`) and `PrefixSourceUpdateHistoryService`, which
are outside this checker.

Therefore:

- **`Prefix.PersistMetadata` is intentionally NOT emitted.** The approved child
  is absent because no persistence occurs inside the instrumented owner. Emitting
  a persist child here would require instrumenting a boundary (the persistence
  service / `JsonStore`) that the checker does not own, which the Phase 32.7
  spec forbids ("Do not instrument JsonStore directly").
- The only children emitted by the checker are `Prefix.HttpHead`,
  `Prefix.HttpGet` (fallback only), and `Prefix.Compare` (only when comparison
  metadata is present). These cover the entire real surface of the check.

## Hierarchy

```text
IranDirect.PrefixUpdateCheck       (root)
├── Prefix.HttpHead                 (child, when HEAD is attempted)
├── Prefix.HttpGet                  (child, only on HEAD-unsupported fallback)
└── Prefix.Compare                  (child, only when remote comparison metadata exists)

IranDirect.CustomRouteRefresh      (root)
├── Dns.CacheRead                   (child, one per resolve)
├── Dns.Resolve                     (child, one per real DNS lookup attempt)
└── Dns.CacheWrite                  (child, one per resolve)
```

The prefix check and the DNS refresh are each an independent root Activity — they
are not nested under `Runtime.Execute`. They are triggered by the prefix monitor /
custom-route resolver on their own schedules, not by the runtime reconciliation
cycle. A child Activity may become a root if observed outside its normal caller —
no fake parent is manufactured.

Per the architecture test `PlannerAndModelNamespaces_DoNotReferenceTelemetry`, the
scanned namespaces are only `Runtime/Reconciliation`, `Runtime/Execution`, and
`Routing`. `Prefixes` and `CustomRoutes` are **outside** the scan, so both
workflows are instrumented directly (no decorator), keeping the production code
the single source of truth and the telemetry helpers as thin, failure-isolated
wrappers.

## Roots and owners

| Workflow | Root Activity | Owner (narrowest real) |
| --- | --- | --- |
| Prefix update check | `IranDirect.PrefixUpdateCheck` | `OfficialIranPrefixUpdateChecker.CheckAsync` |
| Custom-route DNS refresh | `IranDirect.CustomRouteRefresh` | `CustomRouteResolver.ResolveAsync` |

A single `StartCheck()` / `StartRefresh()` call opens the root; every exit path
(completion, exception, cancellation, timeout) routes through a terminal
`Complete*` that sets `outcome` and (on failure) `failure-category` exactly once
via an `Interlocked.Exchange` guard — a crashed/un-disposed scope still records an
`Unknown` outcome on `Dispose`.

## Activities and instrumentation

### Prefix update check
- Root tags: `operation=prefix_update_check`, `source=official`, `trigger`
  (scheduled/forced/startup/repair/unknown — default `unknown` since the checker
  API does not yet surface a trigger).
- Children (all optional, all carry only `operation` + `outcome` or failure tags):
  - `Prefix.HttpHead` — HEAD probe.
  - `Prefix.HttpGet` — only on `405 Method Not Allowed` / `501 Not Implemented`
    HEAD response (fallback to GET).
  - `Prefix.Compare` — only when the remote response exposes comparison metadata
    (`ETag` / `Last-Modified` / `Content-Length`); otherwise omitted (a bare 200
    with no metadata still completes the check successfully without a Compare
    child).
- `irandirect.prefix.checks` counter — +1 at start.
- `irandirect.prefix.check.duration` histogram — sampled once at completion.

### Custom-route DNS refresh
- Root tags: `operation=custom_route_refresh`, `source=custom`.
- Children:
  - `Dns.CacheRead` (`source=cache`, `cache_state`) — one per resolve.
  - `Dns.Resolve` (`source=custom`) — one per real DNS lookup attempt (fresh-cache
    hits do **not** open a resolve child, so no lookup work is counted). On stale
    fallback the cached entry is served while the refresh writes the new value.
  - `Dns.CacheWrite` (`source=cache`, `cache_state`, `outcome`) — one per resolve;
    also wraps the failure-cache upsert.
- `irandirect.dns.lookups` counter — +1 per real lookup attempt (success **and**
  failure — the lookup counter counts attempts, not wins).
- `irandirect.dns.lookup.duration` histogram — sampled once per real lookup.

The `Dns.Resolve` child is opened only when `_dnsLookup` is actually invoked, and
its duration is measured from child start to `CompleteLookupSuccess`/
`CompleteLookupFailure`. This keeps the per-lookup histogram honest and avoids
double counting when the cache absorbs the hit.

## Bounded tags (no sensitive values)

Approved tag keys and value domains:

- `operation` — `prefix_update_check`, `head`, `get`, `compare`,
  `custom_route_refresh`, `cache_read`, `lookup`, `cache_write`.
- `source` — `official`, `custom`, `cache`.
- `outcome` — `success`, `no_change`, `failure`, `cancelled`, `unknown`.
- `cache_state` — `fresh`, `stale`, `expired`, `failed`, `missing`, `disabled`
  (derived via `TelemetryOutcomeMapper.Map(CustomRouteDnsCacheState)`).
- `trigger` — `scheduled`, `forced`, `startup`, `repair`, `unknown`.
- `failure_category` — `io`, `timeout`, `routing`, `dns`, `http`, `unknown`.
- `Prohibited` names (URLs, domains, IPs, prefixes, file paths, cache keys,
  response bodies, exception messages) are never attached — enforced by
  `PrefixUpdateTelemetry_OnlyApprovedTags_NoProhibited`,
  `CustomRouteRefreshTelemetry_OnlyApprovedTags_NoProhibited`, and the
  `PrefixAndDnsDoNotReferenceSensitiveTags` architecture test.

The `trigger` value is mapped locally (only the five known members are handled;
anything else falls back to `unknown`). No `TelemetryTrigger.Cli`/`Tray` exist, so
they are intentionally not referenced.

## Failure isolation and no-listener behavior

Both helpers are null-safe: `ActivitySource.StartActivity` returns `null` when no
listener is attached, and every scope method tolerates a null `Activity`. The
production call sites (`OfficialIranPrefixUpdateChecker`, `CustomRouteResolver`)
were verified unchanged in behavior — the existing 72 prefix/DNS tests still pass
with no listener present. `NoListener_BehaviorUnchanged` tests prove the resolve
and check flows complete normally (and return the same results) with no
`ActivityListener` attached.

## Privacy guarantees

- No URL, domain, IP address, prefix, file path, cache key, ETag value,
  `Last-Modified` value, `Content-Length` value, or HTTP response body is attached
  to any Activity or metric.
- Children carry only bounded `operation` / `outcome` / `source` / `cache_state`
  tags.
- The DNS refresh emits **no per-address span** — `MultipleDomains_...` asserts
  every emitted Activity is one of the four known DNS activity names, proving the
  absence of per-domain spans.

## Tests

- `PrefixUpdateTelemetryTests` — behavior (current / update-available / unknown /
  failed), HEAD-only vs GET-fallback, comparison child emission only with metadata,
  cancellation, fault injection, no-listener, privacy (no URLs), trigger tag.
- `CustomRouteRefreshTelemetryTests` — fresh cache (one cache-read, no lookup),
  stale refresh (cache-read + resolve + cache-write, `dns.lookups`=1), miss
  (lookup + write), multi-domain (one lookup per domain, no per-address span), DNS
  failure (timeout → `failure_category=timeout`, lookup counter still increments),
  stale fallback (lookup fails but usable stale data is served → `Dns.Resolve`
  child is `Error`, root is `Ok`, `cache_state=stale`, `dns.lookups`=1 and no
  duplicate failed-workflow metric), no-listener, lookup counter + duration
  histogram.
- `TelemetryArchitectureTests` — exactly one root-location per workflow (no
  per-prefix / per-address spans), approved instrument counts, approved activity
  constants, approved-only tags, and `PrefixAndDnsUseOnlyApprovedInstruments`.

## Files

- `IranDirect.Core/Observability/Telemetry/PrefixUpdateTelemetry.cs` (new) — root +
  child scopes, counter + histogram.
- `IranDirect.Core/Observability/Telemetry/CustomRouteRefreshTelemetry.cs` (new) —
  root + child scopes, one lookup counter + one lookup duration histogram.
- `IranDirect.Core/Observability/Telemetry/IranDirectMetricNames.cs` — added
  `DnsLookups`, `DnsLookupDuration` (already-present approved names used here).
- `IranDirect.Core/Prefixes/OfficialIranPrefixUpdateChecker.cs` — root + child
  scopes wired into `CheckAsync` / `ProbeRemoteAsync` / `FetchRemoteMetadataAsync`.
- `IranDirect.Core/CustomRoutes/CustomRouteResolver.cs` — root + child scopes wired
  into `ResolveAsync` / `ResolveDomainsAsync` / `ResolveDomainCoreAsync` /
  `RecordDnsFailureAsync`; added `CacheStateOf` helper.
- Tests as above; `TelemetryNameCatalogTests` and `TelemetryArchitectureTests`
  extended.
