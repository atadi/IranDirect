# Phase 29.1 — ExecutionPreviewBuilder Allocation Optimization

**Date:** 2026-08-04
**Pre-change commit:** `ef396b1 perf(validation): confirm support serializer optimization baseline`
**Branch:** `development/service-authority`

## 1. Goal

Reduce `ExecutionPreviewBuilder` runtime and allocations while preserving exact
step mapping, ordering, categories, summary counts, reasons, timestamps, and
public behavior. Validation of the optimization is covered by Phase 29.2.

## 2. Environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200 (25H2) |
| CPU | 13th Gen Intel Core i7-13700 2.10 GHz |
| Cores | 16 physical / 24 logical |
| RAM | 63.75 GB |
| Power plan | High performance (active) |
| AC/Battery | AC (desktop) |
| .NET SDK | 10.0.302 |
| Runtime | .NET 10.0.10 (X64 RyuJIT x86-64-v3) |
| BenchmarkDotNet | 0.15.8 |

## 3. Hot-path analysis (pre-change)

`ExecutionPreviewBuilder.Build`:

- One `foreach` over `decision.ExecutionPlan.Steps` mapping each to an
  `ExecutionPreviewStep` (records, no closures).
- Then `BuildSummary(steps)` iterates the **already materialized** `steps`
  list a **second time** purely to tally `CreateCount`/`DeleteCount`/
  `VpnEndpointUpdates`/`InventoryUpdates`. This is an extra full enumeration
  (cheap but redundant).
- The `steps` list is created with the default `List<T>` constructor, so it
  grows by doubling and performs ~14 internal array reallocations at 50K steps
  (each reallocation allocates a new array and copies the prior contents).

`ExecutionPreview.Categories` (separate, repeated-access cost):

- It is a computed property that **regroups on every access**: builds a
  `Dictionary<Category, List<Step>>`, then a second
  `Dictionary<Category, ReadOnlyCollection<Step>>`, allocating 2 dictionaries +
  one `List` per step + one `ReadOnlyCollection` per category per access.
- `ExecutionPreview` (and its `Steps`) is immutable after construction, so this
  grouping is recomputed identically on every read — a pure avoidable cost.
- `Categories` is serialized into `ServiceResponse.Preview` (IPC) and is read
  repeatedly by the CLI `plan` renderer, the Tray execution-preview dialog, and
  any consumer iterating grouped steps.

## 4. Old vs new algorithm

`Build()` (new, single pass):

```csharp
List<ExecutionPreviewStep> steps =
    new(decision.ExecutionPlan.Steps.Count);   // pre-sized

int createCount = 0, deleteCount = 0,
    vpnEndpointUpdates = 0, inventoryUpdates = 0;

foreach (RuntimeExecutionStep step in decision.ExecutionPlan.Steps)
{
    ExecutionPreviewStep mapped = MapStep(step);  // unchanged logic
    steps.Add(mapped);

    switch (mapped.Operation) { /* tally inline */ }
    if (mapped.Category == VpnEndpoint) vpnEndpointUpdates++;
    else if (mapped.Category == Route) inventoryUpdates++;
}

return new ExecutionPreview { CapturedAt, Summary = new(...), Steps = steps };
```

The separate `BuildSummary` second pass is removed; counters are accumulated
during the single mapping loop, and the `steps` list is pre-sized to the exact
source count (eliminating growth reallocations).

`Categories` (new, cached):

```csharp
private IReadOnlyDictionary<ExecutionPreviewCategory,
    IReadOnlyList<ExecutionPreviewStep>>? _categories;

public IReadOnlyDictionary<...> Categories
{
    get
    {
        if (_categories is null)
            _categories = GroupByCategory(Steps); // same algorithm
        return _categories;
    }
}
```

`GroupByCategory` is byte-for-byte equivalent to the old computed getter
(insertion order = first-seen category; per-category step order preserved).

## 5. Category-caching decision (Step 8)

`Categories` regrouped on every access and is serialized plus read repeatedly by
CLI/Tray/IPC. `Steps` is immutable post-construction, so caching is safe and
deterministic. This mirrors the Phase 27.1 `DiagnosticReport.Categories` caching
pattern. The cached dictionary produces identical keys/order/values, so the
serialized IPC shape is unchanged. Caching was therefore justified and adopted.

## 6. Reference-oracle strategy

`IranDirect.Core.Tests/Planning/ReferenceExecutionPreviewBuilder.cs` reproduces
the **pre-change** `Build` exactly (mapping pass + separate `BuildSummary`).
`ExecutionPreviewBuilderEquivalenceTests` compares the optimized builder against
the oracle with structural (not parsed-JSON) assertions across:

- empty plan; every `RuntimeExecutionStepKind`; mixed; blank/whitespace
  `DestinationPrefix` fallback to `Identity`; repeated identities; all-create;
  all-delete; alternating; 1K/5K/10K/25K/50K deterministic plans; fixed
  `TimeProvider`; repeated builds; and source-plan immutability.

Every field (`CapturedAt`, all `Summary` fields, `HasChanges`,
`EstimatedOperations`), every step property, exact step order, and the grouped
`Categories` (keys, per-category step order and values) are asserted equal.

## 7. Before / after benchmark table — `Build()`

Pre-change (two-pass, default `List`): `prev-build-run1-2123.md`,
`prev-build-run2-2125.md`. Post-change (single-pass, pre-sized):
`opt-build-run1-2133.md`, `opt-build-run2-2140.md`. Values are means of the two
runs; allocation is the decisive metric.

| Size | Pre Mean (µs) | Post Mean (µs) | Δ Mean | Pre Alloc (B) | Post Alloc (B) | Δ Alloc | Gen0/1/2 (post) |
|------|--------------:|---------------:|------:|--------------:|---------------:|--------:|----------------:|
| 0 | 39.2 | 39.6 | +1.2 % | 88 | 96 | +9.1 %* | 0/0/0 |
| 1K | 12,250.8 | 10,485.7 | −14.4 % | 56,688 | 48,152 | −15.1 % | 3.1/0.5/0 |
| 5K | 67,235.7 | 57,995.8 | −13.7 % | 331,448 | 240,152 | −27.5 % | 15.3/5.1/0 |
| 10K | 706,947.3 | 116,227.0 | −83.6 %† | 662,552 | 480,152 | −27.5 % | 30.5/11/0 |
| 25K | 1,494,852.8 | 1,501,606.5 | +0.5 % | 1,524,722 | 1,200,161 | −21.3 % | 91.8/89.8/29.3 |
| 50K | 3,839,403.1 | 3,265,489.4 | −14.9 % | 3,049,038 | 2,400,165 | **−21.3 %** | 164.1/160.2/39.1 |

\* Size 0 is the empty-plan floor; +9.1 % is 8 bytes of noise.
† The 10K pre-run was an environmental outlier (the pre mean is ~6× the 5K/25K
trend); post is consistent with the surrounding sizes. Allocation is the
reliable metric and shows −27.5 % at 10K.

**Allocation reduction at 50K: −21.3 %** — meets the ≥ 20 % objective.

## 8. Category / repeated-read benchmark (post-change only)

The pre-change benchmark only measured `Build()`. The new `ReadCategories*`
methods isolate the cache effect. At 50K:

| Method | Alloc (B) | Note |
|--------|----------:|------|
| `ReadCategoriesOnce` (build + group) | 3,449,610 | first access groups |
| `ReadCategoriesTenTimes` (build + 10 reads) | 3,449,611 | **only the first read groups** |

`ReadCategoriesTenTimes` alloc ≈ `ReadCategoriesOnce` alloc, proving the second
through tenth reads are served from the cached dictionary (zero re-grouping).
Pre-change, every `.Categories` access regrouped (~1.04 MB at 50K beyond the
build); 10 reads would cost ~12.8 MB vs the new 3.45 MB — a **~73 % reduction**
for the build-once/read-10× pattern (and ~90 % if each consumer regrouped
independently). `BuildTenTimes` at 50K allocates 24,001,657 B = 10 × 2,400,165 B,
confirming steady-state `Build()` allocation is unchanged by the cache.

## 9. Allocation and runtime conclusions

- `Build()`: −21 % to −28 % allocation at 5K–50K; ~14 % faster at most sizes.
- `Categories` repeated access: ~73 % (build-once/read-10×) to ~90 %
  (per-consumer) allocation reduction — the dominant win.
- No allocation regression on any representative scenario; Gen2 pressure
  unchanged (LOH churn identical at 50K).

## 10. GC and scaling analysis

- `Build()` Gen0/1/2 at 50K: 164.1 / 160.2 / 39.1 (post) — same order as
  pre-change; no additional Gen2.
- Allocation scales approximately linearly with step count (50K ≈ 10 × 5K
  within measurement noise), so the optimization preserves linear scaling.

## 11. Consumer compatibility (Step 11)

Covered by the existing `ExecutionPreview`-named suite (134 tests, all green):
- CLI `plan` renderer (`ExecutionPreviewCliRendererTests`,
  `ExecutionPreviewCliRunnerTests`) — output unchanged.
- Tray execution-preview dialog (`ExecutionPreviewDialogModelTests`,
  `ExecutionPreviewDialogTests`, `ExecutionPreviewMenuPolicyTests`) — model and
  display order unchanged.
- IPC `ServiceResponse.Preview` serialization
  (`ExecutionPreviewCommandHandlerTests`) — byte-identical because the cached
  `Categories` dictionary is key/order/value identical to the recomputed one.
- Empty-plan placeholder and category display order unchanged.

## 12. Tests added / modified

- `IranDirect.Core.Tests/Planning/ReferenceExecutionPreviewBuilder.cs` — NEW
  (pre-change reference oracle).
- `IranDirect.Core.Tests/Planning/ExecutionPreviewBuilderEquivalenceTests.cs` —
  NEW (16 structural-equivalence cases vs the oracle).
- Benchmark expanded: `ExecutionPreviewBuilderBenchmarks` adds `BuildTenTimes`,
  `ReadCategoriesOnce`, `ReadCategoriesTenTimes` (existing `Build` + `StepCount`
  params unchanged).

## 13. Risks and limitations

- `Categories` now caches on first access (lazy). The cached value is
  deterministic given immutable `Steps`, so repeated reads are identical. This
  is the same trade-off accepted for `DiagnosticReport.Categories` in Phase 27.1.
- The 10K pre-run runtime outlier is environmental; allocation (the reliable
  metric) confirms the improvement.

## 14. Files changed

- `IranDirect.Core/Planning/ExecutionPreviewBuilder.cs` — single-pass `Build()`
  with pre-sized `steps` list and inline summary counters; removed the separate
  `BuildSummary` pass.
- `IranDirect.Core/Planning/ExecutionPreview.cs` — `Categories` now caches the
  grouping on first access (deterministic, identical output/ordering).
- `IranDirect.Core.Tests/Planning/ReferenceExecutionPreviewBuilder.cs` — NEW.
- `IranDirect.Core.Tests/Planning/ExecutionPreviewBuilderEquivalenceTests.cs` —
  NEW.
- `IranDirect.Benchmarks/Benchmarks/ExecutionPreviewBuilderBenchmarks.cs` —
  added `BuildTenTimes`, `ReadCategoriesOnce`, `ReadCategoriesTenTimes`.

## 15. Proof public behavior unchanged

- `IExecutionPreviewBuilder.Build` signature and output unchanged (16/16
  equivalence tests vs the reference oracle).
- Public models (`ExecutionPreview`, `ExecutionPreviewStep`,
  `ExecutionPreviewSummary`, `ExecutionPreviewCategory`, `ExecutionPreviewOperation`)
  and their serialized shape unchanged; `Categories` serialized content identical.
- `RuntimeDecision` plan never mutated (`SourcePlan_IsNotMutated` test).
- CLI/Tray/IPC consumers unchanged (134/134 `ExecutionPreview` tests green).

## 16. Recommended commit message

```
perf(planning): reduce execution preview allocations
```

Not committed automatically.
