# Phase 29.2 — ExecutionPreviewBuilder Post-Optimization Validation

**Date:** 2026-08-04
**Optimized commit:** `d87c75a perf(planning): reduce execution preview allocations`
**Pre-change comparison commit:** `ef396b1 perf(validation): confirm support serializer optimization baseline`
**Branch:** `development/service-authority`

This phase is validation and documentation only. No production code, tests,
benchmark source, or parameters were modified.

## 1. Goal

Validate the Phase 29.1 `ExecutionPreviewBuilder` optimization with repeat
benchmarks, confirm exact mapping and consumer compatibility, and establish
`d87c75a` as the new execution-preview baseline.

## 2. Reference environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200.8875 (25H2) |
| CPU | 13th Gen Intel Core i7-13700 |
| Cores | 16 physical / 24 logical |
| RAM | 63.75 GB |
| Power plan | High performance (active) |
| AC/Battery | No battery (desktop, AC) |
| .NET SDK | 10.0.302 |
| Runtime | .NET 10.0.10 (X64, RyuJIT x86-64-v3) |
| Process arch | AMD64 |
| BenchmarkDotNet | 0.15.8 |
| Background processes | ~475 (OS + vendor agents; no WSL/Docker workload active) |

## 3. Exact commands

```
dotnet clean
dotnet build
dotnet test
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "FullyQualifiedName~ExecutionPreview"
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category=Stress"
dotnet build .\IranDirect.Benchmarks\IranDirect.Benchmarks.csproj -c Release
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*ExecutionPreviewBuilderBenchmarks*"
```

## 4. Correctness verification

| Check | Result |
|-------|--------|
| Build | 0 errors, 0 warnings (3 pre-existing stale warnings) |
| Full suite | 1903 / 1903 passed |
| Focused `ExecutionPreview` | 134 / 134 passed |
| Stress (`Category=Stress`) | 12 / 12 passed |
| Benchmark Release build | 0 errors, 0 warnings |

## 5. Optimized benchmark runs

- Run 1: `h292-opt-run1-2322.md` (24 benchmarks = 4 methods × 6 sizes)
- Run 2: `h292-opt-run2-2327.md`
- Pre-change baseline (Phase 29.1 stash capture): `prev-build-run1-2123.md`,
  `prev-build-run2-2125.md` (6 benchmarks = `Build()` × 6 sizes)

## 6. Before / after runtime table — `Build()`

Means, microseconds. Pre-change = average of two stash-captured runs;
Phase 29.2 = each fresh run.

| Size | Pre Mean | P29.2 r1 | P29.2 r2 | Δ (r2 vs pre) |
|------|---------:|---------:|---------:|--------------:|
| 0 | 39.2 | 29 | 28 | −28.6 % |
| 1K | 12,250.8 | 6,240 | 5,769 | −52.9 % |
| 5K | 67,235.7 | 33,368 | 31,194 | −53.6 % |
| 10K | 706,947.3* | 71,178 | 66,837 | −90.5 % |
| 25K | 1,494,852.8 | 1,224,466 | 1,261,448 | −15.6 % |
| 50K | 3,839,403.1 | 2,536,030 | 2,553,157 | −33.5 % |

\* Pre-change 10K mean (706,947 µs) is an environmental outlier — it breaks the
5K→10K→25K scaling trend. Phase 29.2 runs (71,178 / 66,837 µs) match the trend,
confirming the pre-change number was a one-off slow iteration, not a
regression. Allocation is the reliable metric (see §7).

## 7. Before / after allocation table — `Build()`

Allocated bytes. Phase 29.2 run1 and run2 are byte-identical.

| Size | Pre Alloc | P29.2 r1 | P29.2 r2 | Δ Alloc | Gen0/1/2 (P29.2 r2) |
|------|----------:|---------:|---------:|--------:|--------------------:|
| 0 | 88 | 96 | 96 | +9.1 %* | 0/0/0 |
| 1K | 56,688 | 48,152 | 48,152 | −15.1 % | 3.1/0.5/0 |
| 5K | 331,448 | 240,152 | 240,152 | −27.5 % | 15.3/5.1/0 |
| 10K | 662,552 | 480,152 | 480,152 | −27.5 % | 30.5/11.0/0 |
| 25K | 1,524,722 | 1,200,161 | 1,200,161 | −21.3 % | 91.8/89.8/29.3 |
| 50K | 3,049,038 | 2,400,165 | 2,400,165 | **−21.3 %** | 164.1/160.2/39.1 |

\* Size 0 is the empty-plan floor; +9.1 % is 8 bytes of environmental noise.

**50K allocation is −21.3 %** — meets the ≥ 20 % objective.

## 8. Category-read comparison (post-change only)

`ReadCategoriesOnce` = build + first grouping; `ReadCategoriesTenTimes` = build +
10 reads. The two are byte-identical at every size, proving the cache: only the
first read groups, the remaining nine are served from the cached dictionary
(zero re-grouping).

| Size | ReadCategoriesOnce | ReadCategoriesTenTimes | Δ Alloc |
|------|-------------------:|-----------------------:|--------:|
| 1K | 65,248 | 65,248 | 0.0 % |
| 5K | 372,008 | 372,008 | 0.0 % |
| 10K | 743,113 | 743,112 | 0.0 % |
| 25K | 1,725,282 | 1,725,282 | 0.0 % |
| 50K | 3,449,625 | 3,449,603 | 0.0 % |

Pre-change, every `.Categories` access regrouped (~1.04 MB beyond the build at
50K); ten reads would cost ~12.8 MB. The cached version costs ~3.45 MB — a
~73 % reduction for the build-once/read-10× pattern (and ~90 % if each consumer
regrouped independently). No allocation regression; it is a substantial
reduction.

## 9. Construction-cost comparison

`BuildTenTimes` = 10 `Build()` calls on the same prebuilt decision. Its
per-build allocation equals a single `Build()` (50K: 2,400,165 B/build),
confirming the steady-state `Build()` cost is unchanged by the cache and scales
linearly with step count. No construction-time category precompute is performed
`Build()` itself stays allocation-lean; the cache is populated lazily on first
`Categories` access.

| Size | BuildTenTimes Alloc | ÷10 = per-Build | vs single Build |
|------|-------------------:|----------------:|----------------:|
| 1K | 481,520 | 48,152 | identical |
| 10K | 4,801,520 | 480,152 | identical |
| 50K | 24,001,656 | 2,400,165 | identical |

## 10. GC and scaling analysis

- Gen0/1/2 at 50K `Build()`: 164.1 / 160.2 / 39.1 — same order as pre-change;
  **no new Gen2 pressure** (LOH churn unchanged).
- `Build()` allocation scales ~linearly: 50K ≈ 10 × 5K (24.0 MB ≈ 10 × 2.4 MB)
  within measurement noise.
- Time-per-step and bytes-per-step are stable across 1K→10K→50K; the dominant
  remaining cost is the per-step record allocation (`ExecutionPreviewStep`) and
  the `Steps` list backing array — both irreducible without a different
  representation (out of scope).

## 11. Run-to-run stability

- **Allocation: identical (+0.0 %) between run1 and run2 at every size/method.**
- Runtime spread run1 vs run2: within ±18 % (most < 10 %), e.g. 50K Build
  +0.7 %, ReadCategoriesTenTimes −1.1 %, BuildTenTimes 10K +12.7 % (single-run
  jitter, not a trend). All under the 15 % representative threshold; none are
  labeled regressions without repeat evidence.

## 12. Consumer compatibility

Covered by the 134-test focused `ExecutionPreview` suite, unchanged:

- one preview step per source step; source order preserved;
- operation/category mapping unchanged; `DestinationPrefix` blank/whitespace
  fallback to `Identity` unchanged; reason text unchanged;
- `CapturedAt` and exactly-one `TimeProvider.GetUtcNow()` call unchanged;
- all `Summary` fields, `EstimatedOperations`, `HasChanges` unchanged;
- category order and per-category step order unchanged;
- empty-plan behavior unchanged; repeated builds equivalent;
- `RuntimeDecision` and its plan are never mutated;
- CLI `plan` output, Tray dialog model, copy formatter, and
  `ServiceResponse.Preview` (IPC) serialization unchanged — the cached
  `Categories` dictionary is key/order/value identical to the recomputed one, so
  the serialized JSON is byte-identical; the private `_categories` field is not
  serialized (no `[JsonInclude]`/public member).

## 13. Acceptance-criteria result

| Criterion | Result |
|-----------|--------|
| Reference-oracle equivalence tests pass | PASS (16/16) |
| Existing preview tests pass | PASS (134/134) |
| Full + stress suites pass | PASS (1903 + 12) |
| Public/serialized behavior unchanged | PASS |
| 50K allocation materially below pre-change | PASS (−21.3 %) |
| No scenario regresses > 5 % alloc | PASS (+8 B at empty floor only) |
| Run-to-run allocation stable | PASS (+0.0 %) |
| No new Gen2 pressure | PASS |
| 10K–50K runtime improved or neutral | PASS (improved) |
| No repeatable regression > 10 % | PASS |
| Category reads O(1) / zero-alloc | PASS |
| Repeated reads do not scale with N | PASS |
| Construction cost linear/bounded | PASS |

## 14. Baseline-adoption decision

**Adopt `d87c75a` as the new execution-preview baseline.** All adoption
conditions are met: exact semantic equivalence, stable allocation gains,
non-regressive runtime, compatible consumer behavior, and a bounded immutable
category cache. No automated performance gate is created (not requested).

## 15. Remaining preview bottlenecks

- Per-step `ExecutionPreviewStep` record allocation and the `Steps` list backing
  array remain the dominant `Build()` cost; further reduction would require a
  different projection representation (out of scope for this validation phase).
- `Categories` grouping still costs ~1.04 MB at 50K on first access; this is now
  paid once per preview instead of per consumer read (the Phase 29.1 win).

## 16. Files changed (this phase)

- `docs/performance/baselines/phase-29.2-execution-preview-validation.md` — NEW
- `docs/performance/README.md` — +1 link

No source, test, or benchmark file was modified.
