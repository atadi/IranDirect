# Phase 30.3 — RuntimeChangeSetPlanner second-pass post-optimization validation

Validation and documentation only. No production code, test, benchmark or
workload-generator source was modified in this phase.

## Commits

| Item | Value |
| --- | --- |
| Optimized commit (validated) | `b0b7484afb1f3fc36e54ff34991702058b7703df` |
| Optimized commit subject | `perf(planning): reduce remaining planner allocations` |
| Pre-change comparison commit | `4f0e444e8677f0f452744792608e1bde4c81d0a0` |
| Pre-change subject | `perf(baseline): rerank current performance bottlenecks` |
| Branch | `development/service-authority` |
| Working tree at start | clean (`git status --short` empty) |

Contents of the validated commit `b0b7484` — exactly four files, no
out-of-scope source:

```
IranDirect.Core/Runtime/Reconciliation/RuntimeChangeSetPlanner.cs        134 +-
IranDirect.Core.Tests/.../PlannerAdversarialEquivalenceTests.cs          327 +
docs/performance/README.md                                                 6 +
docs/performance/baselines/phase-30.2-...-optimization.md                397 +
```

## Environment

| Item | Value |
| --- | --- |
| OS | Microsoft Windows 11 Pro, 10.0.26200 Build 26200 (25H2) |
| CPU | 13th Gen Intel Core i7-13700, 2.10 GHz base |
| Cores | 16 physical / 24 logical, 1 socket |
| RAM | 65,277 MB (64 GB) |
| Power plan | **High performance** (`8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`) — already active, unchanged |
| .NET SDK | 10.0.302 (commit 35b593bebf) |
| .NET runtime / host | 10.0.10 |
| RID / architecture | `win-x64`, X64 RyuJIT x86-64-v3 |
| BenchmarkDotNet | 0.15.8 |
| BDN job | IterationCount=7, LaunchCount=1, WarmupCount=5, RunStrategy=Throughput (unchanged) |

### Significant background processes

The machine was a normal interactive desktop during measurement. Notable
resident workloads by working set: Memory Compression (~2.0 GB), ChatGPT
Classic (~1.3 GB), Windows Explorer (~699 MB), Opera (multiple processes,
~330–530 MB each), Microsoft Defender `MsMpEng` (~495 MB), Telegram
(~407 MB), Everything (~345 MB). A `tasklist` scan matched 28 entries for
`docker|wsl|vmmem`.

**No user application, WSL, Docker or other workload was terminated**, in
line with the phase restriction requiring explicit approval. This background
load is the primary source of the runtime jitter quantified below and is the
reason the runtime conclusions in this document are deliberately conservative.

## Exact commands

```
git status --short
git rev-parse HEAD
git branch --show-current
git log -5 --oneline

dotnet clean
dotnet build
dotnet test

dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj `
  --filter "FullyQualifiedName~RuntimeChangeSetPlanner|FullyQualifiedName~PlannerEquivalenceTests|FullyQualifiedName~PlannerAdversarialEquivalenceTests"

dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj `
  --filter "Category=Stress"

dotnet build .\IranDirect.Benchmarks\IranDirect.Benchmarks.csproj -c Release

dotnet run -c Release --project .\IranDirect.Benchmarks -- `
  --filter "*RuntimeChangeSetPlannerBenchmarks*"      # run 1

dotnet run -c Release --project .\IranDirect.Benchmarks -- `
  --filter "*RuntimeChangeSetPlannerBenchmarks*"      # run 2
```

## Correctness verification

| Check | Result |
| --- | --- |
| `dotnet clean` | succeeded, 0 warnings, 0 errors |
| `dotnet build` | 0 errors, **3 warnings** (see deviation below) |
| `dotnet test` (full suite) | **1912 passed, 0 failed, 0 skipped** (2 m 39 s) |
| Focused planner + equivalence + adversarial | **43 passed, 0 failed, 0 skipped** |
| `Category=Stress` | **12 passed, 0 failed, 0 skipped** (2 m 52 s) |
| Benchmarks Release build | **0 warnings, 0 errors** |

### Deviation from the "0 warnings" acceptance criterion

The phase brief requires 0 warnings. The solution build emits **3**, all
pre-existing and all in a single file unrelated to the planner:

- `IranDirect.Core.Tests/Diagnostics/DiagnosticReportEquivalenceTests.cs(296,13)`
  — `CS0219`: variable `summaryCount` assigned but never used.
- `IranDirect.Core.Tests/Diagnostics/DiagnosticReportEquivalenceTests.cs(213,9)`
  — `xUnit2013`: use `Assert.Single` instead of `Assert.Equal` for collection size.
- `IranDirect.Core.Tests/Diagnostics/DiagnosticReportEquivalenceTests.cs(257,14)`
  — `xUnit1031`: blocking task operation in a test method.

These predate Phase 30.2 (they are visible in the Phase 30.2 records) and are
untouched by the planner work. Clearing them would require editing a test
file, which §11 of this phase explicitly forbids. They are therefore reported
as a **known, accepted deviation** rather than silently fixed or silently
ignored. Recommend clearing them in a separate housekeeping change.

## Semantic confirmation

The test-only `ReferenceChangeSetPlanner` was verified to be **byte-identical
to its state at the pre-change commit**:

```
git diff --quiet 4f0e444 HEAD -- .../ReferenceChangeSetPlanner.cs   ->  identical
git log --oneline -3 -- .../ReferenceChangeSetPlanner.cs            ->  last touched in e263d05 (Phase 24.2)
```

It therefore remains a genuinely independent oracle reproducing
pre-Phase-24.2 semantics, not a copy of the implementation under test.

Confirmed preserved, by oracle comparison asserting step count, kind,
identity, destination prefix, gateway, interface index, metric, description
and index-by-index ordering:

- case-insensitive identity matching
- first-occurrence duplicate semantics
- desired / observed / inventory precedence
- endpoint and prefix separation
- gateway mismatch behavior
- interface mismatch behavior
- metrics and descriptions
- add / remove classification
- ownership semantics
- exact final step ordering
- all emitted step fields
- empty and blocked behavior
- input immutability

### Load-bearing invariant 1 — passes append in ascending `RuntimeChangeKind` order

`RuntimeChangeKind` declares `AddEndpointRoute, RemoveEndpointRoute,
AddPrefixRoute, RemovePrefixRoute` (0..3). The planner calls the four passes
in that exact order and captures a boundary after each
(`RuntimeChangeSetPlanner.cs`):

```
 46  AddMissingEndpointRoutes(...)
 52  int addEndpointEnd   = changes.Count;
 54  RemoveUndesiredOwnedEndpointRoutes(...)
 60  int removeEndpointEnd = changes.Count;
 62  AddMissingPrefixRoutes(...)
 68  int addPrefixEnd     = changes.Count;
 70  RemoveUndesiredOwnedPrefixRoutes(...)
 78  Changes = SortAuthoritative(changes, addEndpointEnd, removeEndpointEnd, addPrefixEnd)
105  SortByIdentity(ordered, 0,                 addEndpointEnd)
106  SortByIdentity(ordered, addEndpointEnd,    removeEndpointEnd)
107  SortByIdentity(ordered, removeEndpointEnd, addPrefixEnd)
108  SortByIdentity(ordered, addPrefixEnd,      ordered.Length)
```

Pinned by `AllFourKindsTogether_OrderingMatchesReference`, which builds a plan
containing all four kinds with interleaved identities and asserts exact
ordering equality against the global-`OrderBy` oracle.

### Load-bearing invariant 2 — `desiredIds.Add` precedes the observed check

Both add passes short-circuit in the required order:

```
175  if (!desiredIds.Add(identity) ||
176      observed.ContainsKey(identity))
...
237  if (!desiredIds.Add(identity) ||
238      observed.ContainsKey(identity))
```

The identity must enter the membership set even when the observed check
suppresses the add; otherwise an already-observed desired identity would
escape the set and the removal pass would emit a spurious removal. Pinned by
`DuplicateDesiredThatIsAlsoObserved_MatchesReference`.

### Adversarial suite — individual results

All nine executed and passed:

| Test | Result |
| --- | --- |
| `InputCollectionsAreNotMutated` | Passed (13 ms) |
| `HighDuplicateRatio_MatchesReference(2)` | Passed (10 ms) |
| `HighDuplicateRatio_MatchesReference(10)` | Passed (1 ms) |
| `HighDuplicateRatio_MatchesReference(100)` | Passed (8 ms) |
| `HighDuplicateRatio_MatchesReference(1000)` | Passed (138 ms) |
| `DuplicatesDifferingOnlyByCase_MatchesReference` | Passed (1 ms) |
| `DuplicateDesiredThatIsAlsoObserved_MatchesReference` | Passed (1 ms) |
| `AllDuplicatesOfSingleIdentity_MatchesReference` | Passed (5 ms) |
| `AllFourKindsTogether_OrderingMatchesReference` | Passed (6 ms) |

## Allocation — before versus after

Pre-change values are the Phase 30.2 baseline at `4f0e444` (both pre-runs
agreed exactly). Both validation runs are shown; the change column uses their
mean.

| Scenario | Size | Pre alloc | Run 1 | Run 2 | Δ vs pre | Run1↔Run2 spread |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| AllMissing | 1K | 502.84 KB | 327.74 KB | 327.74 KB | **−34.8%** | 0.000% |
| AllMissing | 10K | 5083.90 KB | 3377.49 KB | 3377.50 KB | **−33.6%** | 0.000% |
| AllMissing | 50K | 24967.67 KB | 16803.90 KB | 16803.38 KB | **−32.7%** | 0.003% |
| AllPresent | 1K | 291.70 KB | 211.63 KB | 211.63 KB | **−27.4%** | 0.000% |
| AllPresent | 10K | 2917.68 KB | 2102.92 KB | 2102.92 KB | **−27.9%** | 0.000% |
| AllPresent | 50K | 14892.28 KB | 10746.26 KB | 10746.26 KB | **−27.8%** | 0.000% |
| AllObsolete | 1K | 300.52 KB | 276.58 KB | 276.58 KB | −8.0% | 0.000% |
| AllObsolete | 10K | 3100.08 KB | 2865.06 KB | 2865.07 KB | −7.6% | 0.000% |
| AllObsolete | 50K | 15437.70 KB | 14263.57 KB | 14263.58 KB | −7.6% | 0.000% |
| Mixed | 1K | 480.72 KB | 357.76 KB | 357.76 KB | **−25.6%** | 0.000% |
| Mixed | 10K | 4969.53 KB | 3751.64 KB | 3751.64 KB | **−24.5%** | 0.000% |
| **Mixed** | **50K** | **24321.88 KB** | **18384.26 KB** | **18383.96 KB** | **−24.4%** | 0.002% |
| DuplicateInput | 1K | 686.36 KB | 431.52 KB | 431.52 KB | **−37.1%** | 0.000% |
| DuplicateInput | 10K | 6926.07 KB | 4405.04 KB | 4405.04 KB | **−36.4%** | 0.000% |
| DuplicateInput | 50K | 34351.98 KB | 22048.15 KB | 22048.15 KB | **−35.8%** | 0.000% |

Allocation is **effectively identical between the two optimized runs** — the
largest divergence anywhere is 0.003%, and 11 of 15 rows are bit-for-bit
equal. The Phase 30.2 figures reproduced exactly on an independent pair of
runs, so the allocation result is reproducible rather than a one-off.

## Runtime — before versus after, with jitter envelope

Reporting both pre-change runs alongside both optimized runs, because the
pre-change pair alone disagrees by 28–49% on **every** configuration. Quoting
a single "before" number would manufacture either an improvement or a
regression at will.

| Scenario | Size | Pre run1 | Pre run2 | Pre spread | Opt run1 | Opt run2 | Opt spread | Within pre envelope |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| AllMissing | 1K | 213.8 µs | 338.3 µs | 45% | 157.1 µs | 243.3 µs | 43% | faster than both |
| AllMissing | 10K | 4516.0 µs | 7049.5 µs | 44% | 3946.1 µs | 5274.5 µs | 29% | yes |
| AllMissing | 50K | 30837.7 µs | 47241.9 µs | 42% | 25231.5 µs | 34655.5 µs | 31% | faster than both |
| AllPresent | 1K | 173.0 µs | 261.8 µs | 41% | 146.7 µs | 234.2 µs | 46% | faster than both |
| AllPresent | 10K | 2749.1 µs | 4322.2 µs | 44% | 2933.2 µs | 2864.2 µs | 2% | yes |
| AllPresent | 50K | 19831.6 µs | 29133.8 µs | 38% | 23906.9 µs | 24354.5 µs | 2% | yes |
| AllObsolete | 1K | 199.0 µs | 329.0 µs | 49% | 313.3 µs | 304.9 µs | 3% | yes |
| AllObsolete | 10K | 4524.6 µs | 5999.0 µs | 28% | 5945.3 µs | 5920.3 µs | 0.4% | yes |
| AllObsolete | 50K | 24019.9 µs | 35598.1 µs | 39% | 36609.6 µs | 36458.3 µs | 0.4% | yes |
| Mixed | 1K | 274.9 µs | 426.4 µs | 43% | 353.0 µs | 355.6 µs | 0.7% | yes |
| Mixed | 10K | 4673.6 µs | 7119.5 µs | 41% | 5715.1 µs | 5708.2 µs | 0.1% | yes |
| Mixed | 50K | 36924.5 µs | 54468.3 µs | 38% | 49435.5 µs | 46944.9 µs | 5% | yes |
| DuplicateInput | 1K | 291.2 µs | 481.0 µs | 49% | 302.9 µs | 305.5 µs | 0.9% | yes |
| DuplicateInput | 10K | 6131.0 µs | 9688.4 µs | 45% | 5938.6 µs | 5900.4 µs | 0.6% | yes |
| DuplicateInput | 50K | 48764.4 µs | 76484.4 µs | 44% | 47519.1 µs | 48699.4 µs | 2% | yes |

**Interpretation.** In 12 of 15 configurations the optimized timings land
inside the interval spanned by the two pre-change runs of the *unmodified*
baseline binary; in the remaining 3 the optimized code is faster than both
pre-change runs. Consequently:

- No repeatable runtime regression greater than 10% is demonstrated. Every
  apparent regression measured against the *faster* pre-run reverses sign when
  measured against the *slower* pre-run — e.g. AllObsolete 50K reads "+52%"
  against pre-run1 but "−7%" against pre-run2, and Mixed 50K reads "+27%"
  against pre-run1 but "−14%" against pre-run2.
- Equally, **no fine-grained runtime improvement is claimed.** The only
  runtime statements this document supports are the three configurations
  faster than both pre-change runs (AllMissing 1K/50K, AllPresent 1K), and
  even those sit close to the jitter band.
- The optimized runs are markedly *more self-consistent* than the pre-change
  runs on the desired-heavy scenarios (Mixed and DuplicateInput spreads drop
  to 0.1–5%, versus 38–49% pre-change), which is consistent with lower GC
  pressure but is not by itself proof of a speedup.

The reliable, reproducible result of this phase is the **allocation and GC**
reduction, not a runtime number.

## GC comparison

Gen0 / Gen1 / Gen2 collections per 1000 operations, pre-change versus
optimized run 1 (run 2 agrees to within one bucket):

| Scenario | Size | Pre Gen0/1/2 | Opt Gen0/1/2 |
| --- | ---: | --- | --- |
| AllMissing | 1K | 32.71 / 17.33 / — | 21.24 / 10.99 / — |
| AllMissing | 10K | 421.88 / 351.56 / 210.94 | 250.00 / 246.09 / 89.84 |
| AllMissing | 50K | 2428.57 / 2357.14 / 1214.29 | 1468.75 / 1437.50 / 531.25 |
| AllPresent | 1K | 19.04 / 9.28 / — | 13.67 / 5.13 / — |
| AllPresent | 10K | 246.09 / 121.09 / 121.09 | 121.09 / 121.09 / 121.09 |
| AllPresent | 50K | 1062.50 / 1031.25 / 281.25 | 750.00 / 718.75 / 218.75 |
| AllObsolete | 1K | 19.53 / 6.10 / — | 17.82 / 6.59 / — |
| AllObsolete | 10K | 265.63 / 257.81 / 101.56 | 242.19 / 234.38 / 101.56 |
| AllObsolete | 50K | 1218.75 / 1187.50 / 468.75 | 1066.67 / 1000.00 / 333.33 |
| Mixed | 1K | 31.25 / 17.58 / — | 23.19 / 11.72 / — |
| Mixed | 10K | 398.44 / 390.63 / 195.31 | 328.13 / 164.06 / 164.06 |
| Mixed | 50K | 1857.14 / 1785.71 / 642.86 | 1384.62 / 1307.69 / 461.54 |
| DuplicateInput | 1K | 44.43 / 29.30 / — | 28.08 / 13.92 / — |
| DuplicateInput | 10K | 531.25 / 460.94 / 265.63 | 285.16 / 140.63 / 140.63 |
| DuplicateInput | 50K | 2636.36 / 2545.45 / 909.09 | 1615.38 / 1538.46 / 461.54 |

**No configuration gained Gen2 pressure.** Gen2 fell in every configuration
that had any, most sharply for AllMissing 50K (1214 → 531, −56%) and
DuplicateInput 50K (909 → 462, −49%).

## Run-to-run stability

- **Allocation: exact.** Maximum divergence between optimized run 1 and run 2
  is 0.003% (AllMissing 50K, 16803.90 vs 16803.38 KB); most rows are
  identical to the last reported digit. Allocation conclusions are not
  noise-sensitive.
- **Runtime: high jitter, host-dominated.** Pre-change runs of identical
  binaries differed by 28–49% per configuration; optimized runs differed by
  0.1–46%. Highest observed instability: AllPresent 25K in run 1 reported
  StdDev 1548.63 µs on a 9180.9 µs mean (~17%), and AllPresent 1K spread 46%
  between runs. This is attributed to the documented interactive background
  load, which was deliberately left running.
- Because the measurement floor is coarser than any plausible runtime delta
  from this change, runtime is treated as **non-regressed** rather than
  improved.

## Scaling analysis

### Allocation per route (bytes per input route, optimized)

| Scenario | 1K | 10K | 50K |
| --- | ---: | ---: | ---: |
| AllMissing | 335.6 B | 345.9 B | 344.1 B |
| AllPresent | 216.7 B | 215.3 B | 220.1 B |
| AllObsolete | 283.2 B | 293.4 B | 292.1 B |
| Mixed | 366.3 B | 384.2 B | 376.5 B |
| DuplicateInput | 441.9 B | 451.1 B | 451.5 B |

Per-route allocation is flat across a 50× size range in every scenario
(variation ≤ 6%), confirming the optimization is **O(n) with a reduced
constant** and introduces no size-dependent overhead. The pre-change
per-route figures were correspondingly flat but higher (e.g. Mixed 50K:
498.1 B → 376.5 B).

### Runtime growth

Given the jitter documented above, growth ratios are reported from optimized
run 2 (the more internally consistent run) purely as an order-of-growth
sanity check, not as performance claims:

| Scenario | 1K→10K | 10K→50K | Expected if linear |
| --- | ---: | ---: | ---: |
| AllMissing | 21.7× | 6.6× | 10× / 5× |
| AllPresent | 12.2× | 8.5× | 10× / 5× |
| AllObsolete | 19.4× | 6.2× | 10× / 5× |
| Mixed | 16.1× | 8.2× | 10× / 5× |
| DuplicateInput | 19.3× | 8.3× | 10× / 5× |

Growth is super-linear at small scale and closer to linear at large scale.
This is the expected signature of GC amortization and cache effects rather
than algorithmic non-linearity — the algorithm is hash-lookup plus
partitioned sorts, i.e. O(n log n) worst case dominated by O(n) hashing, and
the per-route allocation table above is flat.

### Duplicate-heavy input

`DuplicateInput` retains the largest allocation win at every scale (−37.1% /
−36.4% / −35.8%) and shows no scale-dependent degradation. Removing the
second identity materialization pays twice under duplication, exactly as the
Phase 30.2 attribution predicted. Its runtime is also the most stable of any
scenario post-change (0.6–2% spread).

### AllObsolete — sort-change-only scenario

`AllObsolete` has no desired input, so it never paid the duplicate-identity
cost and benefits only from replacing the LINQ sort pipeline. Its improvement
is correspondingly small and consistent: −8.0% / −7.6% / −7.6%. At 50K that
is 1174 KB saved against a measured pre-change sort-pipeline cost of
~1.60 MB — the right order of magnitude, with the remainder being the LINQ
buffer that the exact-size array copy still has to pay in some form. This
scenario is the clean control proving the sort change is real and is not
merely a side effect of the identity change.

### Remaining gap above the output-payload floor

Using the Phase 30.2 measured floor (final step records plus the exact-size
backing array):

| Scenario (50K) | Steps | Payload floor | Optimized total | Floor share | Gap |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mixed | 45,000 | ~3.09 MB | ~17.95 MB | ~17% | ~14.86 MB |
| AllMissing | 50,000 | ~3.43 MB | ~16.41 MB | ~21% | ~12.98 MB |
| AllObsolete | 50,000 | ~3.43 MB | ~13.93 MB | ~25% | ~10.50 MB |
| DuplicateInput | 50,000 | ~3.43 MB | ~21.53 MB | ~16% | ~18.10 MB |
| AllPresent | 0 | ~24 B | ~10.49 MB | ~0% | ~10.49 MB |

### Do computed `Identity` strings now dominate the remainder?

Yes. `AllPresent 50K` is the decisive case: it produces **zero** steps, so its
payload floor is 24 bytes, yet it still allocates ~10.49 MB. That allocation
is almost entirely one interpolated `Identity` string per observed route plus
one per desired route (50K + 50K), together with the dictionary and hash-set
backing storage. The Phase 30.2 attribution measured ~4.25 MB of observed
identity strings and ~3.62 MB on the desired side for comparable inputs,
which brackets this figure.

After the second-pass optimization each identity is materialized exactly
once, so what remains is the irreducible-under-current-types cost:
`Identity` is a computed property
(`$"{DestinationPrefix}|{Gateway}|{InterfaceIndex}"`) that allocates a fresh
string on every read. **Record-level identity caching is therefore the
single largest remaining opportunity — and, per §8, it is explicitly not
implemented in this phase.**

## Baseline adoption decision

**Adopted.** `b0b7484` becomes the new planner performance baseline.

| Adoption gate | Status |
| --- | --- |
| Semantic equivalence remains exact | Met — unmodified oracle matches field-by-field and index-by-index across the full matrix plus adversarial cases |
| Allocation gains reproducible | Met — two independent runs reproduce Phase 30.2 figures to ≤0.003% |
| No reproducible material regression | Met — all runtime deltas lie inside the pre-change jitter envelope; allocation improved everywhere |
| Ordering invariants test-pinned | Met — `AllFourKindsTogether_OrderingMatchesReference` and `DuplicateDesiredThatIsAlsoObserved_MatchesReference` green |
| GC pressure equal or lower | Met — Gen2 down in every configuration, up in none |

### Acceptance criteria results

| Criterion | Result |
| --- | --- |
| Reference equivalence exact | **Pass** |
| Adversarial tests green | **Pass** (9/9) |
| Full suite + stress pass | **Pass** (1912 + 12) |
| No executor-visible behavior change | **Pass** — planner output identical to oracle; no executor/routing/IPC code touched |
| Mixed 50K ≥15% below pre-change allocation | **Pass — −24.4%** |
| DuplicateInput materially lower | **Pass — −35.8%** |
| No normal scenario allocation regression >5% | **Pass** — every scenario improved; smallest gain −7.6% |
| Allocation effectively identical between runs | **Pass** — ≤0.003% |
| No additional Gen2 pressure | **Pass** |
| No repeatable runtime regression >10% | **Pass** — all deltas within pre-change jitter envelope |
| Jitter documented from individual run data | **Pass** — both runs tabulated per side |
| No fine-grained runtime claims below spread | **Pass** — explicitly withheld |
| Exact output order matches reference | **Pass** |
| 0 build warnings | **Deviation** — 3 pre-existing unrelated test warnings; cannot be fixed without violating §11 |

No automated performance gate was created; that remains unapproved.

## Future opportunity — record-level identity caching

The remaining allocation is dominated by repeated materialization of the
computed `Identity` property on `ObservedRoute`, `DesiredPrefixRoute` and
`DesiredEndpointRoute`. Caching it on the record (e.g. a lazily initialized
backing field, or computing it once at construction) would remove roughly one
string allocation per input route per plan.

Risks that must be handled before attempting it:

- **Scope.** It changes runtime model types, which several phases have
  deliberately kept out of scope; those records are consumed by routing,
  inventories, IPC and persistence.
- **Serialization.** A cached backing field must not leak into JSON contracts
  or persisted inventory shapes; the support-snapshot serializer work in
  Phase 28.x makes byte-level output contracts load-bearing.
- **Record semantics.** These are `sealed record` types with `init`
  properties and value equality; adding mutable cached state interacts with
  `with`-expression copying and generated equality members, and must not make
  a copied record report a stale identity.
- **Thread safety.** Lazy initialization must be benign-racy (idempotent
  computation, single reference write) since snapshots may be shared.
- **Measurement honesty.** The benefit is allocation, not necessarily
  wall-clock, and this host cannot resolve runtime deltas below ~30%; any
  such phase should be judged on allocation and GC, as this one was.

## Files changed in this phase

- `docs/performance/baselines/phase-30.3-planner-second-pass-validation.md` — NEW
- `docs/performance/README.md` — one link added

No production, test, benchmark or workload-generator source was modified.
Previous baseline documents were not altered.
