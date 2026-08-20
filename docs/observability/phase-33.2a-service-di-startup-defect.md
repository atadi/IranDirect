# Phase 33.2A — Service dependency-injection startup defect

Startup-defect fix. No telemetry contract, observability stack, or runtime
behavior change.

## Symptom

`IranDirect.Service.exe` could not start. `HostApplicationBuilder.Build()`
threw during ServiceProvider validation, before the host reached started state:

```
System.InvalidOperationException: Unable to resolve service for type
'IranDirect.Core.Configuration.IDesiredConfigurationService' while attempting
to activate 'IranDirect.Core.Support.SupportSnapshotProvider'.
```

The failure was independent of observability: it reproduced with
`Observability:Enabled=false` and with no observability environment variables
set at all.

## Root cause

`Program.cs` registered several Core types by their **concrete** type only,
while consumers depend on the **interface**. Four interface mappings were
absent from the composition root:

| Interface | Implementation | Consumer that failed to activate |
|-----------|----------------|----------------------------------|
| `IDesiredConfigurationService` | `DesiredConfigurationService` | `SupportSnapshotProvider` |
| `ICustomRouteDnsCacheService` | `CustomRouteDnsCacheService` | `SupportSnapshotProvider` |
| `ISupportSnapshotProvider` | `SupportSnapshotProvider` | `SupportSnapshotExporter` |
| `ISupportSnapshotUtf8Serializer` | `SupportSnapshotSerializer` | `SupportSnapshotExporter` |

`IDesiredConfigurationService` was the first to fail and therefore the only one
named in the exception; the other three were latent behind it.

`git log -S "IDesiredConfigurationService" -- IranDirect.Service/Program.cs`
returns nothing, so the registration was never present rather than accidentally
deleted — the interface dependency was introduced in `SupportSnapshotProvider`
without a corresponding composition-root entry. The previous
`ISupportBundleExporter` factory sidestepped the gap by newing up
`SupportSnapshotExporter` by hand, which is why the defect stayed hidden until
`SupportSnapshotProvider` itself was resolved.

## Fix

Each interface is registered as a **singleton that forwards to the existing
concrete singleton**:

```csharp
services.AddSingleton<IDesiredConfigurationService>(
    sp => sp.GetRequiredService<DesiredConfigurationService>());
```

Lifetime evidence — singleton is correct and forwarding is required:

- Every one of the four implementations is already registered
  `AddSingleton` for its concrete type, so a different lifetime for the
  interface would contradict the existing registration.
- All four are stateful over a single backing file or cache
  (`DesiredConfigurationStore`, `CustomRouteDnsCacheStore`, the snapshot
  provider's cached dependencies). Registering the implementation type a
  second time (`AddSingleton<IFoo, Foo>()`) would create a **second instance**
  writing the same file.
- The forwarding form guarantees one object per implementation, which the
  composition tests assert with `Assert.Same`.

No implementation was changed, no dependency was made optional, no null or
fallback implementation was introduced, and ServiceProvider validation remains
enabled.

## Composition root extraction

`Program.cs` used top-level statements, so no test could construct the real
graph — which is precisely why the defect shipped. The application graph moved
verbatim into `ServiceCompositionRoot.AddIranDirectServiceComposition`, which
`Program.cs` now calls. Host-only concerns (Windows Service lifetime, telemetry
export hosting) stay in `Program.cs`.

This makes the defect class structurally untestable-no-more: the composition
tests build the exact graph the shipped host builds.

## Tests

`IranDirect.Service.Tests/ServiceCompositionRootTests.cs` — 9 tests building the
real graph with `ValidateOnBuild` and `ValidateScopes` enabled:

- composition root builds and validates;
- `IDesiredConfigurationService` resolves and is a `DesiredConfigurationService`;
- each of the four interfaces resolves to the *same* instance as its concrete
  registration (`Assert.Same`), guarding against duplicate singletons;
- `SupportSnapshotProvider` activates with all dependencies (the exact
  activation that failed);
- `ISupportBundleExporter` resolves;
- the host starts and stops cleanly with observability disabled.

Verified RED before the fix: with the four registrations removed, all 9 fail
and the pre-existing 35 Service tests still pass. GREEN after: 44/44.

## Related test update

`TelemetryArchitectureTests.RouteTelemetry_WrapperIsOnlyBridge_NoOpenTelemetry`
asserted that `Program.cs` contains `TelemetryRouteApi`. That wiring moved to
`ServiceCompositionRoot.cs`. The test now asserts the wrapper is wired in the
composition root and that **both** files stay free of OpenTelemetry references —
same intent, follows the code, not weakened.

## Verification

- `dotnet clean` + `dotnet build IranDirect.slnx -c Debug` → 0 warnings, 0 errors
- `dotnet test IranDirect.slnx -c Debug` → 2208 passed, 0 failed
  (Core 2164, Service 44)
- Stress (`--filter "Category=Stress"`) → 12 passed
- `dotnet build IranDirect.Benchmarks -c Release` → 0 warnings, 0 errors
- Real `IranDirect.Service.exe`, telemetry disabled → host built, DI validated,
  "Application started", runtime cycles completed, graceful Ctrl+C shutdown, rc=0
- Real `IranDirect.Service.exe`, telemetry enabled against the Phase 33.2 stack
  → 25 `irandirect_*` metric series in Prometheus and 3 `IranDirect.RuntimeCycle`
  traces in Tempo, matching the 3 logged cycles
- Collector stopped → Service still started, ran cycles, shut down cleanly (rc=0)

## Known unrelated issue

A separate long-running `IranDirect.Service.exe` (session 0, installed since
2026-08-02) owns the named pipe, so a second interactive instance logs
`IOException: All pipe instances are busy` from `NamedPipeCommandServer`. This
is pre-existing single-instance behavior, unrelated to this fix, and does not
prevent startup, reconciliation, or shutdown.
