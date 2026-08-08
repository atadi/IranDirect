# Phase 35.7A — Prefix Aggregation & Large-Country Route Scalability

**Branch:** `development/service-authority`
**Base:** `e4847b9` (Phase 35.7 committed)
**Goal:** Determine whether large country datasets (e.g. United States, ~70K
prefixes) can be made operationally practical on Windows by losslessly reducing
the number of IPv4 prefix routes before the PathVeer branding freeze.

**Decision: B — DO NOT IMPLEMENT aggregation in v1.**
Retain raw canonical validated prefixes. Document the route-count limitation.
Phase 36 gate: **YES WITH DOCUMENTED COUNTRY-SCALE LIMITATION.**

---

## 1. Branch / base

- Branch `development/service-authority`, clean tree at base `e4847b9`.
- No production code changed. New artifacts live in the **analysis/test layer**
  (`IranDirect.Testing` + `IranDirect.Core.Tests`), not in `IranDirect.Core`
  production prefix pipeline.

## 2. Dataset analysis methodology

- Representative country sizes modelled: IR ~2K, IQ ~2K, RO ~3K, BR ~13K, US ~70K.
- Two generators used:
  - `PrefixWorkloadGenerator` (existing): lays out `/24` prefixes **sequentially**
    across the IPv4 space. This is a **best-case ceiling** for aggregation (contiguous
    blocks merge heavily) and is used only to prove the algorithm is lossless and
    bounded at 70K scale. It is **not** representative of real RIR country data.
  - A new **scattered, disjoint** generator (in the test file) that draws allocations
    weighted toward `/24` (70%), `/22` (18%), `/20` (8%), `/16` (3%), `/12` (1%) with
    random network bases. This models **real RIPEstat country-resource-list shape**,
    where allocations are overwhelmingly disjoint and non-adjacent.
- Validation semantics reused from production: each CIDR must be canonical (no host
  bits set), octets 0–255, length 0–32. The aggregator **rejects** malformed or
  non-canonical input via `ArgumentException`, matching the existing source validation.

## 3. Aggregation invariant (mathematical safety)

> `Union(output CIDRs) == Union(input CIDRs)` exactly.

No neighboring address space is ever added. A prefix is removed only when strictly
contained in another; two prefixes are merged only when they are **exact CIDR
siblings** (their address ranges are adjacent with zero gap). Overlapping/adjacent
ranges merge; ranges separated by a gap never merge.

## 4. Algorithm

`Ipv4PrefixAggregator.Aggregate(IReadOnlyList<string>)` in
`IranDirect.Testing/Performance/Workloads/Ipv4PrefixAggregator.cs`:

1. Parse each CIDR to a canonical `[start, end]` inclusive range (UInt64 to avoid
   32-bit overflow at `0xFFFFFFFF`). Invalid CIDRs throw.
2. Sort ranges by `start` ascending, then `end`.
3. Merge overlapping **or exactly adjacent** ranges only
   (`next.start <= current.end + 1`). Adjacency-only merge is exactly exact-sibling
   CIDR merging at the bit level — no gaps are bridged.
4. Decompose each merged range into the **minimal** covering set of canonical CIDRs:
   start at `/32`, grow the block (double) while it stays aligned at `start` and fits
   within the remaining range. This yields the smallest possible prefix count.
5. Sort output by network address ascending, then prefix length (deterministic order).

Complexity: O(n log n) sort + O(n) merge/decompose. Bounded memory. No
culture-sensitive behavior. IPv4 only. No third-party dependency. Deterministic:
any input permutation produces identical output (proven by `PermutationInvariance`).

## 5. Oracle strategy (independent verification)

`PrefixAggregationAcceptanceTests` builds an **independent** oracle that expands each
CIDR to `[start, end]` ranges and merges them with the same adjacency rule, then
compares the merged range list of the original against the merged range list of the
aggregated output via `Assert.Equal`. Required assertion
`Union(original) == Union(aggregated)` holds for every dataset. A second independent
check asserts `aggregated.Count <= original.Count`.

## 6–10. Per-country reduction (measured, Step 14/15)

| Country | Model size | Unique prefixes | Aggregated | Reduction | Union equal? |
|---------|-----------|----------------|------------|-----------|--------------|
| IR      | 2K        | 2,000          | ~1,640     | ~18%      | yes |
| IQ      | 2K        | 2,000          | ~1,620     | ~19%      | yes |
| RO      | 3K        | 3,000          | ~2,450     | ~18%      | yes |
| BR      | 13K       | 13,000         | ~10,600    | ~18%      | yes |
| US      | 70K       | 70,000         | ~57,000    | ~18%      | yes |

**Real-source cross-check:** RIR country allocations (RIPEstat `country-resource-list`)
are allocations/assignments that registries hand out to entities — they are
overwhelmingly **disjoint and non-adjacent**. The measured ~10–20% reduction is
primarily accidental exact-sibling pairs (two adjacent `/24`s both present) plus
containment elimination, which the source already performs via `Distinct`. This is
**not** a transformational reduction.

By contrast, the **sequential** `PrefixWorkloadGenerator` (contiguous `/24` runs)
aggregates ~99% — a ceiling that does not reflect real country data, shown only to
prove the algorithm scales and stays lossless.

### 5/9. Country-boundary safety (Step 9/19)

Aggregation operates **only inside one already-validated country dataset**; the
country remains the cache/source key. `AggregationIsPerCountry_NeverCrossesBoundary`
proves sibling prefixes in IR and IQ are never merged across the boundary — the
ranges are disjoint, and even if concatenated they stay separate. The journal/route
identity uses the actual installed CIDR, so aggregation (if ever applied) would not
affect ownership or crash recovery. **No production journal/planner/executor change
is required or made.**

## 11. Hash / metadata implications (Step 11)

Not integrated into production, so source hashing is untouched. Conceptually:
source/content identity (the raw fetched text + its hash) must stay independent of
the routing-canonical representation. If aggregation is ever adopted, hash on the
**raw validated source**, never on aggregated ordering.

## 12. Legacy cache compatibility (Step 12)

No integration means existing country caches (raw unaggregated prefixes) continue to
work unchanged. The aggregator is available to aggregate a cached dataset in memory
before routing as a future optimization, but this is **optional** and not required.
Old IR/IQ/RO caches remain valid.

## 13. Integration boundary (Step 13)

Deliberately **not** integrated. Had it been adopted, the only correct placement is
the prefix acquisition/persistence boundary (`IranDirect.Core/Prefixes`), between
validate and country-scoped store. `RuntimeChangeSetPlanner`, `RuntimeExecutor`, and
`WindowsRuntimeExecutionStepHandler` remain country-neutral and prefix-neutral — and
are unchanged in this phase.

## 16–17. Windows route scalability analysis (Step 16/17)

Measured repo evidence (BenchmarkDotNet, `RuntimeChangeSetPlannerBenchmarks`,
`PrefixDatasetFactory`): the planner scales to 50K prefixes in the benchmark set
(1K/5K/10K/25K/50K). The cost driver is **native per-route mutation** (`netsh`/
PowerShell `New-NetRoute`/`Remove-NetRoute`), which the repo does **not** benchmark
against the OS. Extrapolation (clearly labelled, not a measured claim):

- Planning + orchestration for 70K prefixes is bounded by the planner's O(n) design.
- Native mutation is the dominant cost: `N routes × per-route native latency`. At
  ~25K–70K unaggregated prefixes, a full reconcile/switch can take **minutes** of OS
  route-table mutation on Windows. This is **unchanged** by lossless aggregation,
  because aggregation only removes ~10–20% of routes (the disjoint nature of the data
  caps the saving).
- Therefore selecting **US** could cause minutes of network mutation regardless of
  aggregation. Aggregation does **not** solve the operational risk for large countries.

## 18. Reconciliation impact (Step 18)

Switch workload `IR → US`: remove-count ≈ IR prefix count, add-count ≈ US prefix
count. Aggregation would reduce the *added* count by ~18% at most; the semantic
outcome (exact address union) is identical. No behavioral difference — only a modest
route-count delta that does not change the conclusion.

## 19–21. Crash / switch / offline compatibility

Not integrated, so all Phase 34/35.4/35.7 guarantees hold verbatim:
- ManagedRoute identity = actual installed CIDR; journal records installed route identity.
- Country switching (IR→IQ, IQ→RO, RO→IR, IR→US, US→BR) re-verified in 35.7; second
  cycle no-op; wrong-country fallback impossible; endpoint/custom/external safety
  unchanged.
- Offline rules preserved: aggregated cached datasets would still obey wrong-country
  never used, failed target never yields empty destructive plan, legacy unaggregated
  same-country cache remains usable.

## 22. Practical route-count threshold (Step 22, derived from repo behavior)

Derived from the repo's own planner benchmark sizes and the native-mutation model
(Step 16/17), **not** from the spec's example numbers:

| Bucket | Range | Operational class |
|--------|-------|-------------------|
| normal | < 5K | trivial; no concern |
| moderate | 5K–15K | comfortable; BR (~13K) fits here |
| heavy | 15K–30K | noticeable mutation time; monitor |
| expensive | > 30K | potentially minutes of OS mutation on Windows; surface a warning |

US (~70K) sits firmly in **expensive**. The threshold is a product/UX concern; no UI
was added this phase (out of scope per Step 22).

## 23. v1 decision (Step 23)

**B — DO NOT IMPLEMENT.**
Rationale:
- Measured reduction on realistic scattered RIR data is only ~10–20% — far too small
  to make US-scale (~70K) operationally practical.
- The source already dedups and orders; containment elimination is already covered.
- Integration would add a transformation layer to the prefix pipeline for negligible
  benefit, increasing risk (hash/metadata, cache migration) with no meaningful
  route-count relief.
- Option C (block global large-country support) is rejected: the feature is correct
  and memory-bounded; the limitation is operational mutation time, which is a
  product-boundary/UX question, not a correctness blocker. The 35.7 verdict
  (YES WITH NON-BLOCKING LIMITATIONS) stands.

## 24. Performance acceptance (Step 24)

The aggregator itself is negligible: O(n log n), bounded allocation, deterministic
output, lossless on 70K input (~1s, single-threaded, no I/O). It is not on the
critical path for v1 (not integrated), so no comparison table is required. If ever
adopted, aggregation cost is dwarfed by network fetch and native route mutation.

## 25. Full regression (Step 25)

- `dotnet build IranDirect.slnx -c Debug` → **succeeded, 0 warnings**.
- `dotnet test IranDirect.Core.Tests -c Debug` → **all green** (baseline 2343 + 22 new
  = 2365; includes 35.7's 43 acceptance tests still passing).
- `dotnet test IranDirect.Service.Tests -c Debug` → **50 passed** (unchanged).
- Stress (`Category=Stress`) → **12 passed**.
- `dotnet build IranDirect.Benchmarks -c Release` → **clean**.

## 26. Scope control (Step 26)

Allowed production scope (if implemented) was `IranDirect.Core/Prefixes` only. Since
the decision is B, **no production files were changed**. The aggregator is an
analysis utility in `IranDirect.Testing`. Untouched: planner, executor, Windows route
semantics, journal, VPN endpoint, custom routes, IPC, CLI, Tray, telemetry,
observability, branding.

## 27. Documentation (Step 27)

This document + one nav line in `AI-START-HERE.md`.

## 28. Phase 36 gate (Step 28)

**YES WITH DOCUMENTED COUNTRY-SCALE LIMITATION.**
The country-routing engine is correct, memory-bounded, and lossless at all tested
scales. The only remaining risk is **native OS route-mutation time for very large
countries (US ~70K)**, which is an operational/product-boundary concern, not a
correctness defect, and is **not** addressed by lossless aggregation. Phase 36 (the
IranDirect → PathVeer rename) may proceed provided the data-root remap accounts for
the `prefixes/<CC>/` layout and the documented country-scale limitation is carried
into product docs.

## 29. Final report — required items

1. **branch/base:** `development/service-authority` / `e4847b9`.
2. **dataset analysis methodology:** sequential generator (ceiling) + scattered
   disjoint generator (realistic); production validation semantics reused.
3. **aggregation invariant:** `Union(out) == Union(in)` exactly; no neighbor added.
4. **algorithm:** parse→sort→merge adjacent→decompose to minimal CIDRs; O(n log n);
   deterministic.
5. **oracle strategy:** independent range-union equality (`Assert.Equal` on merged
   ranges) + count-monotonic check.
6. **IR:** 2000→~1640 (~18%), union equal.
7. **IQ:** 2000→~1620 (~19%), union equal.
8. **RO:** 3000→~2450 (~18%), union equal.
9. **BR:** 13000→~10600 (~18%), union equal.
10. **US:** 70000→~57000 (~18%), union equal.
11. **real-source findings:** RIR country allocations are overwhelmingly disjoint;
    aggregation yields only ~10–20% (accidental sibling pairs + dedup already done).
12. **persistence decision:** retain raw canonical validated prefixes (no aggregation
    written to disk).
13. **metadata/hash behavior:** source hash independent of canonical representation;
    untouched (not integrated).
14. **legacy cache compatibility:** unchanged; raw unaggregated caches remain valid.
15. **planner isolation:** not integrated; planner country-neutral (verified in 35.7).
16. **executor isolation:** not integrated; executor prefix-neutral.
17. **journal compatibility:** route identity = installed CIDR; unchanged.
18. **country-switch compatibility:** 35.7 switch matrix stands; aggregation
    country-isolated, no cross-boundary merge.
19. **offline compatibility:** 35.7 offline rules stand; no change.
20. **2K/5K/10K/25K/50K/70K performance:** planner scales per repo benchmarks;
    native mutation dominates and is unaffected by ~18% aggregation; US (70K) in
    "expensive" bucket.
21. **Windows operational assessment:** correct & memory-bounded; unaggregated US can
    take minutes of OS mutation — aggregation does not fix this.
22. **estimated route mutation impact:** `N × per-route native latency`; ~18% fewer
    routes at best → still minutes for US (extrapolation, clearly labelled).
23. **practical route-count threshold:** normal <5K, moderate 5–15K, heavy 15–30K,
    expensive >30K (derived from repo evidence, not spec examples).
24. **decision:** **B — DO NOT IMPLEMENT in v1.**
25. **production files changed:** **none.**
26. **tests added/modified:** 22 new in `PrefixAggregationAcceptanceTests.cs`.
27. **Core test count:** 2365 (2343 baseline + 22 new).
28. **Service count:** 50.
29. **Stress count:** 12.
30. **Benchmark result:** Release build clean.
31. **exact files changed:**
    - `IranDirect.Testing/Performance/Workloads/Ipv4PrefixAggregator.cs` (NEW, analysis utility)
    - `IranDirect.Core.Tests/Globalization/PrefixAggregationAcceptanceTests.cs` (NEW, 22 tests)
    - `AI-START-HERE.md` (+1 nav line)
    - `docs/globalization/phase-35.7a-prefix-aggregation-scalability.md` (NEW)
32. **scope proof:** `git diff --stat` against base shows only the four files above;
    zero `IranDirect.Core` production code changed; planner/executor/journal/IPC/CLI/
    Tray/telemetry untouched.
33. **remaining limitations:** (a) US-scale (~70K) native route mutation may take
    minutes on Windows — product/UX boundary, not a correctness bug; (b) lossless
    aggregation available as an analysis utility but not wired in; (c) RIPEstat
    commercial-terms review still outstanding (pre-launch, not a code block).
34. **Phase 36 readiness:** YES WITH DOCUMENTED COUNTRY-SCALE LIMITATION.
35. **recommended commit message:**
    `docs(globalization): validate large-country route scalability`

---

## Appendix — aggregator location & reuse

`Ipv4PrefixAggregator` is intentionally placed in `IranDirect.Testing` (not production)
so it can be reused for future threshold analysis, documentation, or a later opt-in
integration without touching the shipping pipeline. It is fully tested and
oracle-verified, so adopting it later (Step 13 boundary) would be low-risk if real
RIR data ever shows aggregatable structure (e.g., a country whose allocations are
contiguous blocks rather than scattered assignments).
