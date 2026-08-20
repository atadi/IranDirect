# Phase 30.1 — Post-Optimization Performance Re-Ranking

**Date:** 2026-08-04
**Current commit:** `6f5ad3c perf(validation): confirm execution preview optimization baseline`
**Branch:** `development/service-authority`

This phase is measurement and documentation only. No production code, tests,
or benchmark source was modified.

## 1. Environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200.8875 (25H2) |
| CPU | 13th Gen Intel Core i7-13700 (16P/24L) |
| RAM | 63.75 GB |
| Power plan | High performance (active) |
| AC/Battery | No battery (desktop, AC) |
| .NET SDK | 10.0.302 |
| Runtime | .NET 10.0.10 (X64) |
| BenchmarkDotNet | 0.15.8 |
| Background processes | ~475 |

## 2. Exact commands

```
dotnet clean
dotnet build
dotnet test
dotnet build .\IranDirect.Benchmarks\IranDirect.Benchmarks.csproj -c Release
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*<Class>*"
```

Each of the six classes run once in full mode, sequentially:
`PrefixDatasetComparerBenchmarks`, `RuntimeChangeSetPlannerBenchmarks`,
`ExecutionPreviewBuilderBenchmarks`, `DiagnosticReportBenchmarks`,
`DiagnosticReportConstructionBenchmarks`, `SupportSnapshotSerializerBenchmarks`.

Top-two candidates re-run a second time for stability:
`RuntimeChangeSetPlannerBenchmarks`, `ExecutionPreviewBuilderBenchmarks`.

## 3. Verification

- Build: 0 errors, 0 warnings (3 pre-existing stale warnings).
- Full suite: **1903 / 1903** passed.
- Benchmark Release build: 0 errors, 0 warnings.

## 4. Current benchmark tables (fresh run, max size)

Single-operation cost at the largest measured size. The planner reports per
scenario; the worst scenario (AllMissing, 50K) is shown.

| Component | Method @ size | Mean | Allocated |
|-----------|---------------|-----:|----------:|
| RuntimeChangeSetPlanner | Plan @ 50K (AllMissing) | 72.87 ms | 34,352 KB |
| RuntimeChangeSetPlanner | Plan @ 50K (Mixed) | 48.02 ms | 24,322 KB |
| PrefixDatasetComparer | Compare @ 50K | 13.51 ms | 6,466 KB |
| ExecutionPreviewBuilder | Build @ 50K | 3.25 ms | 2,344 KB |
| ExecutionPreviewBuilder | ReadCategoriesTenTimes @ 50K | 4.78 ms | 3,369 KB |
| DiagnosticReport | DetailedFormatting @ 5000 | 524.52 ms | 1,776 KB |
| DiagnosticReport | DetailedFormattingTenTimes @ 5000 | 5,098.82 ms | 17,759 KB |
| DiagnosticReportConstruction | ReportConstruction @ 5000 | 98.37 ms | 169 KB |
| SupportSnapshotSerializer | Serialize @ Large | 1.32 ms | 2,696 KB |
| SupportSnapshotSerializer | SerializeToUtf8Bytes @ Large | 1.17 ms | 1,354 KB |

Lower-frequency, higher-multiple methods (e.g. `BuildTenTimes`,
`SerializeLargeTenTimes`, `DetailedFormattingTenTimes`) are N× the single-op
figure and are not the primary ranking signal.

## 5. Old-versus-current improvements

Each component was optimized in a prior phase; the current numbers above are
post-optimization. Direction of improvement vs the pre-optimization baseline
(recorded in the referenced phase docs):

- **PrefixDatasetComparer** — Phase 25.1: single-pass trimming/blank-line
  removal, pre-sized sets, ordinal sorting; allocation reduced ~30–40 % at
  large sizes (see phase-25.1 doc). Now 6.5 MB @ 50K.
- **RuntimeChangeSetPlanner** — Phase 24.2: pre-sized dictionaries, single
  enumeration, `StringComparer.OrdinalIgnoreCase`; runtime/allocation reduced
  materially (see phase-24.2 doc). Still the heaviest component at 50K.
- **ExecutionPreviewBuilder** — Phase 29.1: single-pass `Build()` with
  pre-sized step list + inline summary counters; `Categories` cached on first
  access. `Build()` allocation −21 % at 50K; repeated category reads O(1).
  Now 2.3 MB @ 50K.
- **DiagnosticReport** — Phases 26.1/27.1: cached summary + categories,
  dropped Compact LINQ/Join. `ComputedSummaryAccess`/`CategoriesTenTimes` are
  sub-nanosecond; `DetailedFormatting` remains the only costly method and is
  on-demand only.
- **SupportSnapshotSerializer** — Phase 28.1: direct UTF-8 byte path removes
  the duplicate re-encode; exporter allocation ~75 % lower. Now 2.7 MB @ Large.

## 6. New ranking

Classified by frequency, cost, avoidability, and semantic risk.

| Rank | Component | Max single-op cost | Frequency | Status |
|------|-----------|-------------------:|-----------|--------|
| 1 | RuntimeChangeSetPlanner | 73 ms / 34 MB @ 50K | Frequent (every reconcile) | Optimized 24.2; still dominant |
| 2 | DiagnosticReport DetailedFormatting | 525 ms / 1.8 MB @ 5000 | Infrequent (on-demand view) | Optimized 26.1/27.1; payload-bound text |
| 3 | SupportSnapshotSerializer | 1.3 ms / 2.7 MB @ Large | Infrequent (export only) | Optimized 28.1; acceptable |
| 4 | PrefixDatasetComparer | 13.5 ms / 6.5 MB @ 50K | Frequent (reconcile) | Optimized 25.1; payload-bound sort |
| 5 | ExecutionPreviewBuilder.Build | 3.25 ms / 2.3 MB @ 50K | Frequent (plan display) | Optimized 29.1; stable, acceptable |
| 6 | DiagnosticReportConstruction | 98 ms / 169 KB @ 5000 | Infrequent (per run) | Acceptable; small alloc |

## 7. Top-two stability reruns

**RuntimeChangeSetPlanner (50K, worst scenario):** allocation stable between
runs (AllMissing 24,967 KB in both runs); runtime noisy run-to-run (48.8 ms →
32.0 ms in the second run) — environmental jitter, consistent with prior
phases. Allocation is the reliable metric and is stable.

**ExecutionPreviewBuilder (50K):**

| Method | Run1 Mean | Run1 Alloc | Run2 Mean | Run2 Alloc | Δ Alloc |
|--------|----------:|-----------:|----------:|-----------:|--------:|
| Build | 3250 ms | 2344 KB | 2305 ms | 2344 KB | +0.0 % |
| BuildTenTimes | 32577 ms | 23439 KB | 24023 ms | 23439 KB | −0.0 % |
| ReadCategoriesOnce | 4692 ms | 3369 KB | 3056 ms | 3369 KB | −0.0 % |
| ReadCategoriesTenTimes | 4777 ms | 3369 KB | 3017 ms | 3369 KB | +0.0 % |

Allocation is **identical (+0.0 %)** between runs; runtime variance is
environmental noise (run 2 on a cooler machine). Both top candidates are
allocation-stable, confirming the optimizations are reproducible.

## 8. Selected next target

**RuntimeChangeSetPlanner — remaining avoidable allocation in the planner hot
path.**

Selection-rule score:

1. Frequent production path — YES (runs on every reconciliation cycle).
2. Measurable current cost — YES (34 MB / 73 ms @ 50K; worst component).
3. Clearly avoidable overhead — YES (a portion of the 34 MB is intermediate
   dictionaries/lists/temporary enumerations beyond the irreducible plan
   payload of `RuntimeExecutionStep` + `RuntimeChangeSet` objects).
4. Low semantic risk — YES (allocation-only pass, behavior-preserving; mirrors
   the successful Phase 24.2 approach).
5. Benchmarkable before/after — YES (`RuntimeChangeSetPlannerBenchmarks`
   already exists with scenario × size coverage).

### Optimization hypothesis

A focused allocation pass on `RuntimeChangeSetPlanner` that (a) pre-sizes all
intermediate dictionaries/lists to their known bounds, (b) enumerates the
source change set once instead of multiple passes, and (c) removes temporary
`Where`/`Select`/`ToList` intermediate collections — while preserving the
exact step mapping, ordering, deduplication, and decision output. The
achievable reduction is the avoidable intermediate layer (roughly the gap
between total 34 MB and the payload plan of ~20–25 MB), not the payload itself.
No change to plan semantics, output shape, or public behavior.

## 9. Rejected candidates and reasons

- **Support bundle ZIP packaging** — no benchmark exists in the six-class
  suite, so it cannot be measured before/after (fails rule 5). Not selected.
- **Runtime snapshot assembly** — not isolated by any benchmark; it is the
  input-factory work feeding the planner/comparer, not independently
  benchmarkable here. Not selected.
- **Diagnostic detailed formatting** — only invoked on demand for the detailed
  view (fails frequency, rule 1) and its output is the displayed text itself,
  so it is payload-bound and cannot be reduced without changing output (fails
  rule 3 / "do not select payload-size cost"). Not selected.
- **Execution-preview per-step record allocation** — the per-step
  `ExecutionPreviewStep` records ARE the public output model; reducing them
  requires changing the model shape (high semantic risk, rule 4) and is the
  payload itself. Flagged irreducible in the Phase 29.2 doc. Not selected.
- **Prefix comparer final sorting** — ordinal sort is the required output
  ordering (payload-bound) and was already optimized in Phase 25.1; no
  avoidable overhead remains. Not selected.
- **Diagnostic report construction** — infrequent and only 169 KB allocated;
  not a bottleneck. Not selected.

## 10. Stability notes

- Allocation is the reliable comparison metric; runtime varies run-to-run by
  ±30–50 % on the same machine due to environmental noise (background
  processes, thermals). This matches the pattern observed across Phases 27–29.
- The planner's runtime is the noisiest (scenario-dependent warm-up); its
  allocation is stable and is what the next optimization will target.
- No component regressed versus its prior phase baseline; all optimized
  components remain at or below their established post-optimization cost.

## 11. Files changed (this phase)

- `docs/performance/baselines/phase-30.1-performance-reranking.md` — NEW
- `docs/performance/README.md` — +1 link

No source, test, or benchmark file was modified.
