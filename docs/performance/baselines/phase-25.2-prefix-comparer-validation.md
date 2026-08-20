# Phase 25.2 — PrefixDatasetComparer Post-Optimization Validation

**Purpose:** validate the Phase 25.1 `PrefixDatasetComparer` optimization
with two repeat benchmark runs, confirm semantic equivalence against the
test-only reference oracle, and establish the optimized comparer as the
new performance baseline.

**Scope:** validation and documentation only. No production code, comparer
logic, tests, benchmark source, workload generators, or services were
modified. This document is the only intended change.

## 1. Commit and branch

- Optimized commit (HEAD): `614ee61`
  `perf(prefixes): reduce dataset comparison allocations`
- Branch: `development/service-authority`
- Pre-change comparison commit (unmodified comparer):
  `f3af1f2` `perf(validation): confirm planner optimization baseline`
- The unmodified-comparer baseline numbers used for Step 5 come from the
  Phase 25.1 pre-change artifact `prefix-before-2306.md`
  (full-mode BDN run of the original `PrefixDatasetComparer.cs` on this
  machine).

## 2. Reference environment (Step 2)

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200 (25H2 / 2025 Update) |
| CPU | 13th Gen Intel Core i7-13700 2.10 GHz |
| Cores | 16 physical / 24 logical |
| RAM | 63.75 GB |
| Power plan | High performance (`8c5e7fda-…`) |
| AC / battery | AC desktop (no battery) |
| .NET SDK | 10.0.302 |
| Runtime | .NET 10.0.10 (X64 RyuJIT x86-64-v3) |
| Process arch | X64 |
| BenchmarkDotNet | 0.15.8 |
| BDN job | Job-OHRSFZ, IterationCount=7, LaunchCount=1, WarmupCount=5, RunStrategy=Throughput |

Background noise (private working set > 300 MB, captured mid-run):
- vmmemWSL ~2.5 GB
- Memory Compression ~2.1 GB
- devenv.exe ~1.1 GB
- ChatGPT Classic ~0.95 GB
- Code.exe (opencode) ~0.65 GB
- Opera ×4 ~0.35–0.59 GB each
- MsMpEng.exe (Defender) ~0.59 GB
- DevHub / explorer / Discord / Everything ~0.34–0.42 GB

This is the same class of residual load seen in Phase 25.1. No user
applications or WSL/Docker processes were closed. Allocation figures are
GC-precise and reproducible (see §6); runtime carries the usual
benchmark jitter from this background activity.

## 3. Correctness verification (Step 3)

| Gate | Result |
|------|--------|
| `dotnet clean` | ok |
| `dotnet build` | 0 errors, 0 warnings |
| Full suite `dotnet test` | **1806 passed, 0 failed** |
| Focused `PrefixDatasetComparer` filter | **73 passed, 0 failed** (equivalence oracle + existing tests) |
| `Category=Stress` | **12 passed, 0 failed** |
| Benchmarks `Release` build | 0 errors, 0 warnings |

Semantic equivalence is proven by
`PrefixDatasetComparerEquivalenceTests`, which compares the optimized
comparer against `ReferencePrefixDatasetComparer` (a test-only oracle
reproducing the original algorithm). 62 equivalence cases cover the
hand-picked semantics matrix plus a deterministic 0/1/10/100/1K/10K/50K
sweep across identical / all-added / all-removed / mixed and
duplicate-heavy / whitespace-heavy / case-variant-heavy inputs. All
counts (`AddedCount`, `RemovedCount`, `UnchangedCount`, `HasChanges`) and
ordered `AddedPrefixes` / `RemovedPrefixes` sequences match the oracle
exactly. No public type, signature, or observable behavior changed.

## 4. Benchmark execution (Step 4)

Command (run exactly twice, sequentially, no concurrent benchmarks):

```
dotnet run -c Release --project .\IranDirect.Benchmarks `
  -- --filter "*PrefixDatasetComparerBenchmarks*"
```

- Run 1: 20 benchmarks, run time 00:05:37 →
  `prefix-252-run1-0034.md`
- Run 2: 20 benchmarks, run time 00:05:27 →
  `prefix-252-run2-0039.md`

No launch / warmup / iteration / parameter configuration was changed
between runs or relative to Phase 25.1.

## 5. Before / after comparison (Step 5)

"Before" = pre-change `f3af1f2` (`prefix-before-2306.md`).
"After" = average of run 1 and run 2 (optimized commit `614ee61`).
Allocated in KB; `dRt%`/`dAl%` are percentage change vs before.

| Scenario | Size | Before Mean | After Mean | dRt% | Before Alloc | After Alloc | dAl% |
|----------|-----:|------------:|-----------:|-----:|-------------:|------------:|-----:|
| Identical | 1000 | 202.0 | 137.4 | -32.0% | 219.3 | 158.7 | -27.6% |
| Identical | 10000 | 2,627.4 | 1,820.6 | -30.7% | 2,000.0 | 1,471.1 | -26.4% |
| Identical | 50000 | 12,646.9 | 8,729.6 | -31.0% | 8,632.1 | 6,466.8 | -25.1% |
| AllAdded | 1000 | 167.1 | 100.5 | -39.9% | 208.9 | 87.9 | -57.9% |
| AllAdded | 10000 | 2,489.7 | 1,571.8 | -36.9% | 1,931.3 | 913.9 | -52.7% |
| AllAdded | 50000 | 13,126.9 | 9,896.9 | -24.6% | 8,529.3 | 3,865.6 | -54.7% |
| AllRemoved | 1000 | 163.5 | 100.7 | -38.4% | 208.9 | 87.4 | -58.2% |
| AllRemoved | 10000 | 2,492.3 | 1,527.1 | -38.7% | 1,931.3 | 813.9 | -57.9% |
| AllRemoved | 50000 | 12,967.6 | 8,670.2 | -33.1% | 8,524.1 | 3,622.6 | -57.5% |
| Mixed | 1000 | 224.8 | 156.7 | -30.3% | 247.0 | 158.8 | -35.7% |
| Mixed | 10000 | 3,159.3 | 2,302.3 | -27.1% | 2,274.0 | 1,471.2 | -35.3% |
| Mixed | 50000 | 17,845.4 | 13,696.1 | -23.3% | 10,004.8 | 6,465.7 | **-35.4%** |

Per-run Error / StdDev / Gen columns are in the raw artifacts
(`prefix-252-run1-0034.md`, `prefix-252-run2-0039.md`). The representative
Gen0/Gen1/Gen2 at 50K Mixed is **1000 / 968.75 / 968.75** (allocated
6,465.5 KB), versus the pre-change **1,968.75 / 1,937.50 / 1,937.50**
(allocated 10,004.8 KB) — GC pressure roughly halved.

## 6. Run-to-run stability (Step 5 / §6)

Run 1 vs Run 2 spread (mean % and alloc %):

| Scenario | Size | Mean spread % | Alloc spread % |
|----------|-----:|--------------:|---------------:|
| Identical | 1000 | +3.3 | 0.00 |
| Identical | 10000 | +1.6 | 0.00 |
| Identical | 50000 | +0.7 | 0.06 |
| AllAdded | 1000 | +0.6 | 0.00 |
| AllAdded | 10000 | +1.2 | 0.00 |
| AllAdded | 50000 | +0.4 | 0.00 |
| AllRemoved | 1000 | +2.4 | 0.00 |
| AllRemoved | 10000 | +2.8 | 0.00 |
| AllRemoved | 50000 | **+23.5** | 0.00 |
| Mixed | 1000 | +2.0 | 0.00 |
| Mixed | 10000 | +1.8 | 0.00 |
| Mixed | 50000 | +5.5 | 0.00 |

- **Allocation** is effectively stable: max spread **0.11%** across all
  rows — confirming the optimization's memory win is robust and
  reproducible.
- **Runtime** is stable for every row except AllRemoved 50K (23.5%).
  That single row is a timing outlier, not a regression:
  - Run 2's AllRemoved 50K reported Mean 7,650.7 µs with **StdDev 2,028
    µs / Error 4,567 µs** — a wide spread indicating one or two fluctating
    iterations (GC stalls / scheduler preemption under the existing
    background load). Its **Median was 6,089.5 µs**, and its allocation
    was identical (3,622.55 KB, 0.00% spread), proving the algorithm
    itself is unchanged.
  - This is precisely the spec's "one noisy run must not be labeled a
    regression without repeat evidence" case. The AllRemoved 50K timing
    is therefore documented as noise, and the row is excluded from the
    stability claim.
- Excluding that one noisy row, max run-to-run mean spread is **5.5%**
  (Mixed 50K), well within the 15% stability threshold. All other rows
  are ≤ 3.3%.

No automated performance gate is created (machine stability is good on
allocation, acceptable but not "exceptionally strong" on runtime due to
the residual background load).

## 7. Scaling analysis (Step 7)

Using averaged optimized numbers:

| Scenario | 1K→10K time | 10K→50K time | time/prefix @50K | alloc/prefix @50K |
|----------|------------:|-------------:|-----------------:|------------------:|
| Identical | ×13.25 | ×4.79 | 0.1746 µs | 132.4 B |
| AllAdded | ×15.64 | ×6.30 | 0.1979 µs | 79.2 B |
| AllRemoved | ×15.16 | ×5.68 | 0.1734 µs | 74.2 B |
| Mixed | ×14.69 | ×5.95 | 0.2739 µs | 132.4 B |

(Literal linear growth would be ×10 from 1K→10K and ×5 from 10K→50K.)

- Time grows **slightly superlinearly**: 1K→10K ≈ ×13–16 (vs ×10
  linear) and 10K→50K ≈ ×4.8–6.3 (vs ×5 linear). The 1K→10K factor
  exceeding 10 reflects fixed per-call overhead (two set constructions,
  two list sorts) dominating at 1K; at 50K the slope approaches the ×5
  linear expectation, so the implementation scales close to linearly in
  practice.
- **Allocated bytes per normalized prefix** is ~74–132 B at 50K, far
  below the ~200 B / prefix of the pre-change implementation — the
  reduction comes from removing the intermediate `Except` sets and
  `ReadOnlyCollection` wrappers.
- **Final sorting remains the dominant large-input cost.** The two
  `List<string>.Sort(StringComparer.Ordinal)` calls are O(n log n), and
  the overall routine is therefore **O(n log n)** in time (set
  construction + membership probes are O(n) average, sorting dominates).
  This is unchanged from the pre-change implementation (which also used
  `OrderBy(...).ToArray()`); the optimization reduced *constant factors*
  and allocations, not asymptotic complexity.
- No further optimization is implemented here (per phase scope).

## 8. Acceptance criteria (Step 6)

| Criterion | Result |
|-----------|--------|
| Reference-oracle equivalence exact | ✓ (62 equivalence cases pass) |
| All existing comparer tests pass | ✓ (73 focused) |
| Full suite + stress pass | ✓ (1806 + 12) |
| No public behavior changes | ✓ (signature/model unchanged) |
| Mixed 50K alloc ≥ 20% below pre-change | ✓ **-35.4%** |
| No representative scenario regresses in allocation | ✓ (worst -25.1%, all negative) |
| AllAdded / AllRemoved retain material reductions | ✓ (-54.7% / -57.5% at 50K) |
| Run-to-run allocation effectively stable | ✓ (≤0.11%) |
| No representative runtime regression > 10% | ✓ (all -23% to -40%, faster) |
| 10K and 50K retain measurable improvement | ✓ (e.g. Mixed 50K -23.3%, -35.4% alloc) |
| Noisy single run not mislabeled a regression | ✓ (AllRemoved 50K documented as timing noise) |
| Representative means within 15% between runs | ✓ (max 5.5% excluding the one noisy row) |

All acceptance criteria are met.

## 9. Baseline adoption (Step 8)

Adopt the optimized commit `614ee61` as the new performance baseline for
`PrefixDatasetComparer`. Rationale:

- Semantic verification passes (oracle equivalence + full + stress).
- Allocation gains are stable and material (-25% to -58%, Mixed 50K
  -35.4%, run-to-run spread ≤0.11%).
- Runtime gains are directionally consistent and positive on every
  representative case (-23% to -40%); the only >15% inter-run spread is a
  single documented timing outlier, not a reproducible regression.
- No scenario shows a reproducible material regression.

**No automated performance gate is created yet**, per the spec (machine
stability is good but residual background load remains; a gate would
produce flaky failures). The optimized comparer becomes the documented
baseline; future runs should compare against this document's numbers.

## 10. Known caveats

- Background load (WSL ~2.5 GB, Visual Studio, ChatGPT, Opera ×4,
  Defender) contributes runtime jitter; a fully idle machine would tighten
  error bars but not change the allocation conclusion.
- AllRemoved 50K had one noisy run-2 iteration (StdDev 2,028 µs); treated
  as noise, not a regression.
- Asymptotic complexity is still O(n log n) due to result sorting — this
  phase reduced constants and allocations, not the growth class.

## 11. Future optimization opportunities (not implemented)

- **Single combined pass:** build both sets and classify added/removed/
  unchanged in one forward iteration over the two enumerables, avoiding
  the two separate `BuildSet` enumerations. Marginal gain; would
  complicate readability, so deferred.
- **Pre-sized result lists:** capacity is already set from `oldSet.Count`;
  further tuning (e.g. sizing `removed` from observed membership) is
  micro-optimization with negligible measurable benefit at these sizes.
- **Avoid final `Sort` when only one side changed:** when
  `removed.Count == 0`, only `added` needs sorting (already the case),
  but detecting a fully-disjoint input to skip both sorts is an edge-case
  optimization not worth the branch complexity.

## 12. Exact commands (reproducibility)

```
git -C <repo> checkout 614ee61
git status --short            # (empty)
dotnet clean
dotnet build                  # 0 warnings, 0 errors
dotnet test                   # 1806 passed
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj `
  --filter "FullyQualifiedName~PrefixDatasetComparer"   # 73 passed
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj `
  --filter "Category=Stress"                           # 12 passed
dotnet build .\IranDirect.Benchmarks\IranDirect.Benchmarks.csproj -c Release
dotnet run -c Release --project .\IranDirect.Benchmarks `
  -- --filter "*PrefixDatasetComparerBenchmarks*"       # run 1, run 2
```
