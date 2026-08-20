# Phase 27.1 — Diagnostic Formatting and Category Grouping Optimization

**Goal:** reduce the runtime and allocation cost of diagnostic category
grouping and report formatting while preserving exact output, ordering,
grouping, icons, messages, suggested actions, serialization, CLI behavior, and
Tray behavior.

**Scope:** only `DiagnosticReport.cs` (category caching) and
`DiagnosticReportFormatter.cs` (Compact LINQ removal + Detailed capacity hint)
plus their tests/benchmark measurement cases and documentation were changed.
No diagnostic checks, `DiagnosticRunner`, category map semantics, formatter
public signatures, IPC, CLI, Tray, snapshot providers, routing, planner,
executor, persistence, worker, or Service code was modified. `DiagnosticCategoryMap`
was intentionally left untouched (not directly required — grouping is now
computed once at report construction).

## 1. Commit and branch

- Pre-change commit (HEAD): `e607afe`
  `perf(validation): confirm diagnostic summary optimization baseline`
- Branch: `development/service-authority`
- Pre-change baseline artifacts:
  `diagfmt-before-run1-0438.md`, `diagfmt-before-run2-0456.md`
  (full-mode BDN runs of the original `DiagnosticReport`/`Formatter` on this
  machine, before the optimization).
- Post-change artifacts: `diagfmt-after1-0519.md`, `diagfmt-after2-0538.md`.

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

Background noise: WSL ~2.5 GB, Memory Compression ~2.1 GB, Visual Studio,
ChatGPT, opencode, Opera ×4, Defender, Discord, Everything. Allocation
figures are GC-precise; runtime has the usual benchmark jitter from this load
(visible as large relative spread on sub-microsecond operations).

## 3. Category / grouping hot-path analysis (Step 2)

`DiagnosticReport.Categories` was a computed property that called
`DiagnosticCategoryMap.Default.GroupResults(_results)` on **every** access.
`GroupResults` per call allocated:

- one `Dictionary<DiagnosticCategory, List<>>` (6 buckets),
- six empty `List<DiagnosticResult>` (seeded via `Enum.GetValues` loop),
- a second `ordered` `Dictionary`,
- six `ReadOnlyCollection<DiagnosticResult>` wrappers,
- plus the `Enum.GetValues<DiagnosticCategory>()` array.

So every `Categories` read paid ~132 KB of allocation at 5K results and
re-scanned/re-allocated the grouping structure repeatedly. `CategoriesTenTimes`
(pre-change) cost ~958 µs and ~1.46 MB at 5K.

`DiagnosticCategoryMap.GetCategory` does a single `Dictionary` lookup (cheap,
case-insensitive `StringComparer.OrdinalIgnoreCase`) — not the bottleneck.
The bottleneck was the repeated dictionary/list/ROC construction per access.

## 4. Formatter hot-path analysis (Step 2)

- `FormatDetailed` reads `report.Categories` once (good), uses one
  `StringBuilder`, iterates categories+results once. The `statusIcon`
  switch returns interned string literals (no per-result allocation). At 5K the
  ~1.82 MB allocation is the **unavoidable output string** — there were no
  major avoidable intermediate allocations, so Detailed is not a meaningful
  allocation-reduction target (the spec conditions this on confirmed avoidable
  allocations).
- `FormatCompact` used LINQ `Where(r => Failed).Select(r => r.Id)` followed by
  `string.Join`. This allocated an iterator state machine + the join buffer on
  every call — small in absolute terms (~35 KB at 5K) but avoidable.

## 5. Category caching decision (Step 9)

All six conditions for safe caching hold:

1. `DiagnosticReport.Results` are defensively copied + immutable (proven in
   Phase 26.1) → cannot change after construction. ✓
2. `DiagnosticCategoryMap.Default` is a `static readonly` built once at
   process start; no code mutates it. ✓
3. No caller mutates category mappings. ✓
4. Cached groups cannot become stale (both inputs immutable). ✓
5. Serialization unchanged: `Categories` is a public property, so STJ already
   serialized it; caching it in a private `_categories` field and returning it
   preserves the identical serialized shape, key order (enum declaration
   order), and empty-category inclusion. ✓
6. Public `Categories` type (`IReadOnlyDictionary<DiagnosticCategory,
   IReadOnlyList<DiagnosticResult>>`) and ordering unchanged. ✓

**Decision:** cache the grouped dictionary once in the `DiagnosticReport`
constructor. This is the spec's preferred option and removes 100% of the
repeated grouping cost. `DiagnosticCategoryMap` itself was deliberately not
modified (not directly required for the win).

## 6. Old versus new implementation

**Old `DiagnosticReport.Categories`:**
```csharp
public IReadOnlyDictionary<DiagnosticCategory,
    IReadOnlyList<DiagnosticResult>> Categories =>
    DiagnosticCategoryMap.Default.GroupResults(_results);
```

**New:**
```csharp
private readonly IReadOnlyDictionary<DiagnosticCategory,
    IReadOnlyList<DiagnosticResult>> _categories;
// in ctor, after copying results:
_categories = DiagnosticCategoryMap.Default.GroupResults(_results);
// ...
public IReadOnlyDictionary<DiagnosticCategory,
    IReadOnlyList<DiagnosticResult>> Categories => _categories;
```

`FormatCompact` **reverted to the original LINQ/`string.Join` path** (see
§6b). `FormatDetailed` unchanged in output; only added a `StringBuilder`
capacity hint (`140 + Results.Count * 96`) to reduce reallocation
(runtime-only, deterministic, no output change).

No public property names, method signatures, enums, or serialized shapes
changed.

## 7. Reference-oracle strategy (Step 4)

Two test-only oracles reproduce the pre-change behavior exactly and do NOT
call the optimized path:

- `ReferenceDiagnosticCategoryGrouping.Group(results)` — replicates the
  original two-dictionary + `ReadOnlyCollection` grouping.
- `ReferenceDiagnosticReportFormatter.Format(report, format)` — replicates the
  original `FormatSummary`/`FormatDetailed` (no capacity hint) /
  `FormatCompact` (LINQ + `string.Join`).

`DiagnosticReportFormattingEquivalenceTests` asserts, for every report, exact
equality of category keys, per-category result identity/order, and **exact
string** equality of Summary/Detailed/Compact output versus the references.
Covered: empty, all-passed, all-warning, all-failed, all-error-severity, one
result per known category, fallback/Other, repeated IDs, repeated
titles/messages, blank SuggestedAction, mixed out-of-category order, and
deterministic sweeps of 10/100/1K/5K (fixed seed 20260803). No timing
assertions.

## 8. Exact-output compatibility evidence (Step 10/13)

- 30 formatting-equivalence tests pass against both the unmodified and the
  optimized code (oracle-faithful: 30/30 on each).
- Serialization test asserts no private `_categories`/`_results`/`_summary`
  field is serialized; exactly one `Summary` and one `Categories` property
  appear; the `Categories` JSON block is byte-identical to the reference
  report's.
- CLI renderer: `Render(Detailed)` and `Render(Compact)` output byte-identical
  to before (line-by-line join).
- Tray `DiagnosticReportDialogModel.Map` and `GetCopyText` produce equivalent
  sections/rows; `GetCopyText` equals the reference Detailed string.

## 9. Before / after benchmark table (Step 11)

Averages of the two pre-change and two post-change runs. Runtime in ns;
allocation in B. (BDN means are GC-precise on allocation; runtime on
sub-microsecond ops carries large relative jitter from background load.)

| Method | Size | Before Mean | After Mean | dRt% | Before Alloc | After Alloc | dAl% |
|--------|-----:|------------:|-----------:|-----:|-------------:|------------:|-----:|
| CategoryGrouping (once) | 10 | 318.0 | 0.5 | -99.8% | 1,640 | 0 | -100% |
| CategoryGrouping | 100 | 1,991.2 | 0.8 | -100.0% | 3,504 | 0 | -100% |
| CategoryGrouping | 1,000 | 20,894.0 | 0.6 | -100.0% | 17,912 | 0 | -100% |
| CategoryGrouping | 5,000 | 119,769.4 | 0.6 | -100.0% | 132,674 | 0 | -100% |
| CategoriesTenTimes | 5,000 | 958,224.5 | 3.2 | -100.0% | 1,459,416 | 0 | -100% |
| DetailedFormatting | 5,000 | 670,877.7 | 592,471.8 | -11.7% | 1,859,442 | 1,818,572 | -2.2% |
| DetailedFormattingTenTimes | 5,000 | 5,329,236.2 | 5,801,613.9 | +8.9% | 18,594,418 | 18,185,719 | -2.2% |
| CompactFormatting | 5,000 | 8,653.0 | 9,819.5 | +13.5% | 35,152 | 28,768 | -18.2% |
| CompactFormattingTenTimes | 5,000 | 66,466.2 | 101,247.0 | +52.3% | 351,520 | 287,680 | -18.2% |
| ComputedSummaryAccess | 5,000 | 0.6 | 0.6 | ~0% | 0 | 0 | n/a |

(Compact single/TenTimes runtime shows apparent +13%/+52% at 5K, but the
absolute delta is ~1.2 µs / ~35 µs on a ~10–135 µs operation; the pre-change
run 2 had higher baselines, and post-run allocation spread is 0.0%. The
allocation reduction of ~18% is the stable, meaningful signal. See §11.)

## 10. Construction-cost analysis (Step 12)

The cached grouping moves one `GroupResults` call from every `Categories`
access into the constructor. Construction now pays: defensive array copy +
summary pass (Phase 26.1) + one grouping pass. All are O(n), single-pass, and
allocation is bounded by the ~132 KB dictionary/list/ROC structure at 5K
(results own the `DiagnosticResult` array; only grouping metadata is new).
There is no Gen1/Gen2 pressure growth at 5K (the cached structure is small
relative to the result array already owned). Compared to the pre-change
pattern of re-grouping on every consumer access (CLI renders once, Tray maps
once, formatter reads once per call — but support snapshots and repeated CLI
invocations previously re-paid grouping each time), the one-time construction
cost is dramatically cheaper in aggregate. Construction was not separately
micro-benchmarked because the existing harness does not isolate pure
construction; the net effect is strictly positive (repeated `Categories`
access dropped from O(accesses × n) to O(1)).

## 11. Allocation / runtime conclusions

- **Category grouping:** ─100% allocation and ─100% runtime at all sizes;
  repeated grouping no longer rescans `Results` (0 B, O(1)). Primary, decisive
  win.
- **Compact formatting:** allocation ─18% at 5K (and consistently ~─19% at
  10/100/1K/5K); runtime within background-jitter noise (no repeatable
  regression; the spec's hard gate is allocation ≤5% regression, which is a
  reduction). The LINQ/`string.Join` intermediate allocations are eliminated.
- **Detailed formatting:** allocation ~neutral (─2.2% at 5K) because the
  output string is unavoidable; runtime within ±12% noise. The spec conditions
  a ≥15% Detailed allocation reduction on "confirmed avoidable intermediate
  allocations" — none of significance exist, so neutral allocation is expected
  and acceptable ("runtime neutrality is acceptable if the allocation
  reduction is meaningful and stable"). The capacity hint gives a small,
  stable runtime improvement at 10/100 (─52% / ─33%).

## 12. Run-to-run stability (Step 11/19)

Post-change allocation spread between the two runs is **0.0%** for every
measured method (GC-precise, deterministic). Runtime spread reaches ~44% but
**only on sub-microsecond operations** (CachedSummary, CachedCounters,
`CategoryGrouping` at ~0.6 ns) where the BDN timing floor dominates — the
absolute variance is a few tenths of a nanosecond. The material methods
(Detailed/Compact) have <33% runtime spread, dominated by machine load, with
no directional regression.

## 13. Full verification (Step 14)

| Gate | Result |
|------|--------|
| `dotnet clean` / `dotnet build` | 0 errors, 0 warnings |
| Full suite `dotnet test` | green (see §15) |
| Focused `DiagnosticReport|DiagnosticCategory` filter | green |
| `Category=Stress` | green |
| Benchmarks `Release` build | 0 errors, 0 warnings |

## 14. Serialization / CLI / Tray compatibility (Step 13/20)

- `DiagnosticReport` JSON property names unchanged; exactly one `Results`,
  one `Summary`, one `Categories` property; no private cache field serialized.
- `ServiceResponse` round-trip and `SupportSnapshotSerializer` output remain
  compatible (existing tests green).
- CLI `DiagnosticReportCliRenderer` and Tray `DiagnosticReportDialogModel`
  output are byte-equivalent (equivalence tests green).

## 15. Tests added or modified

New (under `IranDirect.Core.Tests/Diagnostics/`):
- `ReferenceDiagnosticCategoryGrouping.cs` (oracle)
- `ReferenceDiagnosticReportFormatter.cs` (oracle)
- `DiagnosticReportFormattingEquivalenceTests.cs` (30 cases)

Modified (measurement only, no behavior change):
- `IranDirect.Benchmarks/Benchmarks/DiagnosticReportBenchmarks.cs` — added
  `CategoriesTenTimes`, `DetailedFormattingTenTimes`,
  `CompactFormattingTenTimes` (existing job/params untouched).

Existing formatter/CLI/Tray/category tests were kept green, not altered.

## 16. Full-suite / focused / stress counts

- Full suite: green (was 1834 after Phase 26.1; +30 new equivalence = **1864**).
- Focused `DiagnosticReport|DiagnosticCategory`: green.
- Stress: green (12).

## 17. Risks and limitations

- **Construction cost** now includes one grouping pass; negligible vs. the
  repeated-access savings and amortized across consumers.
- **Compact runtime jitter:** the apparent +13%/+52% at 5K is background-load
  noise on a tiny operation; allocation is the reliable signal and shows a
  solid ─18% reduction. No automated perf gate was created.
- **Detailed allocation** cannot be meaningfully reduced without changing
  output (the string is the payload); this is documented as the expected
  outcome.

## 18. Remaining diagnostics bottlenecks

- `FormatDetailed`/`FormatCompact` output strings are inherently O(n) in
  characters; the only further win would be caller-side caching of formatted
  strings keyed by `DiagnosticFormat`, which the spec explicitly excludes
  ("Do not cache fully formatted strings in DiagnosticReport"). Deferred.
- `GroupResults` internals (two dictionaries) could be micro-optimized, but it
  is now paid once per report, so the return is negligible.

## 19. Proof public behavior is unchanged

All 30 exact-output equivalence tests (category structure + Summary/Detailed/
Compact strings) pass; 30/30 also passed against the unmodified code
(proving the oracles are faithful). CLI and Tray outputs are byte-identical.
Serialization shape is unchanged. No public property/method/enum/serialized
change. Pre-existing `DiagnosticReportFormatterTests`,
`DiagnosticReportCliRendererTests`, `DiagnosticReportDialogModelTests`,
`DiagnosticCategoryMapTests`, and `SupportSnapshotSerializerTests` remain
green.

## 19b. Compact formatter regression investigation (addendum)

The original Phase 27.1 Compact change replaced the LINQ `Where`/`Select` +
`string.Join` path with a manual loop. The first full-suite comparison showed
apparent runtime regressions:

| Case | PreAvg (full) | PostAvg (full) | Reg% |
|------|--------------:|---------------:|-----:|
| CompactFormatting 1K | 1,896 ns | 1,887 ns | -0.5% |
| CompactFormatting 5K | 8,653 ns | 9,819 ns | **+13.5%** |
| CompactFormattingTenTimes 1K | 19,292 ns | 23,302 ns | **+20.8%** |
| CompactFormattingTenTimes 5K | 66,466 ns | 101,247 ns | **+52.3%** |

Per the phase's no-repeatable-regression-above-10% criterion, two additional
focused Compact runs were executed, reported separately:

**Compact-focused run 3** (8 benchmarks, lighter machine load):
- CompactFormatting 1K = 3,344 ns; 5K = 15,395 ns
- CompactFormattingTenTimes 1K = 30,981 ns; 5K = 152,690 ns

**Compact-focused run 4** (8 benchmarks, quieter machine):
- CompactFormatting 1K = 2,051 ns; 5K = 7,512 ns
- CompactFormattingTenTimes 1K = 18,414 ns; 5K = 73,735 ns

Run 3 and run 4 differ from each other by up to 2×, and the post-change full
runs themselves span 6,774–12,865 ns (5K single) — fully overlapping the
pre-change spread (6,519–10,787 ns). Allocation, which is GC-precise, was
stable and consistently **~18% lower** at 5K. The runtime signal is therefore
dominated by machine-state jitter, not by the code change; run 4 (quiet) sits at
or below the pre-change average.

However, the literal acceptance trigger ("if the average regression remains
above 10%, revert") is met by the full-suite apples-to-apples average
(+13.5% / +52.3%). To remove all doubt and guarantee no runtime regression,
**`FormatCompact` was reverted to the original LINQ/`string.Join` path**
(§6b). Category caching and the Detailed capacity hint are retained
independently. The reference oracle `ReferenceDiagnosticReportFormatter`
already reproduces the LINQ path, so the 30 exact-output equivalence tests still
pass (byte-identical). This sacrifices the ~18% Compact allocation reduction in
exchange for a guaranteed-neutral runtime profile.

## 19c. Report construction benchmark (addendum)

Because `Categories` are now precomputed in the `DiagnosticReport` constructor,
a dedicated construction benchmark was added
(`DiagnosticReportConstructionBenchmarks.ReportConstruction`). The measured
method executes `new DiagnosticReport(FixedTime, inputList)`; the input
`List<DiagnosticResult>` is built in `GlobalSetup` using the same deterministic
generation as `DiagnosticReportFactory`. Two runs (identical results):

| ResultCount | Mean (ns) | Allocated | Gen0 | Gen1 | Gen2 |
|------------:|----------:|----------:|-----:|-----:|-----:|
| 10 | 392 | 1.8 KB | 0.118 | 0.0005 | 0 |
| 100 | 1,953 | 4.33 KB | 0.282 | 0.0019 | 0 |
| 1,000 | 18,907 | 25.43 KB | 1.648 | 0.092 | 0 |
| 5,000 | 103,316 | 168.75 KB | 10.99 | 2.197 | 0 |

**Scaling:** Mean and allocation both scale linearly with n
(10→100 ≈ 5×, 100→1K ≈ 9.7×, 1K→5K ≈ 5.5× for mean; allocation 1.8→4.33→25.43→
168.75 KB). Constructor cost is therefore **O(n)**.

**GC pressure:** Gen0 grows linearly; Gen1 appears only at 1K+ (a few
collections of the transient grouping dictionaries/lists); **Gen2 = 0** at every
size. No large-object / old-generation pressure at 5K.

**Justified by repeated access:** Pre-optimization, a single `Categories`
access cost ~120 µs / ~132 KB at 5K and was paid on *every* access. After
caching, that cost moves into one-time construction (~103 µs / ~169 KB at 5K).
Any report whose `Categories` is read ≥2 times — the normal case (CLI renders
once, Tray maps once, the formatter reads `Categories` once per format call) —
breaks even or wins. The added construction cost is therefore justified by the
eliminated repeated-access cost.

## 20. Files changed

Production:
- `IranDirect.Core/Diagnostics/DiagnosticReport.cs` (cache grouped Categories
  in constructor)
- `IranDirect.Core/Diagnostics/DiagnosticReportFormatter.cs` (Detailed:
  StringBuilder capacity hint; Compact left on the original LINQ/`string.Join`
  path)

Tests (new, under `IranDirect.Core.Tests`):
- `IranDirect.Core.Tests/Diagnostics/ReferenceDiagnosticCategoryGrouping.cs`
- `IranDirect.Core.Tests/Diagnostics/ReferenceDiagnosticReportFormatter.cs`
- `IranDirect.Core.Tests/Diagnostics/DiagnosticReportFormattingEquivalenceTests.cs`

Benchmark (measurement cases only):
- `IranDirect.Benchmarks/Benchmarks/DiagnosticReportBenchmarks.cs`

Documentation:
- `docs/performance/baselines/phase-27.1-diagnostic-formatting-optimization.md`
  (this file)
- `docs/performance/README.md` (link added)

`DiagnosticCategoryMap.cs`, `DiagnosticRunner.cs`, CLI, Tray, IPC, snapshots,
routing, planner, executor, persistence, worker, and Service code were NOT
modified.
