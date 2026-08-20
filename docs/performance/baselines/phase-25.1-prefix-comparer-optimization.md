# Phase 25.1 — PrefixDatasetComparer Optimization

**Goal met:** reduced `PrefixDatasetComparer.Compare` runtime and
allocations while preserving exact observable behavior. No public type,
signature, prefix-source update, metadata, history, routing, scheduling,
IPC, CLI, Tray, Service, or worker behavior changed.

## 1. Files changed

Production (only the comparer):
- `IranDirect.Core/Prefixes/PrefixDatasetComparer.cs`

Tests (new, under `IranDirect.Core.Tests`):
- `IranDirect.Core.Tests/Prefixes/ReferencePrefixDatasetComparer.cs`
  — test-only oracle reproducing pre-optimization behavior (lives in the
  test project, never shipped in production).
- `IranDirect.Core.Tests/Prefixes/PrefixDatasetComparerEquivalenceTests.cs`
  — runs the semantics matrix and a deterministic generated sweep through
  both the optimized comparer and the reference oracle.

Documentation:
- `docs/performance/baselines/phase-25.1-prefix-comparer-optimization.md`
  (this file)
- `docs/performance/README.md` — link added.

No benchmark class, workload generator, or other production file was
modified.

## 2. Pre-change baseline

- Pre-change commit: `f3af1f2ebaf52d2e31f6e284d4fb745ba874eacf`
  (branch `development/service-authority`).
- Comparison baseline: `prefix-before-2306.md` (full-mode BDN rerun of the
  unoptimized comparer on this machine, used as the immediate
  before/after pair).
- Phase 24.1 recorded Mixed 50K ≈ 11.22 ms / 10.00 MB; the quieter
  rerun here measured Mixed 50K = 17,845 µs / 10,004 KB (same order of
  magnitude; the 24.1 figure was a noisier single run).
- Machine: Windows 11 Pro 10.0.26200, i7-13700 (16P/24L), 63.75 GB,
  .NET 10.0.10, BenchmarkDotNet 0.15.8, High performance power plan, AC.
  Residual background load present (WSL VM ~2.5 GB, Visual Studio,
  opencode, ChatGPT, Opera ×3, Defender, Discord) — see §9.

## 3. Hot-path analysis

The baseline `Compare` did this per call:

1. `Normalize(old)` → `Select(Trim).Where(non-empty)` (streaming).
2. `new HashSet(oldNormalized, OrdinalIgnoreCase)` — first materialization.
3. `Normalize(new)` → second streaming pass.
4. `new HashSet(newNormalized, OrdinalIgnoreCase)` — second materialization.
5. `newSet.Except(oldSet, OrdinalIgnoreCase)` — BCL builds a **third**
   internal set of the subtracted elements, then `.OrderBy(Ordinal)`
   (stable sort: index-array + sort) then `.ToArray()` → **added**.
6. `oldSet.Except(newSet, ...)` — a **fourth** internal set,
   `.OrderBy(...).ToArray()` → **removed**.
7. `oldSet.Count(newSet.Contains)` — a **fifth** pass over oldSet.
8. `added.AsReadOnly`/`removed` returned as `IReadOnlyList` wrappers.

Per call this allocates **two input sets + two `Except` sets + two
index arrays + two result arrays + two `ReadOnlyCollection` wrappers**,
and performs **five** enumeration passes. The `OrderBy` stable sorts and
the `Count(Contains)` pass are pure overhead versus a single forward
iteration.

## 4. Old versus new algorithm

**Old**
```
oldSet  = HashSet(Normalize(old))
newSet  = HashSet(Normalize(new))
added   = newSet.Except(oldSet).OrderBy(Ordinal).ToArray()
removed  = oldSet.Except(newSet).OrderBy(Ordinal).ToArray()
unchanged = oldSet.Count(newSet.Contains)
```

**New**
```
oldSet  = BuildSet(old)     // single pass: Trim, drop blanks, add to set
newSet  = BuildSet(new)
added   = List(capacity oldSet.Count)
removed  = List(capacity oldSet.Count)
foreach p in newSet:
    if !oldSet.Contains(p): added.Add(p)
    else: unchanged++
foreach p in oldSet:
    if !newSet.Contains(p): removed.Add(p)
    else: unchanged++
added.Sort(Ordinal)         // in-place, single array
removed.Sort(Ordinal)
return diff
```

Changes:
- Normalization folded into a single `BuildSet` pass (one `Trim` per
  input element, blanks dropped) instead of a separate streaming
  `Normalize` + `HashSet` constructor call.
- The two `Except(...)` operations (each internally allocating a set) are
  replaced by direct `HashSet.Contains` probes inside forward `foreach`
  loops — no intermediate subtraction sets.
- `OrderBy(...).ToArray()` is replaced by `List<T>.Sort(StringComparer
  .Ordinal)` — an in-place comparison sort with no index-array, no
  secondary array, no `ReadOnlyCollection` wrapper (the `List<string>` is
  already `IReadOnlyList<string>`).
- The separate `Count(Contains)` pass is eliminated; `unchanged` is
  tallied during the two existing loops.
- String comparers are reused (`StringComparer.OrdinalIgnoreCase` for the
  sets, `StringComparer.Ordinal` for the sorts) — no new comparer
  instances per call.

No global caches, no static mutable state, no pooling, no parallelism,
no culture-sensitive comparison, no unsafe code. The public
`Compare(IEnumerable<string>, IEnumerable<string>) : PrefixDatasetDiff`
signature and `PrefixDatasetDiff` shape are unchanged.

## 5. Semantic-equivalence strategy

`ReferencePrefixDatasetComparer` (test-only) reproduces the original
`Compare` verbatim — two `Normalize` passes feeding case-insensitive
`HashSet`s, two `Except(...).OrderBy(...)` pipelines, and the
`Count(Contains)` unchanged tally. The optimized comparer is validated
against it, not against hand-written expectations, so any divergence in
sorting, dedup, blank handling, or classification is caught.

`PrefixDatasetComparerEquivalenceTests` asserts, for every case, exact
equality of `AddedCount`, `RemovedCount`, `UnchangedCount`, `HasChanges`,
`AddedPrefixes` (sequence), and `RemovedPrefixes` (sequence).

Covered (hand-picked):
- empty/empty, empty/current, previous/empty, identical, all-added,
  all-removed, mixed, duplicates, blank entries, whitespace-trimmed,
  case-only differences, unsorted inputs, repeated values with different
  casing.

Covered (deterministic generated, no RNG):
- sizes 0, 1, 10, 100, 1,000, 10,000, 50,000;
- scenarios identical, all-added, all-removed, mixed (parity with the
  benchmark `PrefixDatasetFactory`);
- synthetic duplicate-heavy, whitespace-heavy, case-variant-heavy.

No timing assertions are used in correctness tests.

## 6. Tests added or modified

New files (see §1). The pre-existing `PrefixDatasetComparerTests` (11
tests) continues to pass unchanged. The new equivalence suite adds 62
cases (13 hand-picked + 28 scenario×size + 21 synthetic). Result:
focused `PrefixDatasetComparer` filter → **73 passing**; full
`IranDirect.Core.Tests` suite → **1806 passing**; `Category=Stress` →
**12 passing**; benchmark project `Release` build → 0 errors / 0
warnings.

## 7. Before / after benchmark table

Full-mode BenchmarkDotNet; "Before" = `prefix-before-2306.md`,
"After" = average of `prefix-after1-2312.md` and `prefix-after2-2318.md`.
Focus scenarios per spec.

### Runtime (Mean µs) and allocation (KB)

| Scenario | Size | Before Mean | After Mean | Mean Δ | Before Alloc | After Alloc | Alloc Δ |
|----------|-----:|------------:|-----------:|-------:|-------------:|------------:|--------:|
| Identical | 1000 | 202.0 | 137.1 | -32.1% | 219.3 | 158.7 | -27.6% |
| Identical | 10000 | 2,627.4 | 1,806.9 | -31.2% | 2,000.0 | 1,471.1 | -26.4% |
| Identical | 50000 | 12,646.9 | 8,764.8 | -30.7% | 8,632.1 | 6,463.2 | -25.1% |
| AllAdded | 1000 | 167.1 | 101.5 | -39.2% | 208.9 | 87.9 | -57.9% |
| AllAdded | 10000 | 2,489.7 | 1,568.0 | -37.0% | 1,931.3 | 913.9 | -52.7% |
| AllAdded | 50000 | 13,126.9 | 9,823.5 | -25.2% | 8,529.3 | 3,865.6 | -54.7% |
| AllRemoved | 1000 | 163.5 | 102.2 | -37.5% | 208.9 | 87.4 | -58.2% |
| AllRemoved | 10000 | 2,492.3 | 1,511.8 | -39.3% | 1,931.3 | 813.9 | -57.9% |
| AllRemoved | 50000 | 12,967.6 | 9,819.9 | -24.3% | 8,524.1 | 3,622.6 | -57.5% |
| Mixed | 1000 | 224.8 | 168.4 | -25.1% | 247.0 | 158.8 | -35.7% |
| Mixed | 10000 | 3,159.3 | 2,324.2 | -26.4% | 2,274.0 | 1,471.2 | -35.3% |
| Mixed | 50000 | 17,845.4 | 13,052.3 | -26.9% | 10,004.8 | 6,465.5 | **-35.4%** |

### Gen0/Gen1/Gen2 (per call, 50K Mixed)

| Phase | Gen0 | Gen1 | Gen2 |
|-------|-----:|-----:|-----:|
| Before | 1,968.75 | 1,937.50 | 1,937.50 |
| After | 1,000.00 | 968.75 | 968.75 |

GC pressure roughly halves at 50K Mixed (Gen0 ~1,969 → ~1,000).

## 8. Acceptance-criteria result

- **Semantic:** exact equivalence tests pass (62 new + 11 existing);
  no public behavior change; full suite (1806) and stress (12) green. ✓
- **Allocation (Mixed 50K):** 10,004.8 → 6,465.5 KB = **-35.4%** —
  exceeds the "≥ 20% lower" objective. ✓
- **Allocation (no regression > 5%):** every scenario reduced 25–58%;
  worst is Identical 50K -25.1%. No scenario regressed. ✓
- **Runtime:** every scenario -24% to -39% faster; no repeatable
  regression > 10% (the "max regression" is -24.3%, i.e. faster). ✓
- **Stability:** run1 vs run2 mean spread ≤ 4.1% (Mixed 50K), allocation
  spread 0.0% — highly reproducible. ✓

## 9. Benchmark noise

The machine ran under a High performance plan with significant background
load (WSL VM ~2.5 GB, Visual Studio, opencode, ChatGPT, Opera ×3,
Defender, Discord). Despite this, run1 and run2 were within ~4% on
runtime and identical on allocation, confirming the allocation win is
robust. A fully quiet re-run would only tighten the runtime error bars.

## 10. Risks and remaining opportunities

- **Behavior is preserved, not re-derived.** The optimization only
  changes *how* sets are built and classified; trimming, blank removal,
  case-insensitive dedup/comparison, ordinal sort order, and all counts
  are byte-for-byte identical (proven by the reference oracle).
- **`unchanged` counting is robust** to differing duplication between the
  two inputs (it is the size of the intersection, computed by counting
  membership hits, not a count difference) — verified by the
  duplicate-heavy / case-variant-heavy equivalence cases.
- **Remaining opportunity (deferred, out of scope):** the two `BuildSet`
  passes still allocate a `HashSet` each; a single combined pass that
  partitions added/removed/unchanged in one iteration over both inputs is
  possible but would complicate the code for marginal gain. Readability
  was preferred per the phase guidance.
- **Scaling remains superlinear** under 50K (inherent to the set
  membership probes); this phase reduces the constant factor and
  allocations but does not change asymptotic complexity.

## 11. Recommended commit message

```
perf(prefixes): reduce dataset comparison allocations
```

(Not committed — as instructed.)
