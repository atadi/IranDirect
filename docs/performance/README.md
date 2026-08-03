# Performance baselines

## Runtime executor stress harness

The runtime executor stress harness in
`IranDirect.Core.Tests/Performance/Execution/` (infrastructure in
`IranDirect.Testing/Performance/Execution/`) drives `RuntimeExecutor` with
large synthetic plans and a controlled fake handler. It is fully
deterministic: every scenario is orchestrated with identity-keyed gates and
concurrency probes, never with timing sleeps, and it exercises the existing
step-handler contract only (no production code is modified).

- Scale matrix: 1K, 2K, 5K, and 10K plans run in the regular suite; 25K and
  50K plans are tagged `Category=Stress`.
- Workload kinds: prefix-route creates, prefix-route deletes, endpoint-route
  creates, endpoint-route deletes, and mixed execution groups (planner order:
  AddEndpointRoute → RemovePrefixRoute → AddPrefixRoute → RemoveEndpointRoute).
- Scenarios: all-success, single failure early/middle/late, multiple
  failures, cancellation before start, cancellation during a group, mixed
  latency (external gates), and benign-race compensation via the group
  verification phase.
- Invariants verified: final results in plan order, stable final progress,
  concurrency never above `MaxDegreeOfParallelism` (8), endpoint groups
  strictly sequential, prefix groups bounded-parallel, no held gates or
  incomplete tasks, and executor reuse after failure/cancellation.

```powershell
# everything, including stress
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj

# stress cases only (25K/50K)
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category=Stress"

# regular suite, skipping stress
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category!=Stress"
```

## Persistence endurance harness

The persistence endurance harness in
`IranDirect.Core.Tests/Performance/Persistence/` (infrastructure in
`IranDirect.Testing/Performance/Persistence/`) drives the JSON-backed
persistence components through deterministic read/write, concurrency,
failure-recovery, cancellation, retention, and cleanup scenarios. It uses
each store's public API only (`JsonStore<T>`, `RouteInventoryStore`,
`VpnEndpointInventoryStore`, `StateRepository`, `CustomRouteStore`,
`CustomRouteDnsCacheStore`, `PrefixSourceMetadataStore`,
`DesiredConfigurationStore`, `PrefixSourceUpdateHistoryRepository`,
`RuntimePerfReportStore`) and never touches production persistence code.

- Scenarios: 1K sequential cycles in the regular suite and 10K+ cycles in
  the `Category=Stress` tier; controlled concurrent readers/writers
  (single store instance, no lost updates); cross-instance readers against
  one writer (no sharing violations escape the existing retry); injected
  fault cycles through `JsonLoad`, `JsonSave`, `FileRead`, `FileWrite`, and
  `FileMove` with destination preservation, temp cleanup, and recovery;
  stale-temp behavior; pre-cancelled and while-waiting cancellation with
  gate release; corrupt-file `JsonException` contract then repair by a
  subsequent save; prefix history retention (newest-first, no unbounded
  growth); perf-report latest-by-trigger ordering, `ListAsync` order, and
  legacy `cycle-<stamp>-<trigger>.json` filename compatibility; and
  cross-store isolation.
- Determinism: fixed seed (`20260803`) and record schedules, a single
  task-completion-source gate for concurrency, no timing sleeps, and no
  wall-clock thresholds. The only wall-clock values captured (elapsed time,
  process handle count) are informational metrics, never assertions.
- Resource-safety checks after every scenario: no orphaned `*.tmp` files,
  the final file is complete JSON openable with `FileShare.None` (no held
  handles), and each store remains responsive after faults and
  cancellation.

```powershell
# everything, including stress
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj

# stress cases only (10K+ cycles, concurrent rounds, all-store matrix)
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category=Stress"

# regular suite, skipping stress
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category!=Stress"
```



This folder documents the deterministic performance baseline suite for the
IranDirect core algorithms.

## What is measured

The suite measures only **pure, read-only** algorithm paths. Benchmarks run on
prebuilt, immutable inputs from the deterministic workload generator in
`IranDirect.Testing` (`RuntimeWorkloadGenerator`). No network, DNS, route
table, file, or ZIP I/O happens inside a measured method.

| Benchmark class | Measured path | Inputs |
| --- | --- | --- |
| `PrefixDatasetComparerBenchmarks` | `PrefixDatasetComparer.Compare` | identical / all-added / all-removed / mixed datasets |
| `RuntimeChangeSetPlannerBenchmarks` | `RuntimeChangeSetPlanner.Plan` | AllMissing / AllPresent / AllObsolete / Mixed / DuplicateInput workloads |
| `ExecutionPreviewBuilderBenchmarks` | `ExecutionPreviewBuilder.Build` | empty, 1K, 5K, 10K, 25K, 50K execution steps |
| `DiagnosticReportBenchmarks` | `DiagnosticReport` summary, category grouping, `DiagnosticReportFormatter` detailed + compact | 10, 100, 1K, 5K diagnostic results |
| `SupportSnapshotSerializerBenchmarks` | `SupportSnapshotSerializer.Serialize` | small / medium / large support snapshots |

Dataset-scale scenarios use 1K, 5K, 10K, 25K, and 50K entries. All generator
inputs are deterministic (default seed, see
`RuntimeWorkloadGenerator.DefaultSeed`).

## Quick mode

Quick mode is intended for a fast sanity check that the benchmarks execute
and produce plausible results. Results from quick mode are **not** suitable
for comparison against other machines.

```powershell
dotnet run --project IranDirect.Benchmarks --configuration Release -- --quick --filter *
```

## Full mode

Full mode is the default and is intended for baseline recording. Run every
benchmark:

```powershell
dotnet run --project IranDirect.Benchmarks --configuration Release -- --filter *
```

Or a subset, for example:

```powershell
dotnet run --project IranDirect.Benchmarks --configuration Release -- --filter "*PrefixDatasetComparerBenchmarks*"
```

Record the full run on the reference machine once, capture the
`BenchmarkDotNet.Artifacts` output, and keep the machine details from
`baseline-environment-template.md`.

## Prerequisites

- `dotnet` SDK that supports `net10.0` and .NET 10 runtime.
- **Release** build. A Debug build throws
  `InvalidOperationException: Debug build, JIT optimization disabled`.
- BenchmarkDotNet artifact output is written to
  `BenchmarkDotNet.Artifacts/` (ignored by git).

## Result format

BenchmarkDotNet reports nanoseconds per operation (mean, median, error,
standard deviation) and, because `MemoryDiagnoser` is attached, allocated
bytes and allocation count. Trust only full-mode numbers.

## Interpreting results

- Baseline timings are **machine-specific**. Compare runs recorded on the
  same machine, same environment (see the baseline template), and same full
  launch mode.
- The suite is a baseline for spotting regressions, not a spec.
- Known quirk: the workload generator derives route gateways from the route
  index rather than the prefix string, so scenarios with equal count/seed
  share gateway personality. The planner distinguishes routes by full
  identity, so this does not skew the measured paths.
