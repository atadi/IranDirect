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



## Lifecycle simulation harness

The lifecycle simulation harness in
`IranDirect.Core.Tests/Performance/Lifecycle/` (infrastructure in
`IranDirect.Testing/Performance/Lifecycle/`) simulates normal service
activity over many repeated cycles to validate lifecycle stability and
cumulative behavior. It drives the real orchestration components
(`IranDirectController`, `RuntimeCycleCoordinator`, `RuntimeExecutor`,
`RuntimeSnapshotProvider`, `DiagnosticRunner`, `RuntimePreviewPlanner`,
`PrefixUpdateMonitor`, `CustomRouteResolver`, `SupportBundleExporter`,
`RuntimePerfReportStore`) against controlled fakes.

### Simulation architecture

A `ServiceSimulationPlan` is an ordered list of `IServiceSimulationStep`
values (`RunCycleStep`, `SnapshotStep`, `DiagnosticsStep`, `PreviewStep`,
`MonitorForceCheckStep`, `ResolveCustomRoutesStep`,
`ExportSupportBundleStep`, `AdvanceTimeStep`, `FaultScopeStep`,
`CompositeStep`, `RepeatStep`, ...). `ServiceSimulationRunner` executes a
plan against a `SimulatedRuntimeEnvironment`, accumulating a
`ServiceSimulationMetrics` counter set, a list of `ResourceSample`
values, and `ResourceTrendAnalyzer` trend lines.
`ServiceSimulationVerifier` then asserts the stability invariants.

### Fake dependencies

No real Windows route table, network, DNS, HTTP, named pipe, or user
profile file is touched:

- `SimulatedRouteTable` / `FakeWindowsRouteApi` — in-memory route table
  (note: one `Add`/`Delete` call is recorded per route, not per batch).
- `ScriptedDnsResolver` — deterministic address sets and scripted
  failures, injected as the resolver's lookup function.
- `ScriptedPrefixUpdateChecker` / `CountingPrefixUpdateChecker` —
  replayable `Current` / `UpdateAvailable` / `Unknown` / `Failed`
  outcomes with concurrency counting.
- `TemporaryLifecycleWorkspace` — per-environment temp directory for
  state, perf reports, and support bundles; deleted on dispose.
- `CountingExecutionHandlerDecorator` plus the `ExecutionHold` gate —
  deterministic in-flight-call counting and cancellation points.

### Fake time

`SimulationTimeProvider` is the only clock. `AdvanceTimeStep` and
`Time.Advance(...)` move it explicitly and fire any due timers; there
are no `Task.Delay` calls, real clock waits, or unseeded random data.
Because advancing time also fires the monitor's *scheduled* check
interval, total monitor check counts can legitimately exceed the number
of forced checks — the harness asserts non-overlap rather than an exact
equality in the long sequences.

### Cycle counts

Regular suite: 100 combined operational cycles, 250 no-op repair cycles,
100 snapshot captures, 120 monitor transitions, 40 DNS transition
rounds, 25 support-bundle exports.

Stress tier: 5,000 no-op repair cycles, 1,000 combined operational
cycles, 1,000 snapshot/diagnostic/preview cycles, 500 support-bundle
exports, and a 500-round monitor + DNS transition sequence.

### Resource samples

`ResourceSample` records the cycle number, managed memory, GC collection
counts, thread and handle counts where supported, active handler calls,
workspace/temp/bundle file counts, and route/inventory counts. Samples
are captured at baseline, at fixed intervals (`SampleEvery`), and at
completion.

**There are deliberately no strict memory, handle, or thread
thresholds.** Those values are reported as informational trends only.
The assertions that do run are structural: active handler calls return
to zero, no orphan `*.tmp` files remain, every JSON parses, every ZIP
opens, the DNS cache holds at most one record per domain, and the route
inventory does not grow across equivalent cycles.

### Commands

```powershell
# regular lifecycle tests
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "FullyQualifiedName~Performance.Lifecycle"

# lifecycle stress only (isolated from executor/persistence stress)
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category=Stress&Area=Lifecycle"

# all non-stress tests
dotnet test .\IranDirect.Core.Tests\IranDirect.Core.Tests.csproj --filter "Category!=Stress"
```

Lifecycle stress tests carry both `Category=Stress` and
`Area=Lifecycle`, because `Category=Stress` alone also selects the
executor and persistence stress suites.

### Approximate runtime

On the reference development machine the full lifecycle filter (41
tests) completes in roughly 45 seconds, of which the five stress cases
account for about 43 seconds. These figures are machine-specific
observations, not universal targets or performance gates.


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

## Recorded baselines

- [Phase 24.1 full performance baseline](baselines/phase-24.1-baseline.md)
  — complete five-family Full-mode run (commit `628d64a`) with scaling,
  allocation, cross-benchmark ranking, repeat-run stability, and the
  selected first optimization target (`RuntimeChangeSetPlanner.Plan`).
- [Phase 24.2 planner allocation optimization](baselines/phase-24.2-planner-optimization.md)
  — `RuntimeChangeSetPlanner.Plan` allocation reduction (pre-change
  `f78d2e7`), reference-oracle equivalence strategy, before/after
  benchmark table, and remaining noise.
