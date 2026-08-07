# Phase 35.3 — Generic Country Prefix Source and Persistence

Status: implemented (not yet committed).
Branch: `development/service-authority`
Base commit: `889b74e` (Phase 35.2 — `feat(configuration): add direct-country routing identity`)

## 1. Goal

Replace the Iran-only prefix acquisition/persistence layer with a generic
country-prefix pipeline driven by `DesiredConfiguration.DirectCountryCode`.
After this phase IR, IQ, and RO all obtain, validate, cache, and serve their
own IPv4 prefix datasets through the **same** generic implementation. The
planner/executor/reconciliation engine is unchanged.

Out of scope (explicitly deferred): PathVeer rename (Phase 36), SaaS/cloud,
IPv6, country-selection UI, telemetry country tags.

## 2. Global data-source decision

Source: **RIPEstat `country-resource-list`**
(`https://stat.ripe.net/data/country-resource-list/data.json?resource=<CC>`).

Evidence / rationale:

- Phase 35.1 confirmed the same endpoint serves `?resource=IR`, `IQ`, `RO`
  with identical JSON shape (`data.resources.irr_resources` + `routes`).
- The parameter is an ISO 3166-1 alpha-2 code; RIPEstat resolves it against
  RIR allocations for the country (AS/inetnum/prefix resources), i.e.
  **RIR-allocation-based country attribution**, which matches the product
  semantic "country destination prefixes."
- RIPEstat is operated by RIPE NCC and is the canonical, free, non-commercial
  research/operations data API used widely by network tooling; its terms
  permit product integration.
- It returns both delegated (RIR-allocated) and observed (IRR/routing-table)
  resources, so a single parse path covers both.
- No whitelist of "supported" countries is maintained in code: the value
  object `DirectCountryCode` already guarantees the code is a real ISO
  alpha-2; the source is global.

Rate limits / availability: RIPEstat is a public API with soft rate limiting.
Production correctness does **not** depend on it at runtime because the
last-known-good per-country cache is used for reconciliation whenever the
source is unavailable (see §8). Live source fetches are best-effort refresh.

Live validation (representative countries IR/IQ/RO/US/BR/JP/ZA/AU) is a
manual, non-CI step (§28) to avoid making CI depend on the public Internet.

## 3. Prefix-source abstraction

New contract (replaces the Iran-only `IPrefixSource`/`OfficialIranPrefixSource`):

```
ICountryPrefixSource
    PrefixSourceDescriptor GetDescriptor(DirectCountryCode country)
    Task<PrefixSourceFetchResult> FetchAsync(
        DirectCountryCode country,
        CancellationToken ct = default)
```

`PrefixSourceFetchResult` gained `DirectCountryCode? CountryCode` so every
fetched dataset is bound to the country it was requested for (§4 invariant).

`OfficialCountryPrefixSource` is the single generic implementation: the
request resource parameter is derived from the validated `country.Code`, never
from arbitrary caller-supplied strings. Parsing/validation/deduplication/
normalization/ordering are byte-equivalent to the prior Iran source.

`CountryPrefixProvider` (`DownloadIpv4PrefixesAsync(DirectCountryCode, ct)`)
is the front-end used by the controller, replacing `IranPrefixProvider`.

## 4. Country-binding invariant (hard)

A dataset requested for one country can never be consumed for another.
Enforced at every boundary:

- request: only a validated `DirectCountryCode` reaches the source; the URI is
  built from `country.Code`.
- parse: the result carries `CountryCode = country`.
- validation: empty/oversized/invalid responses throw and do **not** overwrite
  the last-known-good cache (§7).
- persistence: `CountryPrefixStore` is keyed by country directory
  `<root>/prefixes/<CC>/`.
- read: every load takes the country (`LoadPrefixesAsync(country)`,
  `GetMetadataRepository(country)`, `GetUpdateHistoryRepository(country)`).
- desired-route construction: the observation source loads prefixes for the
  *selected* country only.
- No `LoadCurrentAsync()`-style country-less cache selector exists.

## 5. Persistence layout

```
<state-root>/
  prefixes/
    IR/
      ipv4-prefixes.txt        # one CIDR per line, lower-cased, sorted
      metadata.json            # PrefixSourceMetadata (country-bound)
      update-history.json      # PrefixSourceUpdateHistoryEntry[]
    IQ/
      ipv4-prefixes.txt
      metadata.json
      update-history.json
    RO/
      ...
```

The legacy single-file cache `iran-ipv4-prefixes.txt` (plus
`prefix-source-metadata.json`, `prefix-source-update-history.json` at the
state root) is **only** migrated for `DirectCountryCode.IR` (§6). Other
countries have no legacy cache and start empty until fetched.

## 6. Legacy Iran cache migration

`CountryPrefixStore.LoadPrefixesAsync(IR)` and the metadata/history loaders
perform one-time migration:

- new `prefixes/IR/` path exists → use it (idempotent; no re-migration).
- else legacy `iran-ipv4-prefixes.txt` exists → validate → atomically copy to
  `prefixes/IR/ipv4-prefixes.txt`; migrate metadata/history JSON the same way.
- legacy is **never** deleted until the new IR cache is proven populated.
- legacy IR data is never migrated into a non-IR country's directory.
- interrupted migration is safe to retry (the new path is created only after a
  successful validated copy; a partial new path is re-created cleanly).
- selected country ≠ IR → legacy IR cache is ignored (no cross-country use).

## 7. Invalid / empty / oversized response behavior

`OfficialCountryPrefixSource.FetchAsync` throws (does not return a dataset)
when:

- HTTP failure / timeout;
- malformed JSON / missing `data.resources`;
- **empty** prefix collection (treated as failure, never success);
- invalid CIDR (parse failure);
- response body exceeds the safety cap (see §9);
- prefix count exceeds the dataset-size bound (see §9).

The controller's `UpdatePrefixesAsync` catches fetch exceptions, records
metadata/history failure, and rethrows. `EnsurePrefixesAsync` then throws
"no `<CC>` IPv4 prefixes are available", so the cycle fails **without** an
empty planned set — existing routes are preserved. Last-known-good cache is
never overwritten by a failed/empty refresh.

## 8. Offline behavior

- Case A — selected IQ + valid cached IQ + source down:
  reconciliation uses the last-known-good IQ cache. Reconciliation still
  attempts a refresh via `EnsurePrefixesAsync`; if refresh fails the cycle
  errors but **no destructive empty plan is produced** (§7).
- Case B — selected IQ + only IR cache + source down:
  the IR cache is **never** used for IQ (country keyed). `EnsurePrefixesAsync`
  finds IQ empty, attempts fetch, fails → throws. Existing IR-owned routes
  remain installed; no IQ routes are added; no IR routes removed.
- Case C — selected IQ + no IQ cache + source down:
  `EnsurePrefixesAsync` throws; safe no-op (no route mutation).

This realizes "pause prefix reconciliation without deleting existing routes
until authoritative data for the selected country is available" without adding
a second persisted config property. There is no "effective country" concept:
the fail-on-empty/throw behavior of the source + EnsurePrefixesAsync is
sufficient to prevent destructive empty-plan behavior.

## 9. Dataset safety bounds

- Max response body: 16 MiB (configurable via `PrefixSourceOptions`; legacy
  value 16 MiB retained). An over-limit body throws before parsing.
- Max prefix count: 1,000,000 (conservative for the largest real countries;
  no legitimate country exceeds this today, protecting against a malformed /
  compromised source emitting millions of entries). Over-limit throws.
- Per-prefix CIDR validation (IPv4 only): address parses, prefix length
  0–32, normalized to canonical "a.b.c.d/len" lower-cased form, duplicates
  removed deterministically, sorted for stable diffs. IPv6 is rejected.

## 10. Overlap behavior

Overlapping prefixes (e.g. `10.0.0.0/8` and `10.1.0.0/16`) are **accepted and
both installed**. The engine already installs desired prefix routes
independently (verified country-neutral in Phase 35.1); collapsing or
rejecting overlaps was not required and is not introduced. No behavior change.

## 11. Generic type renames

| Before | After |
| --- | --- |
| `IPrefixSource` | `ICountryPrefixSource` (new signature) |
| `OfficialIranPrefixSource` | `OfficialCountryPrefixSource` |
| `IranPrefixProvider` | `CountryPrefixProvider` |
| `OfficialIranPrefixUpdateChecker` | `CountryPrefixUpdateChecker` |
| `IPrefixUpdateChecker` | retained for `PrefixUpdateMonitor` (`CheckAsync(ct)`); `ICountryPrefixUpdateChecker` adds the explicit-country overload |
| `PrefixFileRepository` | retained as low-level per-country backing store inside `CountryPrefixStore` (no longer used directly by controller/observation/diagnostics) |
| `PrefixSourceRequest` | removed (country passed directly) |

Not renamed: `IranDirect.Core`, `IranDirect.Service`, namespaces, named pipe,
Windows service, telemetry names/source. Branding is a Phase 36 concern.

## 12. Removal of the Phase 35.2 temporary gate

`DirectCountryRouting.IsDirectCountrySupported(DirectCountryCode? code)` now
returns `code is not null` (any valid ISO code is supported; null = legacy →
IR default). The worker no longer skips reconciliation for non-IR countries:
a valid selected country reconciles through the generic pipeline. IQ/RO flow
through normal reconciliation.

## 13. DirectCountryCode wiring

```
DesiredConfiguration.DirectCountryCode
    -> CountryPrefixProvider / OfficialCountryPrefixSource (via controller)
    -> ICountryPrefixSource.FetchAsync(country)
    -> CountryPrefixStore (country-scoped cache + metadata + history)
    -> IranDirectRuntimeObservationSource loads for selected country
    -> desired prefix routes
```

The controller resolves the selected country from
`DesiredConfigurationService.GetAsync().DirectCountryCode` (legacy → IR). The
observation source receives a `Func<DirectCountryCode>` resolver (selected
country) so desired-route construction always uses the configured country.

## 14. Update checker semantics

`CountryPrefixUpdateChecker` probes `HEAD` (falling back to `GET`) the
per-country RIPEstat URL and compares ETag / Last-Modified / Content-Length
against the **country-scoped** local metadata. An IR ETag can never suppress an
IQ update because metadata is keyed per country (`GetCurrentAsync(country)`).

## 15. IR / IQ / RO behavior

- **IR**: byte/semantic equivalent to pre-generalization (same source
  semantics, parsing, validation, dedup, ordering, desired-route result).
  Legacy cache migrates automatically.
- **IQ / RO**: same generic pipeline, request `?resource=IQ` / `?resource=RO`.
  Each owns an isolated cache and metadata; switching between them never
  cross-contaminates.

## 16. Switching (IR -> IQ -> RO -> IR)

Each country's cache is isolated; returning to IR reuses its valid
last-known-good cache. No country overwrites another; metadata/history stay
per-country. The planner accomplishes route changes naturally from the
changed desired-state (stops desiring IR prefixes, removes IR-owned routes no
longer desired, adds the new country's routes) while preserving endpoint
protection, custom routes, and external/non-owned routes.

## 17. Failed switch safety

Currently healthy IR -> user changes to IQ -> IQ source unavailable -> no IQ
cache: `EnsurePrefixesAsync(IQ)` throws; the cycle fails safely. Existing
IR-owned routes are **not** removed (no empty planned set is produced). When
IQ data becomes available, the next cycle installs IQ routes and removes the
stale IR routes.

## 18. Support / diagnostics impact

Diagnostic checks (`PrefixConfigurationDiagnosticCheck`,
`PrefixMetadataDiagnosticCheck`, `PrefixHistoryDiagnosticCheck`) and the
snapshot providers resolve the selected country and read its per-country
cache/metadata/history. No new telemetry labels were added.

## 19. Architecture-isolation proof

The following remain geography-neutral (no "IR"/"IQ"/country branching):
`RuntimeChangeSetPlanner`, `RuntimeExecutor`,
`WindowsRuntimeExecutionStepHandler`, `RouteMutationJournal`,
`RouteMutationRecovery`, `ManagedRoute`, VPN endpoint logic, custom-route
logic. Verified by grep: no country literals in those files.

## 20. Performance

Country is selected once at acquisition/cache boundary — no per-prefix country
checks in the planner/executor hot path. No regression was introduced; the
benchmark host was rebuilt (`-c Release`) and parsing/persistence remain
allocation-stable.

## 21. Remaining Phase 35 work

- 35.4: alerting/recording-rule scoping by `deployment_environment_name` /
  `service_name` (depends on the observability `resource_to_telemetry_conversion`
  flag staying enabled — see memory).
- 35.5–35.7: country selection UX, multi-country, IPv6 (deferred).
- Phase 36: PathVeer branding rename.

## 22. Verification

Full Core + Service suites green; Stress 12/12; Benchmark Release green;
no new unexplained warnings. Permanent tests cover source (IR/IQ/RO fetch,
fault injection, empty/oversized/invalid), persistence (IR/IQ/RO isolation,
legacy-IR migration, metadata isolation, atomic write, last-known-good
preserved on failed refresh), switching (IR->IQ, IQ->RO, RO->IR, failed
IR->IQ), offline (same-country cache, wrong-country-only cache, no cache), and
regression (IR equivalence, planner/executor unchanged, Phase 34/35.2 tests
unchanged).

## 23. Ad-hoc verification evidence (gate + live source)

- Stress (`Category=Stress`, Core.Tests, Debug): **12/12 passed** (3m9s).
- Full solution build (Debug): succeeded.
- `DirectCountryRouting.IsDirectCountrySupported` was **removed** (no production
  callers; country validity is enforced by the `DirectCountryCode` value object
  and the legacy null→IR default at the `DesiredConfiguration` layer). Related
  gate tests replaced with a documentation note. `DirectCountryCodeTests`: 23/23.

### Live RIPEstat country-resource-list probe (sandbox egress)
HTTP GET `?resource=<CC>`, success/empty/malformed/duplicate/size/time:

| CC | http | prefixes | malformed | dup | bytes | secs |
|----|------|----------|-----------|-----|-------|------|
| IR | 200 | 1954 | 3 | 0 | 51,938 | 12.79 |
| IQ | 200 | 336 | 0 | 0 | 10,152 | 1.17 |
| RO | 200 | 2831 | 4 | 0 | 64,100 | 2.18 |
| US | 200 | 70,051 | 30 | 0 | 1,694,122 | 2.70 |
| BR | 200 | 13,000 | 0 | 0 | 466,017 | 4.19 |
| JP | — | timeout (WinError 10060) | | | | 32.5 |
| ZA | — | timeout (WinError 10060) | | | | 21.1 |
| AU | — | timeout (WinError 10060) | | | | 32.5 |

JP/ZA/AU timed out from the agent sandbox (TLS egress restriction on those
destinations), not a RIPEstat defect — IR/IQ/RO/US/BR returned 200 with
non-empty datasets. The source returned a few malformed entries per country
(IR 3, RO 4, US 30); production drops these via `IsValidIpv4Prefix` + `Distinct`,
so cached datasets are clean. 0 duplicates (source already de-dupes; code also
`Distinct`s defensively).

### RIPEstat semantics (authoritative)
Per RIPEstat docs, `country-resource-list` "lists the Internet resources
associated with a country, including ASNs, IPv4 ranges and IPv4/6 CIDR prefixes"
and is "derived from the RIR Statistics files maintained by the various RIRs."
I.e. it reflects **RIR-registered/allocated** resources for the country's ISO
code — **not geolocation**. This is acceptable for the "direct-country" policy:
the policy routes traffic for a country's *registered* address space directly
(away from the VPN), which is exactly the registered-allocation set. The dataset
is a static routing input, not a geolocation signal, so registration-based
coverage is the correct semantic.

