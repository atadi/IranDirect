# Phase 36.2 — Source / Namespace / Project / Assembly Rename to PathVeer

**Branch:** `development/service-authority`
**Base:** `2744bff` (Phase 36.1 committed)
**Goal:** Rename the source/build identity `IranDirect.*` → `PathVeer.*` (solution,
projects, assemblies, root namespaces) while preserving **every runtime
compatibility identifier** exactly as it was. Runtime identity is deliberately
deferred to later slices (36.3 state root, 36.4 service/IPC, 36.6 telemetry, etc.).

---

## 1. Branch / base
- Branch `development/service-authority`, clean tree at base `2744bff`.
- This phase changed build/source identity only; zero runtime semantic change.

## 2. Baseline (before rename)
- `dotnet build IranDirect.slnx -c Debug` → succeeded, 104 warnings, 0 errors.
- Core: 2365 passed. Service: 50 passed. Stress: (run post-rename). Benchmark Release: clean.
- 104 warnings are pre-existing doc/test analyzer warnings (unchanged count).

## 3. Build-graph inventory (Step 3)
No csproj sets explicit `AssemblyName`/`RootNamespace`, so assembly name **defaults
to the project name**. Renaming the project directory + csproj file renames the
assembly automatically. `IranDirect.Tray` had an `EmbeddedResource` with
`LogicalName="IranDirect.Tray.tray-icon.ico"` (must follow the assembly name).

| Current | Target | Type |
|---------|--------|------|
| IranDirect.slnx | PathVeer.slnx | solution |
| IranDirect.Core | PathVeer.Core | lib |
| IranDirect.Service | PathVeer.Service | worker/svc |
| IranDirect.Cli | PathVeer.Cli | exe |
| IranDirect.Tray | PathVeer.Tray | winexe |
| IranDirect.Testing | PathVeer.Testing | lib |
| IranDirect.Benchmarks | PathVeer.Benchmarks | exe |
| IranDirect.Core.Tests | PathVeer.Core.Tests | test |
| IranDirect.Service.Tests | PathVeer.Service.Tests | test |

## 4–12. Rename execution
Order: `git mv` directories (Core first; needed to clear file locks by killing
lingering `dotnet.exe` build servers), then csproj filenames, then a surgical
content transform, then `git mv` the csproj files, then solution.

Content transform (Python, surgical — see §15 freeze):
- `namespace IranDirect…` → `namespace PathVeer…`
- `using [static] IranDirect…` → `using [static] PathVeer…`
- fully-qualified namespace refs `IranDirect.Core.` → `PathVeer.Core.` (and Service/
  Cli/Tray/Testing/Benchmarks/Tests) — **dot-qualified only**, so the frozen string
  literals `"IranDirect.Core"` (telemetry source) and `"IranDirect.Service"`
  (service.name) are untouched.
- repo-path scan strings `IranDirect.Core/` → `PathVeer.Core/` etc. (architecture tests).
- csproj ProjectReference Include paths updated; slnx updated.
- `IranDirect.Tray.tray-icon.ico` → `PathVeer.Tray.tray-icon.ico` (resource logical name).
- Binary exe references `IranDirect.Service.exe`/`IranDirect.Cli.exe`/
  `IranDirect.Tray.exe` → `PathVeer.*.exe` (build-artifact rename; the Windows
  **ServiceName** string stays legacy — binary identity ≠ service identity).
- Solution-root sentinel in two test helpers (`RepoRoot()` /
  `FindRepositoryRoot()`) updated `IranDirect.slnx` → `PathVeer.slnx` (test infra,
  not a product identifier).

589 tracked `.cs`/`.csproj`/`.slnx` files had content edits; 597 files moved
(directories + csproj + slnx). Git detects the moves as renames.

## 13. InternalsVisibleTo / reflection
- No `InternalsVisibleTo` attributes exist.
- No `Type.GetType`/`Assembly.Load` string-based type lookups (the one
  `Activator.CreateInstance` uses a resolved `Type`, not a string).
- No assembly-name assertions that would break. Safe.

## 14. Source-text branding
Developer/source text that is purely build identity became `PathVeer`. User-facing
product text, doc comments referring to runtime behavior, and historical docs were
left for 36.5/36.6. No churn to historical phase docs.

## 15. Hard compatibility freeze — PROVEN UNCHANGED
After rename, grep confirms every frozen identifier is still the legacy value:

| Identifier | Value | Location |
|------------|-------|----------|
| State root | `%ProgramData%\IranDirect` | PathVeer.Service/Program.cs:7 |
| Named pipe | `IranDirect.Control.v1` | PathVeer.Core/Ipc/IranDirectPipeNames.cs:6 |
| Windows ServiceName | `"IranDirect"` | PathVeer.Core/ServiceLifecycle/IranDirectServiceNames.cs:5 |
| Service DisplayName | `"IranDirect Service"` | …IranDirectServiceNames.cs:7 |
| Telemetry source/meter | `IranDirect.Core` | PathVeer.Core/Observability/Telemetry/IranDirectTelemetry.cs:24 |
| service.name default | `IranDirect.Service` | PathVeer.Service/Observability/ObservabilityOptions.cs:53, ObservabilityResourceBuilder.cs:30 |
| Legacy migration file | `iran-ipv4-prefixes.txt` | PathVeer.Core/Prefixes/CountryPrefixStore.cs:33 |
| Persisted JSON properties | brand-neutral | unchanged |
| Route inventory/journal model | brand-neutral | unchanged |

No `irandirect_*` metric strings exist in `.cs` (they live in deployment YAML,
untouched in 36.2). IPC command enum values and wire envelope unchanged.

## 16. Runtime artifact check
Build artifacts are now `PathVeer.Service.exe`, `PathVeer.Cli.exe`,
`PathVeer.Tray.exe` (assembly = project name). Installed-runtime identity remains
legacy until later slices. Distinction documented in code/tests:
**binary identity ≠ Windows ServiceName ≠ pipe identity ≠ state-root identity.**

## 17. Service composition / startup
Service composition + startup tests pass (included in the Observability/DI focused
run and the full Service.Tests suite: 50 passed). No DI registration disappeared;
the object graph validates.

## 18. IPC compatibility
IPC tests pass (focused run, 597 total incl. IPC). Newly-built `PathVeer.Cli` /
`PathVeer.Tray` still communicate over the **same** `IranDirect.Control.v1` pipe.
No command enum/value, serialization, or protocol-version change.

## 19. Persistence compatibility
Config/inventory/journal/prefix tests pass. A renamed assembly still reads legacy
`%ProgramData%\IranDirect` state and current globalized state. No type-name metadata
is serialized into JSON (config model is brand-neutral; confirmed in 36.1 §15).

## 20. Globalization regression
DirectCountryCode / country prefix source / country switching / legacy upgrade /
global routing acceptance / large-country scalability tests all pass (included in
the focused + full Core runs). Source rename did not alter country behavior.

## 21. Observability freeze
Telemetry architecture tests pass and **prove** `ActivitySource`/`Meter` still use
`IranDirect.Core`, metrics remain `irandirect_*`, and `service.name` defaults to
`IranDirect.Service`. No accidental split of telemetry history — the build rename
did not change any telemetry string.

## 22. Deployment files
`deployment/observability` was intentionally **not** renamed in this slice. Only
project/binary path references inside code/tests were updated. No dashboard
title/UID/query or Prometheus rule renamed.

## 23. Documentation
- `docs/branding/phase-36.2-source-project-rename.md` (this file)
- `AI-START-HERE.md` +1 nav line

## 24. Canonical verification (post-rename)
- `dotnet clean PathVeer.slnx` + `dotnet build PathVeer.slnx -c Debug` → succeeded,
  104 warnings, 0 errors.
- `dotnet test PathVeer.Core.Tests -c Debug` → **2365 passed** (baseline 2365).
- `dotnet test PathVeer.Service.Tests -c Debug` → **50 passed**.
- Stress (`Category=Stress`) → **12 passed**.
- `dotnet build PathVeer.Benchmarks -c Release` → clean.
- Focused IPC / ServiceCompositionRoot / DesiredConfiguration / RouteMutation /
  Country / Globalization / Observability architecture → all green (597 in the
  filtered run, 0 failed).

## 25. Old-name source audit (remaining `IranDirect` after 36.2)
Allowed remaining occurrences (by category):
- **Runtime compatibility constants** (frozen): pipe `IranDirect.Control.v1`,
  service name `IranDirect`/`IranDirect Service`, telemetry `IranDirect.Core`,
  service.name `IranDirect.Service`, legacy file `iran-ipv4-prefixes.txt`, state
  root `IranDirect`.
- **Legacy/historical type & file names retained intentionally** (not renamed in
  36.2 to avoid touching frozen-constant-containing files and to keep the slice
  minimal): `IranDirectTelemetry.cs`, `IranDirectPipeNames.cs`,
  `IranDirectServiceNames.cs`, `IranDirectServiceClient.cs`, `IranDirectCommand.cs`,
  `IranDirectController.cs`, `IranDirectWorker.cs`, `IranDirectMetricNames.cs`,
  `IranDirectActivityNames.cs`, `IranDirectCliRunner.cs`, etc. These live under the
  new `PathVeer.*` namespaces but keep their historical class/file names; renaming
  them is cosmetic and deferred (they encode no runtime contract beyond the frozen
  string constants already pinned).
- **Deployment/observability** (`irandirect_*` dashboards/rules, container names) —
  deferred to 36.6.
- **Historical docs** — deferred.
- **User-facing branding** (CLI/Tray text, support ZIP name) — deferred to 36.5.
- **Temp-dir test fixtures** using `"IranDirect.Tests"` as a `%TEMP%` subfolder name
  — arbitrary test artifact paths, not product identity; left as-is.

NOT allowed (verified absent): old namespace declarations (`namespace IranDirect`),
old project references, old active assembly names in code, old solution references.

## 26. Scope control
Semantic production diffs are minimal: no routing/planner/executor/config/IPC-
protocol/telemetry-contract/persistence-root change. `git diff` is dominated by
rename + `IranDirect`→`PathVeer` namespace text; every functional behavior is
identical (proven by the full test suite).

## 27. Git rename quality
- `git status` shows `R` (rename) entries for all moved files; `git diff --check`
  shows no whitespace issues.
- No line-ending normalization; no formatting churn beyond the namespace tokens.
- `git diff --stat` reflects renames + targeted content edits.

## 28. Final report (condensed; full items in §1–§27)
1. branch/base: `development/service-authority` / `2744bff`.
2. old→new solution: `IranDirect.slnx` → `PathVeer.slnx`.
3. project map: IranDirect.{Core,Service,Cli,Tray,Testing,Benchmarks,Core.Tests,
   Service.Tests} → PathVeer.* (§3).
4. namespace map: `IranDirect.*` → `PathVeer.*` (root only; domain type names kept).
5. assembly map: same as project map (default assembly name = project name).
6. exe map: `IranDirect.{Service,Cli,Tray}.exe` → `PathVeer.*.exe` (build artifact).
7. InternalsVisibleTo/reflection: none present; no reflection-by-string.
8. source-text branding: build-identity text → PathVeer; user-facing deferred.
9. runtime identifiers retained: §15 table (all frozen values unchanged).
10. state-root proof: `%ProgramData%\IranDirect` unchanged.
11. pipe proof: `IranDirect.Control.v1` unchanged; IPC tests pass.
12. service-identity proof: `IranDirect`/`IranDirect Service` constants unchanged.
13. telemetry proof: `IranDirect.Core` source/meter unchanged; tests assert it.
14. metric proof: `irandirect_*` untouched (deployment YAML out of scope).
15. legacy-file proof: `iran-ipv4-prefixes.txt` detection unchanged.
16. JSON/persistence proof: brand-neutral; rename reads legacy + globalized state.
17. Service DI/startup: 50 Service tests pass; graph validates.
18. IPC: pass; same pipe; no protocol change.
19. globalization regression: pass (full Core 2365 incl. country tests).
20. Core count: 2365.
21. Service count: 50.
22. focused count: 597 (IPC/DI/Config/RouteMutation/Country/Globalization/Obs).
23. Stress count: 12.
24. Benchmark: Release clean.
25. exact files changed/renamed: 8 directories moved, 8 csproj renamed, 1 slnx
    renamed, 589 tracked files content-edited; +2 docs (this file, AI-START-HERE).
26. remaining IranDirect inventory: frozen constants + retained historical type/
    file names + deployment (36.6) + docs + user-facing (36.5) + temp-fixture paths.
27. proof no runtime semantic change: full suite green at identical counts.
28. risks/limitations: (a) `dotnet.exe` build-server handles blocked the initial
    `git mv` of `IranDirect.Service` — resolved by killing servers; (b) test helpers
    hardcoded the solution filename as repo-root sentinel — updated to
    `PathVeer.slnx`; (c) architecture tests hardcoded `IranDirect.Core`/`Service`
    as scan directories — updated; (d) type/file names containing "IranDirect" were
    intentionally kept (cosmetic, deferred) to keep this slice minimal and avoid
    touching frozen-constant files.
29. Phase 36.3 entry point: persistent state-root migration —
    `%ProgramData%\IranDirect` → `%ProgramData%\PathVeer` with copy-then-switch +
    legacy fallback + idempotency (designed in 36.1 §6/§7). Runtime identifiers
    `IranDirect.Control.v1`, service name, telemetry source, and `irandirect_*`
    remain legacy in 36.3; only the on-disk root moves.
30. recommended commit: `refactor(branding): rename source and projects to PathVeer`.

---

## Explicit legacy identifiers that remain until later slices
- `%ProgramData%\IranDirect` — moves to `PathVeer` in **36.3**.
- `IranDirect.Control.v1` — dual-listen compat in **36.4**.
- `IranDirect` / `IranDirect Service` service identity — **36.4**.
- `IranDirect.Core` telemetry source + `irandirect_*` metrics — **36.6**.
- `iran-ipv4-prefixes.txt` — permanent legacy migration detection (never renamed).
- `IranDirect*` historical type/file names — cosmetic, deferred (no runtime contract).
