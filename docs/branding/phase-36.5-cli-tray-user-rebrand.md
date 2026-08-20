# Phase 36.5 — PathVeer CLI / Tray / User-Facing Rebrand

**Branch:** `development/service-authority`
**Base:** `295a425` (Phase 36.4 committed & pushed)
**Date:** 2026-08-08
**Status:** IMPLEMENTED — staged, NOT committed (per user instruction)

---

## 1. Goal

Complete the **user-visible** branding transition from `IranDirect` to `PathVeer`
across the local agent surfaces (CLI, Tray, support bundle) while preserving all
compatibility contracts (wire protocol, IPC, state root, telemetry identity,
planner/executor/journal schema).

Out of scope (frozen for later phases): telemetry source/meter rename (36.6),
`irandirect_*` metric rename (36.6), Grafana/Prometheus/Alertmanager (36.6),
final installer/upgrader (36.7), GitHub repo rename, SaaS/cloud, IPv6,
multi-country routing, planner/executor changes.

---

## 2. Preconditions (verified)

- Clean tree, HEAD `295a425`, branch `development/service-authority` ✓
- `PathVeer.slnx` canonical ✓
- Phase 36.4 committed ✓

---

## 3. User-Facing Inventory (classification)

Active user-facing `IranDirect` strings (Category A) were found in:
- `PathVeer.Cli` — `"IranDirect command failed:"`, `"Usage: IranDirect.Cli ..."`, `"=== IranDirect Diagnostics ==="`
- `PathVeer.Tray` — tooltip `"IranDirect"`, menu/status `"IranDirect — Service"`, `"IranDirect service unavailable"`, `"IranDirect configuration"`, etc. (43 occurrences, all branding)
- `PathVeer.Core/Cli` — `DiagnosticReportCliRenderer` (`"IranDirect Diagnostics"`), `SupportBundlePathBuilder` (`"IranDirect-Support"` prefix + `%TEMP%\IranDirect\SupportBundles`)
- `PathVeer.Tray` — `SaveFileDialogAdapter` (`"Save IranDirect Support Bundle"`), `SupportBundleDefaultFileName` (`"IranDirect-Support-{stamp}.zip"`)

Frozen / non-branding (NOT changed):
- **B. Legacy compatibility literals** — `IranDirect.Control.v1` (`PathVeerPipeNames.LegacyPipeName`), `%ProgramData%\IranDirect` (`StateRootResolver.LegacyStateDirectoryName`), `iran-ipv4-prefixes.txt`.
- **C. Historical/migration** — `LegacyServiceNames.ServiceName = "IranDirect"`, migration messages.
- **D. Telemetry/observability** — `IranDirect.Core` (`SourceName`), `service.name = "IranDirect.Service"`, `irandirect_*` metrics, `IranDirectTelemetry`/`IranDirectTagValues`/`IranDirectMetricNames` (deferred to 36.6).

---

## 4. CLI Branding

- All `"IranDirect command failed:"` → `"PathVeer command failed:"` (`Program.cs`, `CountryCliRunner`, `CustomRouteCliRunner`, `DiagnosticCliRunner`, `ExecutionPreviewCliRunner`, `PrefixUpdateCliRunner`).
- All `"Usage: IranDirect.Cli ..."` → `"Usage: PathVeer.Cli ..."`.
- `"=== IranDirect Diagnostics ==="` → `"=== PathVeer Diagnostics ==="`.
- Command semantics, IPC protocol, command enum values: **unchanged**.

---

## 5. CLI Executable / Alias Decision

`PathVeer.Cli.exe` is already the build artifact (from 36.2). There is **no
installer/alias mechanism** yet, so no `irandirect` compatibility launcher is
introduced in 36.5. The `irandirect` → `pathveer` alias and upgrade launcher are
explicitly deferred to Phase 36.7 (final installer/upgrader). Documented, not
implemented.

---

## 6. CLI Country UX

`CountryCliRunner` usage strings updated to `PathVeer.Cli country ...`.
`DirectCountryCode` semantics, validation, refresh/offline/fail-closed behavior:
**unchanged** (36.5 is presentation-only).

---

## 7. Tray Branding

All 43 user-facing `"IranDirect"` string literals in `TrayApplicationContext.cs`
→ `"PathVeer"` (tooltip, NotifyIcon text, context menu, status dialog,
configuration/country/custom-routes/runtime-snapshot/execution-preview/diagnostics/
support-bundle labels, service-unavailable text). `SaveFileDialogAdapter` title
and filter → `PathVeer`. `DiagnosticReportDialog`/`CustomRouteDialog` etc.
unchanged except via the shared `"IranDirect"`→`"PathVeer"` sweep. UX structure
preserved (no redesign).

---

## 8. Tray Process / Startup

`PathVeer.Tray.exe` is the artifact (from 36.2). No registry/startup keys
referencing `IranDirect` were found in source (startup registration, if any, is
an installer concern → deferred to 36.7). Documented.

---

## 9. Support Bundle Branding

- `SupportBundleDefaultFileName` → `"PathVeer-Support-{stamp}.zip"`.
- `SupportBundlePathBuilder.DefaultFilePrefix` → `"PathVeer-Support"`.
- Temp export dir `%TEMP%\IranDirect\SupportBundles` → `%TEMP%\PathVeer\SupportBundles` (new exports only; historical bundles on disk untouched).
- `DiagnosticReportCliRenderer` header → `"PathVeer Diagnostics"`.
- Serialized support-bundle schema (JSON field names): **unchanged** — only presentation/metadata branding changed.

---

## 10–12. Source-Level Type Renames (wire-safe)

Three misleading primary-source types renamed (whole-token, word-boundary so
telemetry types like `IranDirectTelemetry` are untouched):

| Old | New | Wire impact |
|---|---|---|
| `IranDirectServiceClient` | `PathVeerServiceClient` | Client class only; no wire payload. |
| `IranDirectCommand` | `PathVeerCommand` | Enum serialized via `JsonStringEnumConverter` → **member names** (`"Status"`, `"Enable"`…) are the wire values; the type *name* is not serialized. Wire contract preserved. |
| `IranDirectStatus` | `PathVeerStatus` | Record type; wire uses member names, not the type name. Preserved. |

`IranDirectJson` was intentionally **left** (neutral serialization helper, not in
the rename list, large churn, not user-facing). `IranDirectController`,
`IranDirectDiagnostics`, `IranDirectState`, `IranDirectWorker`,
`IranDirectRuntimeObservationSource`, `IIranDirectStatusProvider`,
`IranDirectJson` remain as historical/domain type names (brand-neutral in
behavior; §33 — do not churn domain types).

---

## 13–15. Wire / Legacy Compatibility

- `IranDirectCommand` → `PathVeerCommand` rename verified wire-safe by
  `JsonStringEnumConverter` (member-name serialization).
- Legacy literals retained: `IranDirect.Control.v1` (as `LegacyPipeName`),
  `%ProgramData%\IranDirect` (`LegacyStateDirectoryName`), `iran-ipv4-prefixes.txt`.
- Explicit `LegacyServiceNames` type keeps `ServiceName = "IranDirect"` for the
  migration contract.

---

## 16. Service Display Result

Tray status text now reads `PathVeer Service` / `PathVeer — Enabled` /
`PathVeer — Service unavailable` etc. The actual service identity was already
`PathVeer` from 36.4.

---

## 17. Country UX Result

Country UX presents as PathVeer (e.g. `PathVeer country set IQ`). No
Iran-specific wording in active UX. Legacy `IR` default/migration remains an
implementation detail.

---

## 18–19. Error / Logging Text

User-visible error/diagnostic strings (`"IranDirect command failed:"`,
`"IranDirect service unavailable"`, `"IranDirect diagnostics"`) → `PathVeer`.
Error codes, diagnostic IDs, IPC failure codes, telemetry values: **unchanged**.
Structured log property names: **unchanged**. Historical migration messages that
must say `IranDirect` (e.g. "Migrating legacy IranDirect state") remain correct.

---

## 20. Active User Docs

`AI-START-HERE.md` nav updated with a 36.5 entry. No historical phase docs
rewritten. This doc (§34) added.

---

## 21. pathveer.com

No `pathveer.com` reference added. The user owns the domain but there is no
existing website field/link in the active surfaces, and SaaS/cloud dependencies
are explicitly out of scope. Deferred.

---

## 22–23. Icon / Resource / File-Version Metadata

- No `.ico`/`.resx` files contained `IranDirect` (none found) — icon is
  asset-neutral; no new logo generated (per spec, no visual branding work).
- csproj files carry **no** explicit `Product`/`Title`/`Description` metadata,
  so there is nothing to change; assemblies already named `PathVeer.*` (36.2).
- AssemblyTitle/FileDescription remain framework defaults. No new metadata
  introduced.

---

## 24. User-Facing File Names

New support ZIPs use `PathVeer-Support-*.zip`; temp dir `%TEMP%\PathVeer\SupportBundles`.
Persisted **state** filenames (schema/storage contracts) unchanged (36.1/36.3
own those).

---

## 25–27. Regression Suites

- IPC mixed-version matrix (old client→new service, new→new, new→old fallback,
  ambiguous send no-replay): covered by 36.4 `ServiceIpcMigrationTests` — still
  green.
- State-root (36.3) tests: green.
- Globalization / Country / State / Coordinator / Recovery / Journal: 245 green
  (re-run this session).

---

## 28–30. Freeze Gates (PROOF)

- **Telemetry** (36.6): `IranDirectTelemetry.SourceName == "IranDirect.Core"`,
  `service.name == "IranDirect.Service"`, `irandirect_*` metrics — all **unchanged** (asserted by `BrandingTests.TelemetryIdentityFrozen` + existing `TelemetryArchitectureTests`).
- **Deployment** (36.6): no Grafana/Prometheus/Alertmanager changes.
- **Installer** (36.7): no final upgrade installer built; only the
  `Install-PathVeerService.ps1` helper (from 36.4) updated to stop legacy.

---

## 31. Remaining IranDirect Inventory (active source)

After 36.5, active production source still contains `IranDirect` only as:
- **B. Legacy compat literals** — `IranDirect.Control.v1`, `%ProgramData%\IranDirect`, `iran-ipv4-prefixes.txt` (approved).
- **C. Historical/migration** — `LegacyServiceNames.ServiceName`, migration log text.
- **D. Telemetry (frozen for 36.6)** — `IranDirectTelemetry`, `IranDirectTagValues`, `IranDirectMetricNames`, `IranDirectActivityNames`, `IranDirectTagNames`, `IranDirectTagValues`, `IranDirectCore` references in observability.
- **Historical domain/diagnostics type names (not user-facing, not in rename list)** — `IranDirectController`, `IranDirectDiagnostics`, `IranDirectDiagnosticsService`, `IranDirectState`, `IranDirectRuntimeObservationSource`, `IranDirectJson`, `IranDirectWorker`, `IIranDirectStatusProvider`, `IranDirectStatus` (provider interface name).

There is **NO unexplained active user-facing `IranDirect` branding** in CLI or
Tray — proven by `BrandingTests.Cli_SourceHasNoUserFacingIranDirectString` and
`BrandingTests.Tray_SourceHasNoUserFacingIranDirectString` (source-scan
architecture tests that fail if any quoted `"IranDirect` string reappears).

---

## 32. Permanent Tests Added

`PathVeer.Core.Tests/Branding/BrandingTests.cs` (13 tests):
- CLI source scan: no user-facing `"IranDirect"`, no `Usage: IranDirect.Cli`, no `IranDirect command failed`.
- CLI diagnostics heading = `PathVeer Diagnostics`; support prefix = `PathVeer-Support`.
- Tray source scan: no user-facing `"IranDirect"`; support filename = `PathVeer-Support-*.zip`; Save dialog title = `PathVeer Support Bundle`.
- `PathVeerServiceClient` type present; legacy pipe literal `IranDirect.Control.v1` + primary `PathVeer.Control.v1` preserved; legacy state root `IranDirect` + current `PathVeer`; telemetry `IranDirect.Core` frozen; new service identity `PathVeer`/`PathVeer Service` + `LegacyServiceNames.ServiceName = "IranDirect"`.

Plus updated branding assertions in existing tests
(`SupportBundleDefaultFileNameTests`, `DiagnosticCliRunnerTests`,
`DiagnosticReportCliRendererTests`, `SupportBundleCliRunnerTests`,
`IranDirectServiceClientConsumerTests`, `ObservabilityConsumerTests`,
`TelemetryArchitectureTests` method rename).

---

## 33. No Cosmetic Domain Churn

Domain-neutral types (`RuntimeChangeSetPlanner`, `RouteMutationJournal`,
`CountryPrefixProvider`, `DirectCountryCode`, `ManagedRoute`, `RuntimeExecutor`,
`IranDirectController`, `IranDirectState`, `IranDirectDiagnostics`) were **not**
renamed — goal is to remove brand coupling, not scatter `PathVeer` prefixes.

---

## 34. Documentation

- NEW `docs/branding/phase-36.5-cli-tray-user-rebrand.md` (this file)
- MOD `AI-START-HERE.md` — +1 navigation entry

---

## 35. Verification (fresh, this session)

| Suite | Result |
|---|---|
| Full solution build (`PathVeer.slnx` Debug) | 0 warnings, 0 errors |
| Focused (CLI/Tray/Support/IPC/StateRoot/Country/Globalization/Branding) | 717 passed |
| Full `PathVeer.Core.Tests` | 2418 passed, 1 failed |
| `PathVeer.Service.Tests` | 50 passed |
| Stress (`Category=Stress`) | 12 passed |
| Benchmark (`PathVeer.Benchmarks` Release) | clean, 0 errors |
| Isolated re-run of the 1 failed Core test | passed (environmental file-lock flake) |

The single Core failure (`PersistenceEnduranceConcurrencyTests.CrossInstance_ReadersAndSingleWriter_NoMalformedReads`) is the **same environmental file-lock flake** observed across 36.2/36.3/36.4; it re-passes in isolation and is not a branding defect.

---

## 36. Scope Proof

`git diff --stat` / `git diff --check` confirm changes are confined to:
user-facing CLI/Tray/support strings, 3 wire-safe source-type renames, the
support-bundle path/prefix, test assertions, the Branding test file, and docs.
No changes to: planner, executor semantics, journal schema, country prefix
semantics, state-root behavior, telemetry names, observability deployment,
SaaS/cloud. Repository path unchanged (`C:\codespace\irandirect`).

---

## 37. Risks / Limitations

1. **`irandirect` CLI alias not created** — users invoke `PathVeer.Cli.exe`
   directly until 36.7 provides the alias/launcher.
2. **Tray auto-run/shortcut** — if a shortcut or registry Run key references the
   old binary by display name, it is an installer concern deferred to 36.7.
3. **Telemetry still `IranDirect.Core`** — intentional temporary mismatch until
   36.6; monitoring dashboards keep working on the legacy identity.
4. **`AddIranDirectObservability`** (DI extension in `PathVeer.Service`) intentionally
   left as the observability entry point; it moves with the observability stack in
   36.6. All other active application/domain/service types are now `PathVeer*`.

---

## 37b. Addendum — Final Active-Source Cleanup (pre-commit)

After the initial 36.5 implementation, a full-filesystem audit confirmed several
ACTIVE current source type/file names still carried `IranDirect` with no
compatibility contract. These were renamed (wire/telemetry-safe; no serialization
by type name) and their files `git mv`'d:

| Old | New |
|---|---|
| `IranDirectController` | `PathVeerController` |
| `IranDirectWorker` | `PathVeerWorker` |
| `IranDirectState` | `PathVeerState` |
| `IranDirectStatus` (filenames) | `PathVeerStatus` (type already renamed in 36.5) |
| `IIranDirectStatusProvider` | `IPathVeerStatusProvider` |
| `IranDirectDiagnostics` | `PathVeerDiagnostics` |
| `IranDirectDiagnosticsService` | `PathVeerDiagnosticsService` |
| `IranDirectRuntimeObservationSource` | `PathVeerRuntimeObservationSource` |
| `IranDirectJson` | `PathVeerJson` |
| `IranDirectControllerTests` | `PathVeerControllerTests` |
| `IranDirectWorkerTests` | `PathVeerWorkerTests` |
| `AddIranDirectServiceComposition` (DI method) | `AddPathVeerServiceComposition` |

**Explicitly NOT touched** (per scope freeze):
- Frozen telemetry types: `IranDirectTelemetry`, `IranDirectMetricNames`,
  `IranDirectActivityNames`, `IranDirectTagNames`, `IranDirectTagValues`
  (move together with telemetry contracts in 36.6).
- `AddIranDirectObservability` — observability entry point, deferred to 36.6.
- Legacy compatibility literals: `IranDirect.Control.v1`,
  `%ProgramData%\IranDirect`, `iran-ipv4-prefixes.txt`,
  `LegacyServiceNames.ServiceName = "IranDirect"`.
- Historical migration identifiers and docs.

**`.csproj.user` finding:** `PathVeer.Tray/IranDirect.Tray.csproj.user` exists on
disk but is **NOT tracked** by git (absent from `git ls-files`/`git status`) — it
is a local-only build artifact, not a product compatibility identifier. Per repo
policy it is excluded from the phase and left untouched.

**Post-cleanup source audit (excl bin/obj/.vs/BenchmarkDotNet.Artifacts/docs):**
remaining `IranDirect` tokens fall ONLY into the allowed categories —
frozen telemetry (`IranDirectTelemetry`/`*Tag*`/`*Metric*`/`*ActivityNames`) and
observability (`AddIranDirectObservability`) for 36.6, plus legacy compat literals
(`IranDirect.Control.v1`, `%ProgramData%\IranDirect`, `LegacyServiceNames`) and
telemetry identity (`IranDirect.Core`/`IranDirect.Service`). No current
application/domain/service type remains `IranDirect` for cosmetic reasons.



Phase 36.6 owns **telemetry identity migration**: `IranDirect.Core` →
`PathVeer.Core` source/meter name, `service.name = "IranDirect.Service"` →
`"PathVeer.Service"`, and the `irandirect_*` metric names → `pathveer_*`, plus
the corresponding Grafana/Prometheus/Alertmanager renames and the
`IranDirectTelemetry`/`IranDirectTagValues`/`IranDirectMetricNames` type
renames. All currently frozen literals become the 36.6 work item.

---

## 39. Recommended Commit Message

```
feat(branding): rebrand CLI and Tray as PathVeer
```
