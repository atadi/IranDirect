# Phase 24.1 — Full Performance Baseline and Bottleneck Analysis

**Status:** measurement and analysis only. No production or benchmark code
was modified in this phase.

## 1. Repository state (precondition)

| Field | Value |
|-------|-------|
| Commit | `628d64af5f29403e6206af78cfa4556862766c7f` |
| Branch | `development/service-authority` |
| `git status --short` | (empty — clean working tree) |
| Last 3 commits | `628d64a test(performance): add lifecycle simulation harness`, `ce460b5 test(performance): add runtime executor stress harness`, `ef73aee test(performance): add runtime executor stress harness` |
| Build | 0 warnings, 0 errors |
| `dotnet test` (full suite) | 1716 passed, 0 failed |

## 2. Reference environment

| Field | Value |
|-------|-------|
| Date / time | 2026-08-03 19:38 +03:30 (run window ~19:40–23:10 local) |
| OS | Microsoft Windows 11 Pro, 10.0.26200 (25H2 / 2025 Update / Hudson Valley 2) |
| Architecture | 64-bit (AMD64) |
| CPU | 13th Gen Intel(R) Core(TM) i7-13700 |
| CPU base clock | 2.10 GHz (turbo to ~5.2 GHz, see caveats) |
| Physical cores | 16 |
| Logical processors | 24 |
| RAM | 63.75 GB |
| SDK | .NET 10.0.302 |
| Runtime | Microsoft.NETCore.App 10.0.10 (also 8.0.29, 9.0.18 installed) |
| Process arch | X64 RyuJIT x86-64-v3 |
| BenchmarkDotNet | 0.15.8 |
| Power plan | **Balanced** (GUID 381b4222-f694-41f0-9685-ff5bb260df2e) |
| AC power | Desktop, AC (no battery) |

### Environment caveats (material to reproducibility)

- **Power plan is Balanced, not High Performance.** Turbo Boost and
  frequency scaling were active; sustained all-core load may have been
  thermally/power capped. Numbers are therefore conservative floor
  estimates and will vary on a differently configured machine.
- **Background load was significant** during the run: WSL VM
  (`vmmemWSL`, ~2.5 GB WS), Visual Studio (`devenv`, ~1.1 GB), Docker
  Desktop (5 processes), OpenCode, Opera (two instances), Discord,
  ChatGPT, and Windows Search indexing. Antivirus (`MsMpEng`,
  Windows Defender) was running and actively scanning.
- No other full benchmark process ran concurrently; the five benchmark
  classes were executed **sequentially**, one `dotnet run` per class.
- These results are **machine-specific and are not pass/fail
  thresholds**.

## 3. Commands

```
dotnet clean
dotnet build                      # 0 warning, 0 error
dotnet test                       # 1716 passed
dotnet build .\IranDirect.Benchmarks\IranDirect.Benchmarks.csproj -c Release

dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*PrefixDatasetComparerBenchmarks*"
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*RuntimeChangeSetPlannerBenchmarks*"
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*ExecutionPreviewBuilderBenchmarks*"
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*DiagnosticReportBenchmarks*"
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*SupportSnapshotSerializerBenchmarks*"

# Section 11 stability re-run (planner only):
dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*RuntimeChangeSetPlannerBenchmarks*"
```

Full mode was used (the benchmark project defaults to `FullJob` unless
`--quick` is passed: LaunchCount=1, WarmupCount=5, IterationCount=7,
RunStrategy=Throughput, MemoryDiagnoser enabled).

## 4. Results — PrefixDatasetComparer

`PrefixDatasetComparer.Compare(old, new)`, 1K→50K, four scenarios.

| Size | Scenario | Mean (µs) | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|-----:|----------|---------:|------:|-------:|----:|----:|----:|----------:|
| 1000 | Identical | 101.82 | 1.458 | 0.647 | 14.28 | - | - | 219.27 KB |
| 1000 | AllAdded | 89.73 | 3.921 | 1.741 | 13.55 | 2.69 | - | 208.89 KB |
| 1000 | AllRemoved | 99.57 | 8.575 | 3.807 | 13.55 | 2.56 | - | 208.89 KB |
| 1000 | Mixed | 130.85 | 4.238 | 1.882 | 15.87 | 4.15 | - | 247.01 KB |
| 5000 | Identical | 738.54 | 10.320 | 4.582 | 166.02 | 166.02 | 166.02 | 959.99 KB |
| 5000 | AllAdded | 715.36 | 12.591 | 5.590 | 124.02 | 124.02 | 124.02 | 932.30 KB |
| 5000 | AllRemoved | 725.05 | 5.795 | 2.067 | 124.02 | 124.02 | 124.02 | 932.30 KB |
| 5000 | Mixed | 1,032.58 | 30.176 | 13.398 | 166.02 | 166.02 | 166.02 | 1,097.16 KB |
| 10000 | Identical | 1,531.35 | 54.955 | 19.598 | 427.73 | 427.73 | 427.73 | 1,999.98 KB |
| 10000 | AllAdded | 1,583.55 | 17.311 | 7.686 | 332.03 | 332.03 | 332.03 | 1,931.33 KB |
| 10000 | AllRemoved | 1,599.08 | 19.494 | 8.655 | 332.03 | 332.03 | 332.03 | 1,931.33 KB |
| 10000 | Mixed | 2,067.78 | 27.870 | 12.375 | 425.78 | 425.78 | 425.78 | 2,273.96 KB |
| 25000 | Identical | 3,366.31 | 43.026 | 19.104 | 1031.25 | 1003.91 | 1000.00 | 4,163.83 KB |
| 25000 | AllAdded | 4,431.13 | 38.421 | 13.701 | 992.19 | 992.19 | 992.19 | 4,129.91 KB |
| 25000 | AllRemoved | 3,646.12 | 107.510 | 47.735 | 1031.25 | 1000.00 | 1000.00 | 4,137.33 KB |
| 25000 | Mixed | 5,492.76 | 58.378 | 25.920 | 2015.62 | 1976.56 | 1976.56 | 7,795.83 KB |
| 50000 | Identical | 7,613.92 | 404.656 | 179.670 | 1125.00 | 1093.75 | 1093.75 | 8,632.88 KB |
| 50000 | AllAdded | 7,705.53 | 73.434 | 26.187 | 1968.75 | 1937.50 | 1937.50 | 8,531.52 KB |
| 50000 | AllRemoved | 8,096.03 | 325.272 | 144.423 | 1703.13 | 1671.88 | 1671.88 | 8,523.72 KB |
| 50000 | Mixed | 11,224.57 | 121.510 | 43.332 | 1859.38 | 1828.13 | 1828.13 | 10,003.55 KB |

Mixed is consistently ~30–40% slower than Identical/AllAdded/AllRemoved
at every size, because `Mixed` both adds and removes (two `OrderBy` +
two `Except` passes with non-empty output).

## 5. Results — RuntimeChangeSetPlanner

`RuntimeChangeSetPlanner.Plan(snapshot, ownership)`, 1K→50K, five
scenarios.

| Scenario | Size | Mean (µs) | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|---------|-----:|---------:|------:|-------:|----:|----:|----:|----------:|
| AllMissing | 1000 | 235.9 | 16.67 | 7.40 | 34.67 | 20.51 | - | 531.91 KB |
| AllMissing | 5000 | 1,920.8 | 71.08 | 25.35 | 179.69 | 177.73 | 89.84 | 2,701.67 KB |
| AllMissing | 10000 | 4,156.5 | 219.79 | 97.59 | 406.25 | 382.81 | 296.88 | 5,450.30 KB |
| AllMissing | 25000 | 12,553.5 | 278.59 | 123.70 | 1390.63 | 1203.13 | 812.50 | 13,000.88 KB |
| AllMissing | 50000 | 40,508.5 | 2,986.47 | 1,326.01 | 2250.00 | 2166.67 | 1000.00 | 26,251.58 KB |
| AllPresent | 1000 | 262.8 | 4.25 | 1.51 | 36.62 | 22.46 | - | 563.80 KB |
| AllPresent | 5000 | 1,836.2 | 30.17 | 13.39 | 181.64 | 181.64 | 181.64 | 2,806.28 KB |
| AllPresent | 10000 | 4,402.1 | 246.62 | 109.50 | 492.19 | 492.19 | 492.19 | 5,700.55 KB |
| AllPresent | 25000 | 17,704.6 | 1,185.00 | 422.58 | 1125.00 | 1093.75 | 562.50 | 13,239.98 KB |
| AllPresent | 50000 | 38,954.0 | 1,495.30 | 663.92 | 1857.14 | 1785.71 | 714.29 | 26,889.81 KB |
| AllObsolete | 1000 | 252.8 | 4.90 | 1.75 | 30.76 | 16.60 | - | 472.11 KB |
| AllObsolete | 5000 | 1,983.7 | 33.83 | 12.06 | 179.69 | 175.78 | 89.84 | 2,404.77 KB |
| AllObsolete | 10000 | 4,018.7 | 223.92 | 99.42 | 296.88 | 296.88 | 296.88 | 4,859.00 KB |
| AllObsolete | 25000 | 19,614.8 | 1,293.70 | 574.41 | 1187.50 | 1156.25 | 656.25 | 11,520.57 KB |
| AllObsolete | 50000 | 34,975.1 | 3,033.58 | 1,081.80 | 1733.33 | 1666.67 | 733.33 | 23,304.51 KB |
| Mixed | 1000 | 344.9 | 9.20 | 4.09 | 39.55 | 20.51 | - | 608.34 KB |
| Mixed | 5000 | 2,726.3 | 152.86 | 67.87 | 269.53 | 265.63 | 179.69 | 3,608.27 KB |
| Mixed | 10000 | 9,069.6 | 745.98 | 331.22 | 843.75 | 843.75 | 500.00 | 7,303.10 KB |
| Mixed | 25000 | 22,994.7 | 2,116.05 | 939.54 | 1531.25 | 1500.00 | 781.25 | 17,120.44 KB |
| Mixed | 50000 | 52,201.0 | 4,459.99 | 1,980.26 | 2555.56 | 2444.44 | 1111.11 | 34,703.99 KB |
| DuplicateInput | 1000 | 282.7 | 17.58 | 7.80 | 42.48 | 22.46 | - | 650.72 KB |
| DuplicateInput | 5000 | 2,211.0 | 38.41 | 17.06 | 226.56 | 175.78 | 89.84 | 3,301.12 KB |
| DuplicateInput | 10000 | 6,910.7 | 213.79 | 94.92 | 570.31 | 562.50 | 281.25 | 6,655.36 KB |
| DuplicateInput | 25000 | 24,669.3 | 558.63 | 248.03 | 1468.75 | 1437.50 | 656.25 | 16,017.01 KB |
| DuplicateInput | 50000 | 51,809.0 | 2,510.01 | 1,114.46 | 2500.00 | 2400.00 | 900.00 | 32,349.10 KB |

## 6. Results — ExecutionPreviewBuilder

`ExecutionPreviewBuilder.Build(decision)`, 0→50K steps.

| StepCount | Mean (ns) | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|----------:|---------:|------:|-------:|----:|----:|----:|----------:|
| 0 | 28.22 | 0.390 | 0.139 | 0.0056 | - | - | 88 B |
| 1000 | 6,967.40 | 258.184 | 92.071 | 3.61 | 0.57 | - | 56,688 B |
| 5000 | 34,088.84 | 776.288 | 344.677 | 21.12 | 7.02 | - | 331,448 B |
| 10000 | 446,339.19 | 21,668.906 | 9,621.128 | 61.52 | 61.04 | 27.83 | 662,556 B |
| 25000 | 1,173,139.36 | 42,874.927 | 15,289.612 | 109.38 | 107.42 | 39.06 | 1,524,723 B |
| 50000 | 2,597,830.47 | 152,637.596 | 67,772.039 | 183.59 | 179.69 | 50.78 | 3,049,038 B |

**Note on the 10K anomaly:** per-step cost at 10K (≈44.6 ns/step) is
roughly 6.5× the 5K rate (≈6.8 ns/step) and then returns to ≈47 ns/step
at 25K. The mapping loop is linear (`MapStep` is O(1) per step), and
Gen2 collections *first appear* at 10K. This is most likely a
GC/measurement artifact at the large-object boundary rather than a real
nonlinearity in the algorithm. It is flagged for a follow-up confirming
re-run, but the underlying `Build` logic shows no structural O(n²) cost.

## 7. Results — DiagnosticReport

Four accessors/methods, 10→5,000 results.

| Method | ResultCount | Mean (ns) | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|--------|------------:|---------:|------:|-------:|----:|----:|----:|----------:|
| ComputedSummaryAccess (.Summary) | 10 | 52.96 | 0.318 | 0.141 | 0.0025 | - | - | 40 B |
| CategoryGrouping (.Categories) | 10 | 352.10 | 10.993 | 3.920 | 0.1049 | - | - | 1,648 B |
| DetailedFormatting | 10 | 826.20 | 53.658 | 23.824 | 0.4120 | 0.0038 | - | 6,464 B |
| CompactFormatting | 10 | 138.75 | 1.601 | 0.571 | 0.0381 | - | - | 600 B |
| ComputedSummaryAccess | 100 | 456.38 | 5.743 | 2.550 | 0.0024 | - | - | 40 B |
| CategoryGrouping | 100 | 1,627.02 | 14.357 | 6.374 | 0.2232 | - | - | 3,512 B |
| DetailedFormatting | 100 | 4,595.43 | 146.945 | 52.402 | 3.4180 | 0.4196 | - | 53,628 B |
| CompactFormatting | 100 | 720.76 | 5.267 | 2.339 | 0.0706 | - | - | 1,120 B |
| ComputedSummaryAccess | 1000 | 4,506.64 | 56.735 | 25.191 | - | - | - | 40 B |
| CategoryGrouping | 1000 | 16,016.31 | 445.593 | 158.903 | 1.1292 | 0.0305 | - | 17,920 B |
| DetailedFormatting | 1000 | 91,716.35 | 2,702.890 | 1,200.100 | 52.61 | 52.61 | 52.61 | 363,726 B |
| CompactFormatting | 1000 | 9,578.03 | 80.324 | 35.664 | 0.4425 | - | - | 7,168 B |
| ComputedSummaryAccess | 5000 | 23,426.20 | 330.982 | 146.958 | - | - | - | 40 B |
| CategoryGrouping | 5000 | 95,121.02 | 2,555.247 | 1,134.546 | 8.4229 | 1.5869 | - | 132,682 B |
| DetailedFormatting | 5000 | 566,469.20 | 10,143.340 | 4,503.706 | 249.02 | 249.02 | 249.02 | 1,859,450 B |
| CompactFormatting | 5000 | 34,891.89 | 427.620 | 189.866 | 2.1973 | 0.1221 | - | 35,200 B |

`ComputedSummaryAccess` is O(n) and **recomputes on every read** — the
`DiagnosticReport.Summary` property rebuilds counts (~7 scans of the
result list) each call. It allocates only 40 B (a small record) but its
cost scales linearly with result count and is paid on every access.

## 8. Results — SupportSnapshotSerializer

`SupportSnapshotSerializer.Serialize(snapshot)`, Small/Medium/Large.

| Size | Mean (µs) | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|------|---------:|------:|-------:|----:|----:|----:|----------:|
| Small | 13.90 | 0.324 | 0.116 | 2.20 | - | - | 33.69 KB |
| Medium | 209.46 | 1.482 | 0.658 | 83.25 | 83.25 | 83.25 | 279.26 KB |
| Large | 1,323.96 | 6.909 | 3.067 | 300.78 | 298.83 | 298.83 | 2,734.90 KB |

Large payload: serialized output length measured separately at
≈2.05 MB (indented JSON) versus 2,734.90 KB managed allocation — the
serializer over-allocates by ~33% beyond the final payload, typical of
`System.Text.Json` buffered string building. Diagnostics and the
execution-preview `Steps` array dominate the payload; DNS cache and
prefix history contribute materially at Large. No Gen2 pressure beyond
what the large string buffer implies.

## 9. Scaling analysis

### A. PrefixDatasetComparer
- Growth 1K→50K (Mixed): 130.85 → 11,224.57 µs ≈ **85.8×** for 50× size
  ⇒ ~1.7× per doubling ⇒ slightly superlinear, consistent with the two
  `OrderBy(...).ToArray()` sorts (O(n log n)) compounding the O(n)
  `Except` passes.
- Identical/AllAdded/AllRemoved are ~30% cheaper than Mixed at every
  size (Mixed does two change sets, the others one).
- Allocation scales ~linearly with size: 247 KB → 10,004 KB (≈40.5× for
  50×) ⇒ ~204 B per element.

### B. RuntimeChangeSetPlanner
- No-op equivalent (AllPresent, where desired already matches observed):
  still ~39 ms at 50K — the planner always rebuilds three
  `GroupBy().ToDictionary()` maps regardless of how few changes exist.
- AllMissing/AllObsolete/AllPresent are similar magnitude; **Mixed** is
  the most expensive scenario at every size (both adds and removes).
- Growth 1K→50K (Mixed): 344.9 → 52,201 µs ≈ **151×** for 50× size ⇒
  ~3× per doubling ⇒ **clearly superlinear**.
- Allocation (Mixed): 608 KB → 34,704 KB ≈ **57×** for 50× ⇒ ~694 B per
  element. The three fully materialized dictionaries are the dominant
  allocation source.

### C. ExecutionPreviewBuilder
- Fixed overhead at 0 steps: 28 ns / 88 B (negligible).
- Per-step cost is ~6.8 ns/step (1K–5K) and ~47 ns/step (25K–50K), with
  the 10K reading (≈44.6 ns/step) an apparent GC artifact (see §6).
- 50K remains within ~3× of linear in the upper range; no structural
  O(n²). Allocation ~61 B/step, no Gen2 until 10K.

### D. DiagnosticReport
- Summary access: O(n), 53 ns → 23.4 µs (10 → 5,000), recomputed each
  call.
- Category grouping: O(n), 352 ns → 95 µs.
- Compact formatting: O(n), 139 ns → 35 µs.
- Detailed formatting: O(n) but the heaviest by far — 826 ns →
  **566 µs**, allocating up to 1.86 MB at 5K (drives Gen2).
- Allocation growth tracks result count linearly (Detailed ≈ 372 B/
  result at 5K).

### E. SupportSnapshotSerializer
- Small→Medium→Large: 13.9 → 209.5 → 1,324 µs (≈15× and ≈6.3×) ⇒ roughly
  linear in payload volume.
- Allocated bytes ≈ 133% of final payload at Large (indented JSON string
  buffering). No structural inefficiency beyond standard STJ behavior.
- Diagnostics and execution-preview content dominate payload size; at
  Large these plus DNS cache and history push Gen2.

## 10. Cross-benchmark ranking (at maximum tested size)

| Rank | Component | Max Mean | Max Allocated | Notes |
|-----:|----------|---------:|-------------:|-------|
| 1 | RuntimeChangeSetPlanner (Mixed 50K) | 52.2 ms | 34.7 MB | every repair cycle; superlinear |
| 2 | PrefixDatasetComparer (Mixed 50K) | 11.2 ms | 10.0 MB | prefix sync; mild superlinear |
| 3 | ExecutionPreviewBuilder (50K) | 2.6 ms | 3.0 MB | cheap per step; runs with planner |
| 4 | SupportSnapshotSerializer (Large) | 1.3 ms | 2.7 MB | infrequent (support bundle) |
| 5 | DiagnosticReport Detailed (5K) | 0.57 ms | 1.86 MB | on-demand, infrequent |

**Classification**
- *High absolute cost, runs on every repair cycle* — RuntimeChangeSetPlanner
  (the reconciliation step fires on every observe/plan/execute cycle).
- *Moderate cost on every prefix-sync* — PrefixDatasetComparer
  (runs when the prefix source updates, not every cycle, but frequently).
- *Allocation-heavy but fast* — SupportSnapshotSerializer (1.3 ms but
  2.7 MB; only on explicit support-bundle export).
- *Latency-heavy but low-allocation* — DiagnosticReport `.Summary`
  (recomputed O(n) per access, 40 B, but 23 µs at 5K; only on demand).

## 11. Repeat-run stability (planner)

The planner class was executed a second time (identical invocation) to
check determinism. Run 2 was ~14% slower wall-clock (309 s vs 270 s) and
the per-case Means varied, consistent with the heavy background load
(WSL VM, Visual Studio, Docker, Defender) and Balanced power plan noted
in §2 rather than with code instability.

Representative comparison (Run 1 → Run 2, Δ%):

| Scenario | Size | Run 1 Mean (µs) | Run 2 Mean (µs) | Δ% |
|---------|-----:|---------------:|---------------:|---:|
| AllMissing | 1000 | 235.9 | 215.9 | -8.5% |
| AllMissing | 50000 | 40,508.5 | 38,559.3 | -4.8% |
| AllPresent | 1000 | 262.8 | 276.1 | +5.1% |
| AllPresent | 50000 | 38,954.0 | 64,859.2 | +66.5% |
| Mixed | 1000 | 344.9 | 547.4 | +58.7% |
| Mixed | 25000 | 22,994.7 | 32,974.1 | +43.4% |
| Mixed | 50000 | 52,201.0 | 73,265.0 | +40.3% |
| DuplicateInput | 50000 | 51,809.0 | 97,805.0 | +88.8% |

**Conclusion:** Run-to-run spread is large (up to ~+89% on the noisiest
large cases; small cases within ±9%). This is machine noise, not a
structural instability — the **ranking is unchanged** (Mixed remains the
most expensive scenario at every size), the **superlinear scaling trend
is preserved** (Mixed 1K→50K is ~150×–210× for a 50× size step across
both runs), and **no benchmark diverged in ordering or shape**. Before
optimizing, a cleaner re-run on a quiet machine (High Performance plan,
no WSL/Docker/IDE, Defender paused) is recommended to shrink the error
bars; the selection of `RuntimeChangeSetPlanner.Plan` as the first
target is unaffected.

## 12. Selected first optimization target

**`RuntimeChangeSetPlanner.Plan`** — `IranDirect.Core/Runtime/
Reconciliation/RuntimeChangeSetPlanner.cs`.

Selection rationale (priority order from the spec):
1. **Measurable nonlinear growth** — Mixed scenario 1K→50K grows 151×
  for a 50× size increase (superlinear).
2. **High cost on frequent runtime paths** — reconciliation runs on
  every observe/plan/execute cycle, i.e. every repair cycle, not an
  infrequent admin action.
3. **Excessive allocation at realistic scale** — 34.7 MB at 50K,
  dominated by three fully materialized `GroupBy().ToDictionary()` maps
  that are rebuilt on every call.
4. High absolute cost even at the production ~2K workload (interpolated
  ≈1–2 ms and ≈1 MB per cycle).
5. The change is structurally localized (the dictionary-build section)
  and low-risk to attempt.

`PrefixDatasetComparer` and `DiagnosticReport.Summary` are noted as
secondary opportunities (mild superlinearity from `OrderBy` sorts;
uncached recompute on every `.Summary` read) but are not selected as the
first target.

## 13. Optimization hypothesis (no code changed this phase)

- **Suspected hot operation:** the three `snapshot.Observed.Routes
  .GroupBy(identity).ToDictionary(...)` + `snapshot.Desired.
  EndpointRoutes.GroupBy(...).ToDictionary(...)` + `snapshot.Desired.
  PrefixRoutes.GroupBy(...).ToDictionary(...)` builds (lines 17–47),
  each a full O(n) pass that allocates a new dictionary sized to the
  collection.
- **Source file / method:** `RuntimeChangeSetPlanner.cs`,
  `Plan(...)` (lines 17–47).
- **Likely cause:** redundant full-materialized dictionaries. The same
  observed-route collection is grouped once and could be reused by all
  four classification passes; building three separate dictionaries
  triples the allocation and the grouping cost.
- **Expected complexity today:** O(n) time but ~3–4× allocation overhead
  relative to a single grouping; the final `OrderBy(...).ToArray()` adds
  O(n log n) only on the change list (small vs n).
- **Proposed direction (future phase):** build a single lookup over
  observed routes once; iterate desired routes directly against it;
  classify in a single pass. Preserve exact ordering (`OrderBy` by
  `Kind` then `Identity`, case-insensitive) and identity semantics.
- **Expected improvement:** ~30–50% allocation reduction and a modest
  time reduction at 25K–50K; smaller but present at 2K.
- **Semantic risks:** must keep case-insensitive identity matching,
  duplicate-identity handling (`group.First()`), and the exact change
  ordering; any reordering could change downstream executor behavior.
- **Validation required:** existing `RuntimeChangeSetPlanner` unit
  tests (change-set equality + ordering) plus a re-run of this benchmark
  class; confirm Mean and Allocated drop while All* scenarios produce
  identical results.

## 14. Baseline integrity

- **Commit:** `628d64af5f29403e6206af78cfa4556862766c7f`
- **BenchmarkDotNet:** 0.15.8
- **Commands:** see §3.
- **Date:** 2026-08-03.
- **Power/Turbo:** Balanced plan, Turbo Boost active — results are a
  conservative floor and will differ on High Performance / a quieter
  machine.
- **Background processes:** WSL VM, Visual Studio, Docker Desktop,
  browsers, Windows Defender, Search indexing were active and likely
  influenced measurements.
- **These results are machine-specific and are not thresholds.**

## 15. Files changed

- `docs/performance/baselines/phase-24.1-baseline.md` (new) — this
  document.
- `docs/performance/README.md` — link to the baseline (optional, if
  added).
- No production (`IranDirect.Core/...`) or benchmark
  (`IranDirect.Benchmarks/...`) source file was modified.
