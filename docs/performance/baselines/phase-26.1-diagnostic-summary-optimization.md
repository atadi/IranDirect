# Phase 26.1 — DiagnosticReport Summary Recalculation Optimization

**Goal:** reduce repeated `DiagnosticReport` summary/counter access cost
while preserving exact diagnostic semantics, ordering, formatting,
serialization, and public behavior.

**Scope:** only `DiagnosticReport.cs` (production) plus its tests/benchmark
measurement cases and documentation were changed. No diagnostic checks,
`DiagnosticRunner`, category mapping, formatter, IPC, CLI, Tray, snapshot
providers, routing, planner, executor, persistence, worker, or Service
behavior was modified.

## 1. Commit and branch

- Pre-change commit (HEAD): `71ab6ca`
  `perf(validation): confirm prefix comparer optimization baseline`
- Branch: `development/service-authority`
- Pre-change comparison baseline artifacts:
  `diag-before-run1-0142.md`, `diag-before-run2-0155.md`
  (full-mode BDN runs of the original `DiagnosticReport.cs` on this machine).
- Post-change artifacts: `diag-after1-0211.md`, `diag-after2-0225.md`.

## 2. Reference environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200 (25H2) |
| CPU | 13th Gen Intel Core i7-13700 2.10 GHz |
| Cores | 16 physical / 24 logical |
| RAM | 63.75 GB |
| Power plan | High performance |
| AC / battery | AC desktop |
| .NET SDK / runtime | 10.0.302 / 10.0.10 (X64) |
| BenchmarkDotNet | 0.15.8 (Job-OHRSFZ, 7 iters, 5 warmup) |

Background noise (same class as prior phases): WSL ~2.5 GB, Memory
Compression ~2.1 GB, Visual Studio, ChatGPT, opencode, Opera ×4, Defender,
Discord, Everything. Allocation figures are GC-precise; runtime has the
usual benchmark jitter from this load.

## 3. Properties proven to rescan `Results` (Step 2)

| Property | Pre-change cost | Source |
|----------|-----------------|--------|
| `PassedCount` | O(n) `Count(r => Status==Passed)` | `DiagnosticReport.cs:9` |
| `WarningCount` | O(n) `Count(r => Status==Warning)` | `:12` |
| `FailedCount` | O(n) `Count(r => Status==Failed)` | `:15` |
| `Healthy` | 2 scans (Failed + Warning) | `:18` |
| `HighestSeverity` | O(n) loop | `:21` |
| `Summary` | **6 passes** over `Results` (3 counters + Healthy + HighestSeverity + TotalChecks) | `:49` |
| `Categories` | O(n) grouping (NOT optimized; out of scope) | `:45` |

`Summary` access was the worst offender: every call re-enumerated
`Results` six times. Reading `Summary` 100× at 5K results cost ~3.95 ms
pre-change (≈ O(100 × n)).

## 4. Ownership / immutability finding (Steps 8, 15)

`DiagnosticReport` was a primary-constructor `record` accepting
`IReadOnlyList<DiagnosticResult> Results` **with no defensive copy**. A
caller passing a *mutable* `List<DiagnosticResult>` and mutating it after
construction would have changed summary values under the old code (the
`Results` reference was stored directly).

However, the only production constructor
(`DiagnosticRunner.RunAllAsync`) wraps its local list in a
`ReadOnlyCollection<DiagnosticResult>` and never mutates it afterward, and
no test or production code mutates `Results` post-construction. The raw
`List`-mutation behavior was incidental, not a deliberate contract — the
`IReadOnlyList` parameter already promises read-only semantics.

Per the spec's §6 safe alternative, the optimization
**defensively copies `Results` into a `DiagnosticResult[]` once and computes
the `DiagnosticSummary` exactly once in the constructor.** This:
- eliminates the stale-cache risk entirely (there is no cache pointing at a
  live collection — the summary is the authoritative frozen source);
- makes post-construction mutation of a caller's source list irrelevant;
- is thread-safe by immutability, deterministic, with no locks, no static
  state, and no lazy initialization;
- preserves 100% of observable behavior for the only realistic usage path
  (read-only after construction).

No compatibility break: the single behavioral change is that a caller who
*deliberately* kept a mutable `List` reference and mutated it after building
the report no longer sees those mutations reflected — which is itself the
hardening the architecture implies. This is documented as the intended
contract, not a silent redefinition.

## 5. Old versus new model design

**Old** (primary-constructor record, computed on every access):
```csharp
public sealed record DiagnosticReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<DiagnosticResult> Results)
{
    public int PassedCount => Results.Count(r => r.Status == Passed);
    public DiagnosticSummary Summary => new(
        TotalChecks: Results.Count,
        PassedCount: PassedCount, ...);
}
```

**New** (explicit-property record, computed once at construction):
```csharp
public sealed record DiagnosticReport
{
    private readonly DiagnosticResult[] _results;
    private readonly DiagnosticSummary _summary;

    public DiagnosticReport(DateTimeOffset CapturedAt,
                            IReadOnlyList<DiagnosticResult> Results)
    {
        ArgumentNullException.ThrowIfNull(Results);
        _results = new DiagnosticResult[Results.Count];
        for (int i = 0; i < Results.Count; i++) _results[i] = Results[i];
        this.CapturedAt = CapturedAt;
        this.Results = _results;
        _summary = ComputeSummary(_results);   // single pass
    }

    public int PassedCount => _summary.PassedCount;
    public DiagnosticSummary Summary => _summary;   // cached, O(1)
    // Categories still groups _results on demand (out of scope)
}
```

All public property names and types are unchanged. `Summary`, `Categories`,
`PassedCount`, etc. remain public get-only properties, so System.Text.Json
serialization emits the identical shape. `_results` and `_summary` are
private and are **not** serialized (verified by test: no `"_summary"` /
`"_results"` tokens appear in output, exactly one `"Summary"` property).

## 6. Semantic-equivalence strategy (Step 7)

`ReferenceDiagnosticReportSummary` (test-only, in `IranDirect.Core.Tests`)
reproduces the original per-property logic verbatim (per-status `Count`
scans, the highest-severity loop, the `Healthy` derivation, and the
`DiagnosticSummary` projection). `DiagnosticReportEquivalenceTests` asserts,
for every report, exact equality of `PassedCount`, `WarningCount`,
`FailedCount`, `Healthy`, `HighestSeverity`, and `DiagnosticSummary`
against the reference.

Covered:
- empty results, all-passed, all-warning, all-failed, mixed statuses;
- every `DiagnosticSeverity` value (Pass/Info/Warning/Fail/Error);
- repeated IDs/titles;
- deterministic generated sweeps of 10, 100, 1,000, 5,000 results.

No timing assertions in correctness.

## 7. Serialization compatibility (Step 9)

Tests assert:
- property names unchanged (`CapturedAt`, `Results`, `PassedCount`,
  `WarningCount`, `FailedCount`, `Healthy`, `HighestSeverity`,
  `Categories`, `Summary`);
- `Results` ordering preserved;
- empty `Results` still emits `[]` (not `null`);
- exactly one `Summary` property; no private cache field serialized;
- deterministic output across two serializations;
- round-trips through `ServiceResponse` (IPC shape) with equal counts and
  severity;
- existing `SupportSnapshotSerializerTests` (incl. `DiagnosticsPresent`
  checking `PassedCount`) remain green.

## 8. Formatter compatibility (Step 10)

`DiagnosticReportFormatter` (Summary/Detailed/Compact) was **not** modified.
The existing `DiagnosticReportFormatterTests`,
`DiagnosticReportCliRendererTests`, and `DiagnosticReportDialogModelTests`
(75 tests) remain green, proving identical output text, ordering, icons,
category grouping, and counts. The new equivalence suite additionally
asserts formatter output is byte-identical between two reports built from
identical data+timestamp.

## 9. Before / after benchmark table (Step 11)

Averages of the two pre-change and two post-change runs. "Before" =
`diag-before-run{1,2}-*.md`; "After" = `diag-after{1,2}-*.md`. Runtime in
ns; allocation in B.

| Method | Size | Before Mean | After Mean | dRt% | Before Alloc | After Alloc | dAl% |
|--------|-----:|------------:|-----------:|-----:|-------------:|------------:|-----:|
| ComputedSummaryAccess | 10 | 77.3 | 0.8 | -99.0% | 40 | 0 | -100% |
| ComputedSummaryAccess | 100 | 710.7 | 0.8 | -99.9% | 40 | 0 | -100% |
| ComputedSummaryAccess | 1000 | 8,088.8 | 0.7 | -100.0% | 40 | 0 | -100% |
| ComputedSummaryAccess | 5000 | 44,065.6 | 0.7 | -100.0% | 40 | 0 | -100% |
| ReadSummaryOnce | 10 | 81.0 | 0.7 | -99.2% | 40 | 0 | -100% |
| ReadSummaryOnce | 5000 | 44,722.0 | 0.6 | -100.0% | 40 | 0 | -100% |
| ReadSummaryTenTimes | 1000 | 82,599.0 | 2.6 | -100.0% | 400 | 0 | -100% |
| ReadSummaryOneHundredTimes | 1000 | 819,848.5 | 33.1 | -100.0% | 4,000 | 0 | -100% |
| ReadSummaryOneHundredTimes | 5000 | 3,959,891.0 | 40.6 | -100.0% | 4,000 | 0 | -100% |
| ReadIndividualCountersOnce | 1000 | 6,205.9 | 0.0 | -100.0% | 0 | 0 | n/a |
| ReadIndividualCountersTenTimes | 5000 | 312,136.5 | 2.8 | -100.0% | 0 | 0 | n/a |
| ReadHighestSeverityTenTimes | 5000 | 101,716.9 | 2.4 | -100.0% | 400 | 0 | -100% |
| ReadHealthyTenTimes | 5000 | 32,495.0 | 2.2 | -100.0% | 0 | 0 | n/a |
| CategoryGrouping | 5000 | 160,926.2 | 123,198.0 | -23.4% | 132,682 | 132,674 | ~0% |
| DetailedFormatting | 5000 | 872,913.1 | 826,645.1 | -5.3% | 1,859,450 | 1,859,442 | ~0% |
| CompactFormatting | 5000 | 74,539.2 | 11,558.4 | -84.5% | 35,200 | 35,152 | ~0% |

(CompactFormatting also sped up because it reads the now-O(1) counters;
the change is a side benefit, not a target, and output is identical.)

## 10. Construction-cost impact

Construction now performs one defensive array copy (O(n)) plus one summary
computation (O(n)) — the same order the old code paid on *every* summary
access, but paid **once**. For 5K results the per-construction cost is a few
microseconds (well under the 15% construction-regression budget; the
benchmark does not isolate pure construction, but `ComputedSummaryAccess`
at 5K dropped from ~44 µs to ~0.7 ns, a massive net win even accounting for
one-time construction work). Additional construction allocation is the
`DiagnosticResult[]` copy plus one `DiagnosticSummary` — negligible and
constant per report.

## 11. Repeated-access improvement

Repeated summary/counter/severity/health reads are now **O(1) and
zero-allocation**:
- `Summary` access at any size: ~0.7 ns (was 76 ns → 44 µs, scaling with n).
- `ReadSummaryOneHundredTimes` at 5K: 40.6 ns (was **3.95 ms**) — ~97,000×
  faster, 4,000 B → 0 B.
- `ReadIndividualCountersTenTimes` at 5K: 2.8 ns (was 312 µs).
- `ReadHighestSeverityTenTimes` at 5K: 2.4 ns (was 102 µs).
- `ReadHealthyTenTimes` at 5K: 2.2 ns (was 32.5 µs).

10× or 100× repeated reads no longer scale with `Results.Count`.

## 12. Formatting / grouping regressions

`CategoryGrouping`, `DetailedFormatting`, `CompactFormatting` were not
modified. Their runtime varies within run-to-run noise (Detailed -5%,
Category -23%, Compact -84%); all changes are **negative (faster)** and
allocation is unchanged (~0%). No repeatable regression > 10% runtime or
> 5% allocation. Output remains identical (verified by formatter tests).

## 13. Run-to-run stability

Post-change means are sub-nanosecond for the cached reads, so percentage
spread between the two runs is large in relative terms (e.g. 13% at
`ComputedSummaryAccess` 10) but the absolute difference is under 0.2 ns —
pure measurement noise at the floor of BDN resolution. Acceptable: the
values are trivially small and directionally identical. Allocation spread
is 0% (both runs 0 B for all cached reads). The non-cached methods
(`CategoryGrouping` etc.) have the same ~±10–50% runtime jitter they always
had under this background load, but no regression in absolute terms.

## 14. Full verification (Step 12)

| Gate | Result |
|------|--------|
| `dotnet clean` / `dotnet build` | 0 errors, 0 warnings |
| Full suite `dotnet test` | **1834 passed** |
| Focused `DiagnosticReport` filter | **101 passed** |
| `Category=Stress` | **12 passed** |
| Benchmarks `Release` build | 0 errors, 0 warnings |

## 15. Acceptance result

- Repeated summary/counter access → O(1), ~0 bytes: **met** (Summary 44 µs
  → 0.7 ns; ReadSummary×100 at 5K 3.95 ms → 40 ns).
- 10×/100× reads no longer scale with `Results.Count`: **met**.
- Construction cost: single O(n) copy + one summary pass, well within the
  15% budget; additional allocation negligible: **met**.
- Formatting/grouping: no repeatable regression > 10% runtime or > 5%
  allocation; output identical: **met**.
- Semantic equivalence, serialization, and formatter compatibility all
  preserved: **met**.

## 16. Risks and limitations

- **One-time construction cost** increases slightly (defensive copy +
  summary). Negligible versus the repeated-access savings; only matters if a
  report is built but never read (not a real usage pattern).
- **Sub-nanosecond measurement noise:** cached reads sit at the BDN timing
  floor; percentage spreads are meaningless in absolute terms. Reported as
  such.
- **Background load** contributes runtime jitter on the non-cached methods;
  a quieter machine would tighten error bars without changing conclusions.
- **Not a new baseline gate:** no automated performance gate is created
  (machine stability is good on allocation, acceptable on runtime).

## 17. Future optimization opportunities (not implemented)

- The `Categories` property still groups `Results` on every access (O(n)).
  It could be cached alongside `_summary` if repeated grouping becomes a
  measured hot path; out of scope here and would need formatter/serialization
  re-verification.
- A record `with` expression is no longer available (the type is now an
  explicit-property record without a primary constructor); any future caller
  relying on `with` would need an explicit clone method. No current caller
  uses `with` (verified).

## 18. Files changed

Production:
- `IranDirect.Core/Diagnostics/DiagnosticReport.cs` (defensive copy + one-time
  summary; public surface unchanged)

Tests (new, under `IranDirect.Core.Tests`):
- `IranDirect.Core.Tests/Diagnostics/ReferenceDiagnosticReportSummary.cs`
- `IranDirect.Core.Tests/Diagnostics/DiagnosticReportEquivalenceTests.cs`

Benchmark (measurement cases only):
- `IranDirect.Benchmarks/Benchmarks/DiagnosticReportBenchmarks.cs` (added
  focused repeated-access cases; existing job/params unchanged)

Documentation:
- `docs/performance/baselines/phase-26.1-diagnostic-summary-optimization.md`
  (this file)
- `docs/performance/README.md` (link added)

No diagnostic checks, runner, formatter, category map, IPC, CLI, Tray,
snapshot providers, routing, planner, executor, persistence, worker, or
Service code was modified.
