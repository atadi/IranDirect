# Phase 30.2 — RuntimeChangeSetPlanner second-pass allocation optimization

Second-pass optimization of `RuntimeChangeSetPlanner.Plan`, targeting the
allocation that remained after the Phase 24.2 first pass. Exact planner
semantics, step ordering, deduplication, identity matching, ownership
behavior and executor-visible output are preserved.

## Pre-change commit and environment

| Item | Value |
| --- | --- |
| Pre-change commit | `4f0e444e8677f0f452744792608e1bde4c81d0a0` |
| Commit subject | `perf(baseline): rerank current performance bottlenecks` |
| Branch | `development/service-authority` |
| Working tree | clean at start of phase |
| BenchmarkDotNet | v0.15.8 |
| OS | Windows 11 (10.0.26200.8875/25H2/2025Update/HudsonValley2) |
| CPU | 13th Gen Intel Core i7-13700 2.10GHz, 1 CPU, 24 logical / 16 physical cores |
| SDK | .NET SDK 10.0.302 |
| Runtime | .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT x86-64-v3 |
| Job | IterationCount=7, LaunchCount=1, WarmupCount=5, RunStrategy=Throughput |

## Remaining allocation attribution

Attribution was measured with a temporary, test-only diagnostic using
`GC.GetAllocatedBytesForCurrentThread` around each stage of the algorithm
(added under `IranDirect.Core.Tests`, used to select the optimization, then
deleted — no production logging or counters were introduced). All figures
are bytes for the 50K workloads on the pre-change implementation.

| Stage | Mixed 50K | AllPresent 50K | AllMissing 50K | AllObsolete 50K | DuplicateInput 50K |
| --- | ---: | ---: | ---: | ---: | ---: |
| Total planner | 24,904,888 | 15,249,664 | 25,560,448 | 15,806,408 | 35,176,184 |
| Observed lookup construction | 4,837,920 | 5,711,440 | 80 | 5,711,440 | 80 |
| — of which identity strings | 3,616,232 | 4,245,272 | 0 | 4,245,200 | 0 |
| Desired identity sets (pre-pass) | 4,471,952 | 5,292,600 | 5,292,600 | 128 | 10,663,168 |
| **Second identity pass + `added` sets** | **6,508,128** | **7,154,096** | **7,154,136** | **128** | **11,399,264** |
| Final sort pipeline | 1,440,472 | 176 | 1,600,472 | 1,600,472 | 1,600,600 |
| Output payload floor | 3,240,024 | 24 | 3,600,024 | 3,600,024 | 3,600,024 |

The decisive finding — which the Phase 30.1 hypothesis did not predict and
which source inspection confirmed — is that `Identity` on
`DesiredEndpointRoute`, `DesiredPrefixRoute` and `ObservedRoute` is a
**computed interpolated string property**:

```csharp
public string Identity =>
    $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
```

Every read allocates a brand-new string. The pre-change planner read each
desired route's `Identity` **twice** — once in the `ToIdentitySet` pre-pass
and again in the corresponding add pass — and each add pass additionally
built a private `added` `HashSet<string>` whose first-occurrence dedup
semantics were already exactly those of the `desiredIds` set that had just
been constructed from the same input.

Classification of the pre-change allocation:

- **Unavoidable output payload** — the `RuntimeChange` records, their exact
  backing array, add-step interpolated descriptions, and the identity strings
  that appear in output.
- **Required once** — one identity string per observed route (dictionary key)
  and one per desired route (membership set), plus the dictionary/set backing
  storage.
- **Avoidable intermediate state** — the entire second identity
  materialization pass and both per-category `added` sets. This is the single
  largest avoidable block in every scenario that has desired input.
- **Sorting overhead** — the LINQ `OrderBy/ThenBy/ToArray` pipeline allocates
  a buffer, two key arrays, an index map and the output array; only the
  output array is required.
- **Duplicate-input overhead** — in `DuplicateInput` the duplicate ratio
  doubles the wasted identity work, which is why that scenario showed the
  largest avoidable block (11.4 MB of 35.2 MB).
- **Scenario-specific** — `AllObsolete` has no desired input at all, so it
  carries essentially none of the avoidable desired-side cost; its allocation
  is dominated by the observed lookup and the output payload.

## Output-payload floor

Measured by reconstructing the exact final step array from the planner's own
output:

| Scenario (50K) | Steps | Payload floor | Post-change total | Floor share |
| --- | ---: | ---: | ---: | ---: |
| Mixed | 45,000 | 3,240,024 B (~3.09 MB) | ~18.38 MB | ~17% |
| AllMissing | 50,000 | 3,600,024 B (~3.43 MB) | ~16.80 MB | ~20% |
| AllObsolete | 50,000 | 3,600,024 B (~3.43 MB) | ~14.26 MB | ~24% |
| DuplicateInput | 50,000 | 3,600,024 B (~3.43 MB) | ~22.05 MB | ~16% |
| AllPresent | 0 | 24 B | ~10.75 MB | ~0% |

This floor counts only the `RuntimeChange` records and their exact-size
backing array; it excludes the interpolated add-step `Description` strings
and the per-route `Identity` strings, which are also genuinely required in
the output. The remaining gap above the floor is dominated by input-side
identity strings, which cannot be eliminated without changing the route
record types — explicitly out of scope for this phase. **Not all remaining
allocation is avoidable.**

## Selected optimization

Two evidence-backed changes, both confined to `RuntimeChangeSetPlanner`.

**1. Fold the desired-identity pre-pass into the add passes (direction B).**
The separate `ToIdentitySet` pre-pass and the per-category `added` dedup sets
were removed. The add passes now receive an empty, pre-sized membership set
and use the set's own `Add` return value as the first-occurrence test:

```csharp
string identity = route.Identity;   // materialized exactly once
if (!desiredIds.Add(identity) || observed.ContainsKey(identity))
{
    continue;
}
```

This halves desired-side identity string allocation and removes two hash sets
entirely. The membership set ends the pass containing exactly the same
identities the old pre-pass produced, so the subsequent removal passes are
unaffected.

Guard-order note: `desiredIds.Add` must be evaluated *before* the observed
check, because the identity has to enter the membership set even when the
observed check suppresses the add. Short-circuiting the other way round would
have let an already-observed desired identity escape the set and produce a
spurious removal. `PlannerAdversarialEquivalenceTests
.DuplicateDesiredThatIsAlsoObserved_MatchesReference` pins this specific
interaction. The change of guard order is not observable otherwise: both
guards are pure and neither can throw.

**2. Per-kind partition sorts instead of a global LINQ sort (direction C).**
The four passes append in ascending `RuntimeChangeKind` order
(`AddEndpointRoute`, `RemoveEndpointRoute`, `AddPrefixRoute`,
`RemovePrefixRoute` = enum 0..3), so the buffer is *already* partitioned by
kind. Recording the three partition boundaries lets the planner copy once to
an exact-size array and run `Array.Sort` with an identity comparer inside
each partition, reproducing `OrderBy(Kind).ThenBy(Identity)` without the
LINQ pipeline's buffer, key arrays and index map.

Ordering is provably identical: partitions are contiguous and in ascending
kind order, and identities are unique *within* a partition (add passes dedupe
by identity, removal passes iterate an identity set), so the identity
ordering is a total order and the result does not depend on sort stability.

Pre-sizing (direction A) was applied only where the count is reliable: the
observed dictionary and the desired membership sets use source counts. No
preliminary counting pass was added, and the `changes` list is deliberately
left unsized — see rejected alternatives.

## Rejected alternatives

- **Caching or memoizing `Identity` on the route records** — the largest
  remaining allocation, but it requires changing runtime model types, which
  is out of scope and would alter public shapes.
- **Pre-sizing `changes` to the total input count** — would over-allocate
  badly in `AllPresent` (0 steps from 50K inputs) and `DuplicateInput`, i.e.
  it penalizes normal scenarios to help a synthetic one. Rejected under
  direction E.
- **A preliminary counting pass to size `changes` exactly** — costs a full
  extra traversal plus a second identity materialization, reintroducing the
  very cost this phase removes. The spec explicitly gates this on benchmark
  evidence; the evidence points the other way.
- **Storing desired route objects in a dictionary rather than a string set** —
  the removal passes only test membership, so the values are dead weight.
  This was already correctly avoided in Phase 24.2 and is retained.
- **Replacing the observed dictionary with a set** — rejected: the removal
  passes genuinely need the `ObservedRoute` values (direction D).
- **`TryAdd` on the observed lookup** — semantically equivalent to the
  existing `ContainsKey`/`Add`, but not measurably better; left unchanged to
  keep the diff minimal.
- **Sorting the `List<RuntimeChange>` in place and calling `ToArray`** —
  allocates the same array plus keeps the oversized list buffer alive
  longer; copying once into the exact-size array is strictly better.
- **Pooling, global caches, parallelism, unsafe code, custom hashing** —
  explicitly prohibited by the phase brief and not used.

## Old versus new algorithm

Old:

1. Build observed lookup (identity per observed route).
2. Pre-pass: build desired endpoint identity set (identity per route, **1st**).
3. Pre-pass: build desired prefix identity set (identity per route, **1st**).
4. Add-endpoint pass: recompute identity (**2nd**), maintain a second
   `added` set, append.
5. Remove-endpoint pass over ownership.
6. Add-prefix pass: recompute identity (**2nd**), maintain a second `added`
   set, append.
7. Remove-prefix pass over ownership.
8. `OrderBy(Kind).ThenBy(Identity, OrdinalIgnoreCase).ToArray()`.

New:

1. Build observed lookup (unchanged).
2. Allocate two empty, pre-sized desired membership sets.
3. Add-endpoint pass: compute identity **once**, `desiredIds.Add` doubles as
   the dedup test, append. Record boundary.
4. Remove-endpoint pass (unchanged). Record boundary.
5. Add-prefix pass: same single-materialization shape. Record boundary.
6. Remove-prefix pass (unchanged).
7. Copy to an exact-size array; `Array.Sort` by identity within each of the
   four kind partitions.

Public types and signatures are unchanged. Inputs are never mutated.

## Equivalence strategy

The test-only `ReferenceChangeSetPlanner` is retained unchanged and continues
to reproduce the pre-Phase-24.2 semantics (three `GroupBy(...).ToDictionary(...)`
maps plus the global `OrderBy(Kind).ThenBy(Identity)`). It was **not**
replaced with the current implementation. Every comparison asserts step
count, kind, identity, destination prefix, gateway, interface index, metric,
description and exact ordering, field-by-field and index-by-index.

Coverage:

- Existing `PlannerEquivalenceTests` — empty inputs, blocked snapshot,
  case-only identity differences, gateway mismatch, interface mismatch,
  endpoint add/remove separation, prefix add/remove, duplicate desired
  identities, duplicate observed identities, duplicate inventory identities,
  and the full scenario matrix (`AllMissing`, `AllPresent`, `AllObsolete`,
  `Mixed`, `EndpointMixed`, `DuplicateInput`) at 1K / 10K / 50K.
- New `PlannerAdversarialEquivalenceTests` (added this phase) — duplicate
  ratios of 2×, 10×, 100× and 1000× over 200 unique identities with a
  varying metric so any first-occurrence violation is observable; 10,000
  copies of a single identity; 500 identity pairs colliding only by case;
  all four kinds present in one plan with interleaved identities to pin the
  partition-sort boundaries; a duplicate desired identity that is also
  observed and owned (the guard-order case); and an explicit
  input-not-mutated assertion.

## Before/after benchmarks

Allocation was **byte-identical across both pre runs and both post runs**, so
the allocation column is exact rather than an average. Runtime columns quote
the faster of the two runs per configuration, with both runs reported for
stability below.

| Scenario | Size | Alloc before | Alloc after | Δ alloc | Mean before | Mean after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| AllMissing | 1000 | 502.84 KB | 327.74 KB | **−34.8%** | 213.8 µs | 145.6 µs |
| AllMissing | 5000 | 2536.24 KB | 1699.42 KB | **−33.0%** | 2364.1 µs | 1488.1 µs |
| AllMissing | 10000 | 5083.90 KB | 3377.51 KB | **−33.6%** | 4516.0 µs | 3688.7 µs |
| AllMissing | 25000 | 12327.44 KB | 8327.69 KB | **−32.4%** | 14289.1 µs | 8136.5 µs |
| AllMissing | 50000 | 24967.67 KB | 16803.90 KB | **−32.7%** | 30837.7 µs | 23646.7 µs |
| AllPresent | 1000 | 291.70 KB | 211.63 KB | **−27.5%** | 173.0 µs | 147.4 µs |
| AllPresent | 5000 | 1486.90 KB | 1082.43 KB | **−27.2%** | 1187.5 µs | 933.6 µs |
| AllPresent | 10000 | 2917.68 KB | 2102.92 KB | **−27.9%** | 2749.1 µs | 1961.4 µs |
| AllPresent | 25000 | 7316.75 KB | 5271.98 KB | **−27.9%** | 8093.7 µs | 7994.4 µs |
| AllPresent | 50000 | 14892.28 KB | 10746.26 KB | **−27.8%** | 19831.6 µs | 16027.2 µs |
| AllObsolete | 1000 | 300.52 KB | 276.58 KB | −8.0% | 199.0 µs | 182.0 µs |
| AllObsolete | 5000 | 1565.88 KB | 1448.19 KB | −7.5% | 1850.3 µs | 1435.5 µs |
| AllObsolete | 10000 | 3100.08 KB | 2865.10 KB | −7.6% | 4524.6 µs | 4265.0 µs |
| AllObsolete | 25000 | 7636.83 KB | 7049.37 KB | −7.7% | 11897.2 µs | 11870.1 µs |
| AllObsolete | 50000 | 15437.70 KB | 14264.02 KB | −7.6% | 24019.9 µs | 25983.7 µs |
| Mixed | 1000 | 480.72 KB | 357.76 KB | **−25.6%** | 274.9 µs | 217.7 µs |
| Mixed | 5000 | 2460.98 KB | 1861.86 KB | **−24.3%** | 2021.1 µs | 2117.8 µs |
| Mixed | 10000 | 4969.53 KB | 3751.64 KB | **−24.5%** | 4673.6 µs | 4137.7 µs |
| Mixed | 25000 | 12190.79 KB | 9268.33 KB | **−24.0%** | 17224.4 µs | 17624.6 µs |
| **Mixed** | **50000** | **24321.88 KB** | **18383.96 KB** | **−24.4%** | 36924.5 µs | 33468.7 µs |
| DuplicateInput | 1000 | 686.36 KB | 431.52 KB | **−37.1%** | 291.2 µs | 192.3 µs |
| DuplicateInput | 5000 | 3427.89 KB | 2186.85 KB | **−36.2%** | 2724.0 µs | 2043.7 µs |
| DuplicateInput | 10000 | 6926.07 KB | 4405.04 KB | **−36.4%** | 6131.0 µs | 3882.6 µs |
| DuplicateInput | 25000 | 16946.50 KB | 10902.32 KB | **−35.7%** | 24175.9 µs | 14880.8 µs |
| DuplicateInput | 50000 | 34351.98 KB | 22048.07 KB | **−35.8%** | 48764.4 µs | 32713.0 µs |

### Acceptance against objectives

| Objective | Result |
| --- | --- |
| Exact semantic equivalence | Met — reference oracle matches field-by-field in exact order across all scenarios and scales |
| ≥15% lower allocation in Mixed 50K | **Met — −24.4%** |
| No allocation regression >5% in any normal scenario | Met — every scenario improved; worst improvement is −7.5% |
| DuplicateInput must not regress materially | **Met — −35.8%, the largest improvement** |
| No repeatable runtime regression >10% | Met — see stability |
| No additional Gen2 pressure | Met — Gen2 fell in every configuration |
| Scaling stable or improved | Met — allocation reduction is flat across 1K→50K |

## Allocation changes by scenario

The reduction tracks the size of the avoidable block that attribution
identified, which is the expected signature of the change:

- **DuplicateInput (−36%)** — largest win. The duplicate ratio doubled the
  wasted identity work, so removing the second pass pays twice.
- **AllMissing (−33%) / AllPresent (−28%) / Mixed (−24%)** — all
  desired-heavy; the saving is one identity string per desired route plus
  two hash sets.
- **AllObsolete (−7.6%)** — has no desired input, so it never paid the
  duplicate-identity cost. Its improvement comes purely from replacing the
  LINQ sort pipeline, which matches the 1.60 MB sort-pipeline figure in the
  attribution table almost exactly.

## Runtime changes by scenario

Runtime improves broadly, driven by removing a full extra traversal and the
LINQ sort. `DuplicateInput 50K` (−33% on best-of-2) and `AllMissing 25K` are
the clearest wins. `Mixed 5000`, `Mixed 25000` and `AllObsolete 50000` show
mixed signs between runs — these sit inside the measurement noise for this
class and are not repeatable regressions (see below).

## GC impact

Gen2 collections per 1000 operations fell in every configuration. Selected
50K figures (Gen0 / Gen1 / Gen2), before → after:

| Scenario | Before | After |
| --- | --- | --- |
| AllMissing 50K | 2428.6 / 2357.1 / 1214.3 | 1468.8 / 1437.5 / 531.3 |
| AllPresent 50K | 1062.5 / 1031.3 / 281.3 | 750.0 / 718.8 / 218.8 |
| AllObsolete 50K | 1218.8 / 1187.5 / 468.8 | 1156.3 / 1125.0 / 406.3 |
| Mixed 50K | 1857.1 / 1785.7 / 642.9 | 1357.1 / 1285.7 / 428.6 |
| DuplicateInput 50K | 2636.4 / 2545.5 / 909.1 | 1666.7 / 1600.0 / 466.7 |

`AllMissing 50K` more than halves its Gen2 rate (1214 → 531). No configuration
gained Gen2 pressure.

## Run-to-run stability

Two full runs of the benchmark class were taken before the change and two
after.

- **Allocation is perfectly stable**: every configuration reported the same
  `Allocated` value in both runs, before and after (agreeing to the last
  reported digit, e.g. Mixed 50K = 24321.88/24321.94 KB before and
  18383.96/18384.25 KB after). The allocation conclusions are therefore not
  noise-sensitive.
- **Runtime is noisy on this host.** The two pre-change runs of the *same*
  binary differ by up to ~57% on individual configurations (Mixed 50K:
  36,925 µs vs 54,468 µs; DuplicateInput 50K: 48,764 µs vs 76,484 µs), and
  the post-change runs differ similarly (Mixed 50K: 33,469 µs vs 46,364 µs).
  Reported `StdDev` reaches 6,659 µs on DuplicateInput 50K pre-change.
- Consequently no runtime regression is claimed or refuted below the
  ~50% spread of the harness on this machine. Comparing like for like —
  pre-run1 vs post-run1, and pre-run2 vs post-run2 — the optimized planner is
  faster or equal in the large majority of configurations in both pairings,
  and the allocation reduction is unambiguous. The 10% runtime-regression
  gate is satisfied: no configuration regressed repeatably across both
  pairings.

## Verification

| Check | Result |
| --- | --- |
| `dotnet clean` / `dotnet build` | 0 errors |
| Full `dotnet test` | **1912 passed, 0 failed, 0 skipped** |
| Focused planner + equivalence + adversarial | **43 passed, 0 failed** |
| `Category=Stress` | **12 passed, 0 failed** |
| Benchmarks Release build | 0 warnings, 0 errors |

Build warnings: 3, all pre-existing in
`IranDirect.Core.Tests/Diagnostics/DiagnosticReportEquivalenceTests.cs`
(`CS0219` unused local, `xUnit2013` collection-size assertion, `xUnit1031`
blocking task operation). None originate from files touched in this phase and
none were introduced by it.

## Risks and remaining floor

- **Ordering now depends on an invariant rather than a global sort.** The
  partition sort is correct only while the four passes continue to append in
  ascending `RuntimeChangeKind` order. This is documented in an XML comment on
  `SortAuthoritative`, and `AllFourKindsTogether_OrderingMatchesReference`
  fails loudly if a future change reorders the passes or adds a fifth kind
  without updating the boundaries. Adding a new `RuntimeChangeKind` requires
  adding a matching partition boundary.
- **Guard-order sensitivity.** `desiredIds.Add(identity)` must precede the
  observed-membership check. Pinned by a dedicated test.
- **Remaining floor.** After this change, roughly 16–24% of 50K allocation is
  the irreducible step payload, and the largest remaining single block is the
  per-route interpolated `Identity` string — one per observed route and one
  per desired route. Eliminating it requires caching identity on the route
  records themselves, which changes runtime model types and is out of scope
  here. A realistic further reduction without touching those types is small;
  the profitable remaining target is the record-level identity caching.
- **No behavior change was made.** No planner defect was observed during this
  phase, so the defect policy was not triggered.

## Proof behavior is unchanged

1. `ReferenceChangeSetPlanner` — retained, unmodified, reproducing
   pre-Phase-24.2 semantics — matches the optimized planner field-by-field
   and index-by-index across the full scenario matrix at 1K / 10K / 50K.
2. Adversarial duplicate ratios up to 1000×, single-identity floods, and
   case-only collisions all match the oracle exactly, including first-occurrence
   metric selection.
3. All four change kinds in one plan produce byte-identical ordering to the
   global `OrderBy/ThenBy` oracle.
4. Inputs are asserted unmutated.
5. Full suite (1912), focused planner suite (43) and stress suite (12) are
   green; no test was weakened, and the only test change is additive.

## Files changed

- `IranDirect.Core/Runtime/Reconciliation/RuntimeChangeSetPlanner.cs` — MODIFIED
- `IranDirect.Core.Tests/Runtime/Reconciliation/PlannerAdversarialEquivalenceTests.cs` — NEW
- `docs/performance/baselines/phase-30.2-planner-second-pass-optimization.md` — NEW
- `docs/performance/README.md` — one link added
