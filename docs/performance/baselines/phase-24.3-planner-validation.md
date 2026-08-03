# Phase 24.3 — RuntimeChangeSetPlanner Post-Optimization Validation

**Purpose:** validate the Phase 24.2 planner optimization under a quieter
benchmark environment, reconfirm semantic equivalence, and establish the
optimized planner as the new reference baseline.

**Scope:** validation and documentation only. No planner, test, benchmark,
workload-generator, or production change was made. This phase touches
documentation files exclusively.

## 1. Optimized commit and branch

- Commit: `e263d050437096d53d59b0218d7c6635889953dd`
- Message: `perf(planning): reduce route planner allocations`
- Branch: `development/service-authority`
- Pre-change baseline was `f78d2e7` (Phase 24.2 pre-optimization run,
  `planner-before-2142.md`); the optimized code shipped in `e263d05`.

## 2. Environment (quiet-machine attempt)

| Item | Value |
|------|-------|
| OS | Microsoft Windows 11 Pro 10.0.26200 (25H2) |
| CPU | 13th Gen Intel Core i7-13700, 2.10 GHz, 16 phys / 24 log |
| RAM | 63.75 GB |
| Power plan | **High performance** (GUID `8c5e7fda-…`, active) |
| Power source | AC |
| .NET SDK | 10.0.302 |
| .NET runtime | Microsoft.NETCore.App 10.0.10 |
| BenchmarkDotNet | 0.15.8 |
| BDN config | IterationCount=7, LaunchCount=1, WarmupCount=5, Throughput |

**Residual load (honest disclosure):** the machine was *not* fully idle.
The following heavy processes were observed during benchmarking:
`vmmemWSL` (~2.6 GB), `devenv.exe`/Visual Studio (~1.1 GB),
`opencode.exe` (~0.85 GB), `ChatGPT Classic` (~0.93 GB),
`opera.exe` ×4 (~2.0 GB total), `MsMpEng`/Defender (~0.55 GB),
`Discord.exe` (~0.35 GB), `DorsanDesk` (~0.31 GB). The CLI agent did
**not** forcibly terminate these interactive applications or shut down
WSL/Docker, to avoid destroying unsaved work or live workloads. The power
plan was already High performance and AC power was assumed. The residual
load means runtime (Mean) is somewhat noisier than a fully quiet lab
machine would show, but it does **not** materially affect the
allocation (byte-count) measurements, which are GC-profiler precise and
showed 0.0% run-to-run spread (see §6).

## 3. Commands executed

```
git status --short                 # clean (precondition)
git rev-parse HEAD                 # e263d05
git branch --show-current          # development/service-authority
git log -3 --oneline

dotnet clean
dotnet build                       # 0 error, 0 warning
dotnet test                        # full suite
dotnet test --filter "FullyQualifiedName~RuntimeChangeSetPlanner|FullyQualifiedName~PlannerEquivalenceTests"
dotnet test --filter "Category=Stress"

dotnet run -c Release --project .\IranDirect.Benchmarks -- --filter "*RuntimeChangeSetPlannerBenchmarks*"
# repeated once (run 1, run 2)
```

No benchmark parameter, workload generator, or source file was modified.

## 4. Correctness verification (§3 results)

| Gate | Result |
|------|--------|
| `dotnet build` | 0 error, 0 warning |
| Full suite `dotnet test` | **1744 passed**, 0 failed |
| Planner + equivalence filter | **34 passed** (6 `RuntimeChangeSetPlannerTests` + 28 `PlannerEquivalenceTests`) |
| Stress suite (`Category=Stress`) | **12 passed**, 0 failed |

The 28 equivalence cases run every planned scenario (AllMissing,
AllPresent, AllObsolete, Mixed, EndpointMixed, DuplicateInput) at 1K /
10K / 50K through both the optimized planner and a test-only
`ReferenceChangeSetPlanner` (verbatim pre-optimization logic), asserting
exact element equivalence (`Kind`, `Identity`, `DestinationPrefix`,
`Gateway`, `InterfaceIndex`, `Metric`, `Description`) after independent
`(Kind, Identity)` sorting. All pass → **no observable behavior change**.

## 5. Benchmark runs (§4)

Both runs executed the planner benchmark class alone, Full mode, on the
environment in §2. Raw artifacts:
`phase24.3-run1-2225.md`, `phase24.3-run2-2230.md`.

### Run 1 — selected scenarios (Mean µs, Allocated KB, Gen0/1/2)

| Scenario | Size | Mean (µs) | StdDev (µs) | Allocated (KB) | Gen0/1/2 |
|----------|-----:|----------:|------------:|---------------:|----------|
| AllMissing | 50000 | 32,311.1 | 463.5 | 24,967.45 | 2437/2375/1250 |
| AllPresent | 1000 | 175.8 | 1.00 | 291.70 | 19/9/0 |
| AllPresent | 10000 | 2,772.4 | 17.36 | 2,917.68 | 246/121/121 |
| AllPresent | 50000 | 20,164.4 | 433.7 | 14,892.30 | 1062/1031/281 |
| AllObsolete | 50000 | 24,560.3 | 253.1 | 15,437.69 | 1219/1188/469 |
| Mixed | 1000 | 283.8 | 4.72 | 480.72 | 31/18/0 |
| Mixed | 10000 | 4,933.3 | 40.35 | 4,969.56 | 398/390/195 |
| Mixed | 50000 | 38,598.1 | 446.8 | 24,322.65 | 1846/1769/615 |
| DuplicateInput | 1000 | 299.6 | 0.84 | 686.36 | 44/29/0 |
| DuplicateInput | 10000 | 6,312.8 | 89.87 | 6,926.07 | 531/461/266 |
| DuplicateInput | 50000 | 52,827.1 | 2,585.2 | 34,351.99 | 2600/2500/900 |

### Run 2 — selected scenarios

| Scenario | Size | Mean (µs) | StdDev (µs) | Allocated (KB) | Gen0/1/2 |
|----------|-----:|----------:|------------:|---------------:|----------|
| AllMissing | 50000 | 32,155.4 | 653.8 | 24,967.26 | 2437/2375/1250 |
| AllPresent | 1000 | 172.0 | 2.21 | 291.70 | 19/9/0 |
| AllPresent | 10000 | 2,752.6 | 20.92 | 2,917.68 | 246/121/121 |
| AllPresent | 50000 | 19,702.7 | 295.5 | 14,892.28 | 1062/1031/281 |
| AllObsolete | 50000 | 24,310.1 | 292.2 | 15,437.69 | 1219/1188/469 |
| Mixed | 1000 | 276.1 | 3.14 | 480.72 | 31/18/0 |
| Mixed | 10000 | 4,854.5 | 83.93 | 4,969.55 | 406/390/188 |
| Mixed | 50000 | 37,202.4 | 498.8 | 24,321.87 | 1857/1786/643 |
| DuplicateInput | 1000 | 308.1 | 8.96 | 686.36 | 44/29/0 |
| DuplicateInput | 10000 | 6,270.5 | 64.95 | 6,926.03 | 539/469/258 |
| DuplicateInput | 50000 | 47,755.7 | 874.5 | 34,351.97 | 2636/2545/909 |

## 6. Comparison vs Phase 24.2 pre-change baseline

Pre-change source: `planner-before-2142.md` (unoptimized planner, commit
`f78d2e7`). Optimized figures are the run-1/run-2 average.

### Allocation (KB) — primary criterion

| Scenario | Size | Pre-change | Optimized | Δ % | Within ≤ −25% / ≤ +5% ? |
|----------|-----:|-----------:|----------:|----:|------------------------|
| AllPresent | 1000 | 563.8 | 291.7 | **−48.3%** | ✓ |
| AllPresent | 10000 | 5,700.6 | 2,917.7 | **−48.8%** | ✓ |
| AllPresent | 50000 | 26,889.1 | 14,892.3 | **−44.6%** | ✓ (materially lower) |
| Mixed | 1000 | 608.3 | 480.7 | −21.0% | ✓ |
| Mixed | 10000 | 7,303.4 | 4,969.6 | **−32.0%** | ✓ |
| Mixed | 50000 | 34,703.4 | 24,322.3 | **−29.9%** | ✓ (target met) |
| AllObsolete | 50000 | 23,304.3 | 15,437.7 | **−33.8%** | ✓ |
| AllMissing | 50000 | 26,251.6 | 24,967.4 | −4.9% | ✓ (within ±5%) |
| DuplicateInput | 1000 | 650.7 | 686.4 | **+5.5%** | △ (1K, +0.5pp over bar) |
| DuplicateInput | 10000 | 6,655.4 | 6,926.0 | **+4.1%** | ✓ (within 5%) |
| DuplicateInput | 50000 | 32,349.3 | 34,352.0 | **+6.2%** | △ (50K, +1.2pp over bar) |

### Runtime (Mean µs)

| Scenario | Size | Pre-change | Optimized | Δ % |
|----------|-----:|-----------:|----------:|----:|
| AllPresent | 1000 | 291.1 | 173.9 | **−40.3%** |
| AllPresent | 10000 | 4,676.7 | 2,762.5 | **−40.9%** |
| AllPresent | 50000 | 39,717.6 | 19,933.6 | **−49.8%** |
| Mixed | 1000 | 383.6 | 280.0 | **−27.0%** |
| Mixed | 10000 | 11,003.5 | 4,893.9 | **−55.5%** |
| Mixed | 50000 | 60,636.4 | 37,900.2 | **−37.5%** |
| AllObsolete | 50000 | 39,199.4 | 24,435.2 | **−37.7%** |
| AllMissing | 50000 | 43,034.1 | 32,233.2 | **−25.1%** |
| DuplicateInput | 1000 | 318.9 | 303.9 | −4.7% |
| DuplicateInput | 10000 | 11,281.8 | 6,291.6 | **−44.2%** |
| DuplicateInput | 50000 | 78,432.9 | 50,291.4 | **−35.9%** |

## 7. Run-to-run stability (run 1 vs run 2)

| Metric | Max spread across scenarios |
|--------|-----------------------------|
| Mean (runtime) | **10.1%** (DuplicateInput 50K: 52,827 vs 47,756 µs) |
| Allocated (bytes) | **0.0%** (identical to 0.01 KB everywhere) |

The allocation measurements are perfectly reproducible (0.0% spread).
Runtime spread peaks at 10.1% on one large duplicate-saturated case,
well under the 15% documentation threshold in §6 — no performance gate is
warranted yet.

## 8. Acceptance-criteria result (§6)

### Semantic
- Exact equivalence tests pass (28 planner equivalence + 6 existing).
- No public behavior change (public `Plan` signature unchanged; only
  internal lookup materialization changed).
- All existing tests green (1744 suite + 12 stress).

### Allocation
- **Mixed 50K: −29.9%** → meets "at least 25% lower" objective. ✓
- **AllPresent 50K: −44.6%** → materially lower. ✓
- All representative scenarios reduced 5–49% except the synthetic
  duplicate-saturated `DuplicateInput`, which is **+4.1% / +5.5% / +6.2%**
  (1K / 10K / 50K). The 1K and 50K figures narrowly exceed the ≤ +5% bar
  by 0.5–1.2 percentage points. This is **real and reproducible** (0.0%
  run-to-run allocation spread), not measurement noise — see §9.

### Runtime
- Every scenario is faster than pre-change (−4.7% to −55.5%); no
  repeatable regression > 10%. The one large run-to-run gap (10.1% on
  DuplicateInput 50K) is within the documented 15% noise envelope and is
  not labeled a real regression.

## 9. DuplicateInput allocation note (honest limitation)

`DuplicateInput` is a synthetic stress scenario where desired routes
contain many duplicate identities (and observed routes too). The optimized
planner replaces the two desired `Dictionary` maps with
`HashSet<string>` membership sets sized to the **full** desired-list
length (including duplicates) plus a per-pass local `HashSet` for
first-occurrence dedup. Under heavy input duplication this over-allocates
HashSet capacity relative to the pre-change `GroupBy(...).First()`
dictionaries (which were sized to the *unique* count). The net effect is a
small, scenario-specific allocation increase of +4 to +6% on
`DuplicateInput` only — every other scenario (including the production-
representative Mixed / AllPresent / AllObsolete / AllMissing) is 5–49%
lower. Behavioral output is identical (proven by equivalence tests).
Tightening this edge (e.g. sizing HashSets to unique counts) is an
*additional* optimization and is explicitly **out of scope** for this
validation phase.

## 10. New baseline recommendation

**Yes — adopt the optimized planner (`e263d05`) as the new reference
baseline**, with the footnote above.

Evidence:
- Allocation reductions are large (Mixed 50K −29.9%, AllPresent 50K
  −44.6%, AllObsolete 50K −33.8%) and perfectly stable across runs
  (0.0% spread).
- Runtime is uniformly improved (−25% to −56% on representative cases).
- Semantic equivalence is proven by an independent reference oracle.
- The only deviation is the synthetic `DuplicateInput` +4/+6% edge, which
  does not affect any production-representative scenario and does not
  alter observable behavior.

A performance gate should **not** be created yet: the runtime
run-to-run spread reached 10.1% under residual background load, and the
machine was not fully quiet; tighten on an idle lab machine before
gating.

## 11. Files changed (§9)

- `docs/performance/baselines/phase-24.3-planner-validation.md` (new)
- `docs/performance/README.md` (link added)

No source, test, benchmark, or workload-generator file was modified.

## 12. Proof no production code changed

```
git status --short          # only the two doc files above
git diff --stat             # docs/performance/... only
git diff --check            # clean (no whitespace issues)
```

`git diff` against `e263d05` shows zero changes outside the two
documentation files. `RuntimeChangeSetPlanner.cs` is byte-identical to the
committed optimized version.

## 13. Recommended commit message

```
perf(validation): confirm planner optimization baseline
```

(Not committed — as instructed.)
