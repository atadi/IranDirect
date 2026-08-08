# Phase 36.1 — PathVeer Brand Rename & Compatibility Audit

**Branch:** `development/service-authority`
**Base:** `f3418d7` (Phase 35.7A committed)
**Type:** ANALYSIS + DOCUMENTATION ONLY. No production code, project, namespace,
path, service, pipe, telemetry, or installer changes in this phase.
**Product family:** IranDirect → **PathVeer** (user owns `pathveer.com`).

This document is the complete rename/migration plan so later implementation slices
(36.2–36.8) can rename IranDirect to PathVeer **without breaking** installed
agents, the Windows Service, IPC, persisted state, route ownership, journals,
configuration, upgrades, telemetry/observability, CLI/Tray, or support tooling.

---

## 1. Branch / base
- Branch `development/service-authority`, clean tree at base `f3418d7`.
- No source/config/deployment changes in this phase; only two doc files.

## 2. Brand terminology mapping
| Future concept | Current component |
|----------------|-------------------|
| PathVeer | IranDirect (Core domain + product) |
| PathVeer Agent | IranDirect.Service (Windows Service host) |
| PathVeer Service | IranDirect.Service + IranDirectWorker |
| PathVeer CLI | IranDirect.Cli (`irandirect` exe) |
| PathVeer Tray | IranDirect.Tray |
| PathVeer Cloud (future) | — (not built; DNS reserved only) |
| PathVeer Console (future SaaS UI) | — (not built; DNS reserved only) |

Names are **documented here only**; not implemented.

## 3. Complete identifier inventory (case-insensitive `IranDirect`)

Aggregate textual occurrences across tracked files (`.cs`, `.csproj`, `.slnx`,
`.json`, `.md`, `.yml`, `.yaml`, `.ps1`): **4,944 line matches** in **700 files**.
This dominates by namespaces/comments (category A). The operationally significant
anchors (categories B–J) are far smaller and enumerated below.

Exact external-identity anchors located in source:
- **Windows Service name/display/description:** `IranDirect.Service/Program.cs:16`
  `options.ServiceName = "IranDirect Service"`; `IranDirect.Core/ServiceLifecycle/
  IranDirectServiceNames.cs` → `ServiceName="IranDirect"`,
  `DisplayName="IranDirect Service"`, `Description="Routes Iranian IPv4 prefixes
  directly through the ISP gateway while protecting VPN endpoint connectivity."`
  (the Description itself embeds a *country assumption* that must be rewritten for
  the globalized product).
- **Named pipe:** `IranDirect.Core/Ipc/IranDirectPipeNames.cs` →
  `Control = "IranDirect.Control.v1"`.
- **State root:** `IranDirect.Service/Program.cs:4-7` →
  `%ProgramData%\IranDirect` (`Environment.SpecialFolder.CommonApplicationData`
  + `"IranDirect"`).
- **Telemetry source/meter:** `IranDirect.Core/Observability/Telemetry/
  IranDirectTelemetry.cs` → `SourceName = "IranDirect.Core"` (used as both
  `ActivitySource` and `Meter` name).
- **service.name:** `IranDirect.Service/Observability/ObservabilityResourceBuilder
  .cs:30` and `ObservabilityOptions.cs:53` default `"IranDirect.Service"`.
- **IPC command enum (wire contract):** `IranDirect.Core/Ipc/IranDirectCommand.cs`
  (261 occurrences across the codebase).
- **Support bundle filename:** `IranDirect.Tray/SupportBundleDefaultFileName.cs:59`
  → `IranDirect-Support-{yyyyMMdd-HHmmss}.zip`.
- **Project/assembly names:** `IranDirect.Core`, `IranDirect.Service`,
  `IranDirect.Cli`, `IranDirect.Tray`, `IranDirect.Testing`, `IranDirect.Benchmarks`
  (no explicit `AssemblyName`/`RootNamespace` in csproj → default to project name).
- **Deployment/observability (`irandirect_*`, 8 files):** grafana dashboards
  `irandirect-ipc-support.json`, `irandirect-prefix-dns.json`,
  `irandirect-reliability-errors.json`, `irandirect-runtime-reconciliation.json`,
  `irandirect-service-overview.json`; prometheus rules `irandirect-alert-rules.yml`,
  `irandirect-recording-rules.yml`, `irandirect-host-alert-rules.yml`
  (production). Docker container_names `irandirect-otel-collector`,
  `irandirect-prometheus`, `irandirect-tempo`, `irandirect-alertmanager`,
  `irandirect-grafana`.
- **Prometheus metric series (`irandirect_*`):** e.g.
  `irandirect_runtime_cycles_completed_total`, `irandirect_ipc_requests_total`,
  `irandirect_routes_operations_requested_total`, `irandirect_prefix_checks_total`,
  `irandirect_dns_lookups_total`, `irandirect_support_bundles_exported_total`, etc.
- **Docs/repo:** 80 markdown files reference `IranDirect` (including this audit
  lineage, AI-START-HERE, and prior globalization docs).
- **Env vars:** no `IRANDIRECT_*` variables found in source/deployment configs.

## 4. Classification (A–J)

| Cat | Meaning | Volume / examples |
|-----|---------|-------------------|
| A | Pure source branding (namespaces, comments) | ~4,900 lines (581 .cs + others); safe direct rename |
| B | Build identity (project/assembly/exe/solution) | 6 projects + 1 .slnx; rename with care (binaries/shortcuts) |
| C | Windows external identity (ServiceName, pipe) | `IranDirect` service, `IranDirect.Control.v1` pipe — MIGRATION |
| D | Persisted path identity (ProgramData root) | `%ProgramData%\IranDirect` — MIGRATION |
| E | Wire/API compatibility (IPC enum, pipe) | `IranDirectCommand` enum, `IranDirect.Control.v1` — versioned compat |
| F | Telemetry contract | `IranDirect.Core` source/meter, `IranDirect.Service` service.name, `irandirect_*` metrics — BREAKING if renamed; plan needed |
| G | Deployment/operations | compose, collector, prometheus, grafana, alertmanager, runbooks — planned rename, history consideration |
| H | User-facing branding | CLI errors ("IranDirect command failed"), Tray, support bundle `IranDirect-Support-*.zip`, diagnostics "IranDirect Diagnostics" — direct rename + compat alias |
| I | Legacy/historical | `iran-ipv4-prefixes.txt` migration id, legacy tests, historical docs — RETAIN |
| J | Repository/domain | GitHub `IranDirect`, docs links, `pathveer.com` reserved — documented, not renamed here |

## 5. Safe renames vs migrations
- **SAFE DIRECT RENAME:** category A (namespaces/comments), category H text
  ("IranDirect command failed" → "PathVeer command failed"), category B project
  names (with install/shortcut follow-through), support bundle filename text.
- **REQUIRES COMPATIBILITY MIGRATION:** C (service + pipe), D (state root),
  E (IPC wire enum/pipe — keep legacy alias during transition), F (telemetry —
  see §13), G (dashboards/rules — recording-rule aliases or history split).
- **MUST RETAIN LEGACY ALIAS:** `IranDirect.Control.v1` pipe (client fallback),
  `iran-ipv4-prefixes.txt` legacy migration detection, `IranDirect` Windows
  service SID during upgrade window, `irandirect_*` metric names until dashboards
  cut over (or via recording-rule aliases).
- **DEFER:** GitHub repo rename (§25), SaaS/cloud DNS (§26), installer if absent
  (§24).

## 6. Persistent state root audit (Step 6)
Current root: `%ProgramData%\IranDirect` (created in `Program.cs`).
Under it (established in 35.3/35.6/35.7):
- `config.json` (DesiredConfiguration; brand-neutral JSON — §15)
- `prefixes/{ISO2}/ipv4-prefixes.txt` + `metadata.json` + `update-history.json`
- `iran-ipv4-prefixes.txt` (legacy IR migration source; RETAIN detection)
- `RouteInventory`, `VpnEndpointInventory`, `RouteMutationJournal`
- custom-route cache/state, runtime state, support artifacts

Future PathVeer root (use repository convention): **`%ProgramData%\PathVeer`**.
Migration behavior:
```
if PathVeer root exists        -> use it
else if legacy IranDirect root -> migrate safely (copy), retain legacy
else                           -> fresh create
```
Legacy source retained through at least the first successful PathVeer startup.
Migration idempotent (re-run copies only missing files; never deletes source
until PathVeer startup verified).

## 7. Data-root migration strategy (Step 7)
**Selected: legacy read fallback + atomic/copy migration, legacy retained.**
- COPY (not MOVE) the legacy root contents into the PathVeer root on first
  PathVeer start; PathVeer then reads/writes only the PathVeer root.
- Old IranDirect service is stopped and removed BEFORE PathVeer starts (§9), so no
  process writes the legacy root after cutover.
- Rollback (§27): if PathVeer startup fails post-migration, the legacy
  `%ProgramData%\IranDirect` is still intact (copy, not move) → restore old
  service binary + legacy root remains valid.
- Prefer COPY over MOVE and over DUAL-READ/NEW-WRITE: DUAL-READ risks two
  authorities; MOVE loses the rollback safety net.

## 8. Windows Service identity (Step 8)
Current: `ServiceName="IranDirect Service"` (Program.cs) and
`IranDirectServiceNames.ServiceName="IranDirect"` (used by
`WindowsServiceControllerAdapter`/`WindowsServiceLifecycle` to start/stop/query).
Changing to `PathVeer Service` / `PathVeer`:
- Windows service names **cannot be renamed in place**; the installer must
  **stop + remove the old service, then create the new one**.
- Risk: if old is not fully removed, **two services could run and both claim route
  authority** — unacceptable (§9).
- The named pipe is owned by whichever service is running; single-authority is
  enforced by "old removed before new starts" + pipe exclusivity.
- Upgrade sequence (designed, not implemented): stop old → confirm stopped →
  migrate state root (copy) → remove old service → install+start PathVeer →
  recover journal/state → reconcile.

## 9. Single-authority safety during upgrade (Step 9)
Strict sequence (each step blocks the next):
1. Stop old `IranDirect` service; await exit.
2. Preserve state (legacy root intact; PathVeer root copied).
3. Migrate/install `PathVeer` service (binary at new path).
4. **Remove/disable old `IranDirect` service** so it cannot auto-start.
5. Start `PathVeer`; open `PathVeer.Control.v1` pipe (or dual-listen legacy pipe
   during compat window — §10).
6. Recover RouteMutationJournal + state.
7. Reconcile (fail-closed; no empty destructive plan — proven in 35.4/35.7).
Rollback mirrors this in reverse and also avoids concurrent authorities.

## 10. Named-pipe compatibility (Step 10)
Current: `IranDirect.Control.v1`.
Options evaluated:
- (A) PathVeer temporarily listens on BOTH `PathVeer.Control.v1` and
  `IranDirect.Control.v1` — simplest mixed-version support; small surface;
  recommended.
- (B) New clients fall back to legacy pipe — pushes compat burden to clients.
- (C) Atomic client+service upgrade — ideal but assumes single managed installer
  (§24: none today).
- (D) Retain old pipe name for v1 — lowest risk but leaves brand in the wire.
**Recommended: (A)** dual-listen during a compatibility window, with clients
preferring `PathVeer.Control.v1` and falling back to `IranDirect.Control.v1`. This
matches the mixed-version IPC compatibility already proven in 35.6. Security: both
pipes share the same OS ACL model; no widening.

## 11. Namespace/project rename graph (Step 11)
| Current | Future (proposed) |
|---------|-------------------|
| IranDirect.Core | PathVeer.Core |
| IranDirect.Service | PathVeer.Service |
| IranDirect.Cli | PathVeer.Cli |
| IranDirect.Tray | PathVeer.Tray |
| IranDirect.Testing | PathVeer.Testing |
| IranDirect.Benchmarks | PathVeer.Benchmarks |
| IranDirect.Core.Tests / .Service.Tests | PathVeer.*.Tests |
| IranDirect.slnx | PathVeer.slnx |

Sequence to minimize broken references (36.2): rename leaf/test projects first,
then core, then solution, using an IDE/tool-assisted rename so `using` directives
and project references stay consistent. Do NOT do one uncontrolled filesystem move.

## 12. Assembly/executable compatibility (Step 12)
Outputs: `IranDirect.Service.exe`, `IranDirect.Cli.exe` (CLI `irandirect`),
`IranDirect.Tray.exe`. Impact: service binary path (ServiceName install), CLI
shortcuts, Tray startup (shell:startup / scheduled), support scripts, any firewall
Defender exclusions referencing the old exe path. Migration: new install lays down
`PathVeer.*.exe` at new paths; old exes removed with old service. Keep a `irandirect
→ pathveer` CLI **compat alias** (launcher) during a transition window (§18).

## 13. Telemetry rename strategy (Step 13)
Operational contract is mature (35.3 observability hardening). Recommendation:
**Option B — keep legacy metric names temporarily while `service.name` becomes
`PathVeer.Service`.**
- `service.name` = `PathVeer.Service` (cosmetic, no dashboard break).
- `IranDirect.Core` ActivitySource/Meter name + `irandirect_*` series:
  **retain** for v1 to avoid splitting Prometheus/Tempo history and breaking
  recording rules/alerts. Plan the rename as a later dedicated slice with
  recording-rule aliases (`irandirect_* = pathveer_*`) so history stays continuous.
- Avoid dual-publish (Option C) unless a hard cutover is forced; it doubles series
  and storage.
- Do NOT casually break the `irandirect_*` contract (§14).

## 14. Dashboard/alert migration (Step 14)
Every `irandirect_*` query/rule/dashboard reference mapped in §3. Changing metric
names **splits historical Prometheus/Tempo data**. Acceptable approach at rebrand:
keep `irandirect_*` series (§13 Option B) and, if a rename is later desired, add
Prometheus **recording-rule aliases** (`irandirect_runtime_cycles_completed_total`
← `pathveer_runtime_cycles_completed_total`) so old dashboards/alerts keep working
and history is continuous. No implementation in 36.1.

## 15. Configuration JSON contract (Step 15)
Persisted `config.json` (DesiredConfiguration) is **brand-neutral**: properties are
`Enabled`, `ProfilePath`, `DirectCountryCode`, etc. — no `IranDirect` string in the
JSON model. **Do NOT rename JSON properties when namespaces change.** Wire/persisted
compatibility preserved.

## 16. Route ownership compatibility (Step 16)
`ManagedRoute`, `RouteInventory`, `VpnEndpointInventory`, `RouteMutationJournal`
encode **no IranDirect brand** in identity/ownership fields (only `using
IranDirect.Core.*` namespace references, confirmed in 35.6/35.7). PathVeer inherits
IranDirect's ownership proof exactly. **No route reset during rename** — the rename
touches paths/service/telemetry, not route semantics.

## 17. Country-prefix compatibility (Step 17)
Layout `prefixes/{ISO2}/...` is brand-neutral and survives the rename unchanged
inside the migrated root. `iran-ipv4-prefixes.txt` is a **legacy migration
identifier** — classified LEGACY (§5), MUST be retained so old upgrades still
detect/migrate IR prefixes. Do not rename historical migration detection strings.

## 18. CLI rename impact (Step 18)
Executable `IranDirect.Cli` → `PathVeer.Cli`; user command `irandirect` → `pathveer`.
Keep a temporary `irandirect` → `pathveer` **compat launcher/alias** during the
transition (low cost, preserves muscle memory + scripts). Help text "IranDirect
command failed" → "PathVeer command failed". Not implemented here.

## 19. Tray rename impact (Step 19)
Window title/tooltip/menu text "IranDirect …", process name `IranDirect.Tray.exe`,
startup shortcut, support-bundle filename `IranDirect-Support-*.zip`. Desired:
PathVeer equivalents; **do not redesign the Tray UX**. Filename builder
(`SupportBundleDefaultFileName.Build`) text changes from `IranDirect-Support-` to
`PathVeer-Support-`.

## 20. Support bundle rename (Step 20)
Filename `IranDirect-Support-{ts}.zip` → `PathVeer-Support-{ts}.zip`. JSON metadata
and diagnostics text should clearly identify PathVeer after migration. Retain
historical values only where needed for cross-version bundle parsing (none today —
bundle schema is brand-neutral).

## 21. Logs / EventLog (Step 21)
No dedicated Windows EventLog source string found (`AddWindowsService` uses the
ServiceName for event log registration). Changing the service name may require
re-registering the event source (admin rights). Migration: old source retained until
old service removed; new service registers its own. Do not silently lose logs — keep
legacy log files in the legacy root (read-only fallback) during transition.

## 22. Environment variables (Step 22)
No `IRANDIRECT_*` variables exist in source/deployment. If any are introduced
later, prefer `PATHVEER_*` and accept both during migration with `PATHVEER_*`
precedence. Not implemented here.

## 23. Observability environment/config (Step 23)
`deployment/observability/.env.example` references "IranDirect local observability
stack" and "IranDirect.Service" token context (comments). Exact migration list:
- `.env.example` header/comments → PathVeer.
- `docker-compose*.yml` container_names `irandirect-*` → `pathveer-*`.
- grafana dashboard file names + UIDs/titles `irandirect-*` → `pathveer-*`.
- prometheus/alertmanager rule file names `irandirect-*` → `pathveer-*`.
- This slice is separate from core rename (§31, 36.6) to avoid coupling.

## 24. Installer/deployment audit (Step 24)
**No installer technology exists in the repo today.** Service install is via
`AddWindowsService` (Program.cs) + `sc.exe`/manual registration; Phase 35.6 noted
in-place binary replacement assumptions. Phase 36 must **add** an installer/migration
step (or document the manual procedure) to perform stop-old/remove-old/install-new
(§8/§9). Do not invent behavior beyond what exists; document the gap as a 36.4/36.7
deliverable.

## 25. GitHub/repository rename (Step 25)
Repository `IranDirect` → future `PathVeer`. **Do NOT rename in this phase.**
Effects to document for execution: clone URLs, CI, badges, docs links (80 md files),
package references, scripts. GitHub redirects old repo URLs after rename — verify at
execution time; update `AI-START-HERE`/docs links accordingly in 36.x.

## 26. Domain architecture (Step 26)
User owns `pathveer.com`. Reserved future naming **only** (no DNS/SaaS changes):
`pathveer.com`, `app.pathveer.com`, `api.pathveer.com`, `docs.pathveer.com`,
`status.pathveer.com`. Current agent does not depend on these.

## 27. Old-version rollback (Step 27)
Rollback PathVeer → IranDirect: state root (legacy `%ProgramData%\IranDirect`
retained via copy, §7), service name (`IranDirect` re-registered), pipe
(`IranDirect.Control.v1`), executable (`IranDirect.*.exe` restored), prefix cache +
RouteInventory + RouteMutationJournal (legacy root intact). **Rollback must not
lose ownership proof** — journal file is copied, not moved.
**Rollback boundary (critical):** once a country-generalized state has been written
(non-IR `DirectCountryCode` such as IQ/RO/BR/US), **very old IranDirect versions
that predate 35.2/35.3 cannot semantically understand it** and may mis-handle the
config/journal. Therefore the safe rollback floor is the **final globalized
IranDirect build** (Phase 35.7 / `e4847b9`+), not arbitrary ancient releases.
Document this boundary explicitly.

## 28. Upgrade-version boundary (Step 28)
Recommend: **PathVeer officially supports upgrades from the final globalized
IranDirect build only** (post-35.7). Older releases must first upgrade to that
globalized build (an intermediate hop) before PathVeer. This drastically simplifies
migration (no need to handle pre-country-generalization state shapes) and is
consistent with the rollback floor in §27. Evaluate against git history: the
globalization line (35.1–35.7, base `93e2d87`→`e4847b9`) is the supported floor.

## 29. Security implications (Step 29)
Names participate in: Windows service SID/ACL (ServiceName), named-pipe ACL
(pipe name), file ACLs on `%ProgramData%\<brand>` root, firewall/Defender exclusions
referencing exe paths, OTel collector auth token audience (service.name in headers).
Rename must **not widen permissions**: keep the same pipe ACL model, same
per-service principal, same file ACL inheritance; only the literal names change.
Verify OTel token audience still matches after `service.name` becomes
`PathVeer.Service` (collector must accept it — update `.env`/collector config in
36.6, not here).

## 30. Tests required for implementation (Step 30)
Permanent future acceptance tests (in 36.2–36.8), none required in 36.1:
- fresh PathVeer install (state root = PathVeer, no legacy present)
- IranDirect final build → PathVeer upgrade (legacy root migrated, idempotent re-run)
- journal pending during upgrade recovers
- legacy config loads (brand-neutral JSON unchanged)
- IR/IQ/RO cache preserved under migrated root
- route ownership survives; endpoint ownership survives
- old pipe `IranDirect.Control.v1` + new pipe `PathVeer.Control.v1` both serve during
  compat window; clients prefer new, fall back to old
- old/new CLI alias behavior
- service single-authority (old removed before new starts; no concurrent authority)
- telemetry compatibility (legacy `irandirect_*` series still emitted in v1)
- rollback to final globalized IranDirect build (state/pipe/service restored)

## 31. Implementation slices (Step 31)
- **36.2 — source namespaces/project/assembly rename.** Goal: rename projects +
  namespaces (PathVeer.*) and keep build green. Compat: no external identity yet.
  Tests: full suite. Rollback: revert commit. Commit: `refactor(branding): rename
  IranDirect.* projects and namespaces to PathVeer.*`.
- **36.3 — persisted state-root migration.** Goal: `%ProgramData%\PathVeer` with
  copy-from-legacy + idempotent fallback. Files: Program.cs, persistence root
  resolver. Compat: legacy root retained. Tests: migration idempotency + journal
  ownership. Commit: `feat(branding): migrate state root to PathVeer with legacy
  fallback`.
- **36.4 — Windows Service + IPC identity migration.** Goal: ServiceName
  `PathVeer Service`/`PathVeer`; pipe dual-listen `PathVeer.Control.v1` +
  `IranDirect.Control.v1`; stop-old/remove-old/start-new. Files: Program.cs,
  IranDirectServiceNames, IranDirectPipeNames. Compat: mixed-version IPC (35.6).
  Tests: single-authority + pipe fallback. Commit: `feat(branding): migrate Windows
  service and IPC pipe identity with legacy fallback`.
- **36.5 — CLI/Tray/user-facing rebrand.** Goal: exe `PathVeer.*`, `pathveer`
  command + `irandirect` compat alias, Tray text, support-bundle filename
  `PathVeer-Support-*.zip`, diagnostics text. Files: Cli/Tray. Tests: CLI alias,
  bundle filename, Tray text. Commit: `feat(branding): rebrand CLI, Tray and
  user-facing text`.
- **36.6 — telemetry/observability migration.** Goal (v1): `service.name` =
  `PathVeer.Service`; retain `IranDirect.Core` source/meter + `irandirect_*`
  series; later recording-rule aliases. Files: ObservabilityResourceBuilder,
  ObservabilityOptions, deployment env/compose/dashboards/rules. Compat: no history
  split. Tests: telemetry architecture parity. Commit: `feat(branding): set
  service.name to PathVeer.Service; retain metric contract`.
- **36.7 — packaging/installer/update migration.** Goal: add stop-old/remove-old/
  install-new migration (none exists today, §24). Files: new installer/migration
  script. Tests: upgrade sequence. Commit: `feat(branding): add PathVeer
  install/migration for IranDirect upgrade`.
- **36.8 — end-to-end IranDirect→PathVeer upgrade acceptance.** Goal: full matrix
  from §30. Tests: permanent acceptance suite. Commit: `test(branding): validate
  end-to-end IranDirect to PathVeer upgrade`.

## 32. Brand-free architecture check (Step 32)
The routing core should keep **domain** names, not replace `IranFoo → PathVeerFoo`
where there is no branding reason. Verified brand-neutral domain types to KEEP
as-is (rename only their namespace root, not the type name): `CountryPrefixProvider`,
`RuntimeChangeSetPlanner`, `RouteMutationJournal`, `RouteInventory`,
`VpnEndpointInventory`, `ManagedRoute`, `PrefixWorkloadGenerator`,
`Ipv4PrefixAggregator`. Do **not** over-brand internal domain logic. The rename is a
namespace-root + external-identity change, not a domain-concept rename.

## 33. Documentation (this phase)
- `docs/branding/phase-36.1-pathveer-rename-audit.md` (this file)
- `AI-START-HERE.md` +1 nav line

## 34. Verification (Step 34)
Baseline (docs-only; no code changed):
- `dotnet build IranDirect.slnx -c Debug` → **succeeded, 0 warnings**.
- `dotnet test IranDirect.Core.Tests -c Debug` → **2365 passed** (baseline 2343 +
  35.7A's 22).
- `dotnet test IranDirect.Service.Tests -c Debug` → **50 passed**.
No performance suites rerun (no code changes).

## 35. Scope proof (Step 35)
`git status --short` shows only:
- `M AI-START-HERE.md`
- `?? docs/branding/phase-36.1-pathveer-rename-audit.md`
`git diff --stat` (vs base `f3418d7`) = only `AI-START-HERE.md | 1 +`.
No source/project/config/deployment changes.

## 36. Final report (condensed; full answers in §1–§35)
1. branch/base: `development/service-authority` / `f3418d7`.
2. total IranDirect occurrence inventory: 4,944 line matches / 700 files.
3. category counts A–J: A ~4,900 (source); B 6 projects+1 sln; C 2 (service+pipe);
   D 1 (state root); E 261 (command enum) + pipe; F ~1,703 (telemetry refs) +
   `irandirect_*` series; G 8 deployment files; H CLI/Tray/bundle text; I legacy
   migration id; J 80 md + repo.
4. safe direct renames: A, H text, B (with follow-through), support filename.
5. migration-required: C, D, E (compat alias), F (planned), G (planned).
6. legacy aliases that must remain: `IranDirect.Control.v1` pipe (compat window),
   `iran-ipv4-prefixes.txt` detection, `IranDirect` service SID during upgrade,
   `irandirect_*` metrics until dashboard cutover.
7. state-root current/future: `%ProgramData%\IranDirect` → `%ProgramData%\PathVeer`.
8. selected state migration: copy-then-switch, legacy retained, idempotent.
9. Service current/future: `IranDirect Service`/`IranDirect` → `PathVeer
   Service`/`PathVeer`; remove-old/create-new.
10. single-authority upgrade: stop-old → preserve → migrate → remove-old →
    start-new → recover → reconcile (each step blocks).
11. named-pipe strategy: dual-listen (A) `PathVeer.Control.v1` +
    `IranDirect.Control.v1`, clients prefer new/fallback old.
12. project/namespace target graph: IranDirect.* → PathVeer.* (§11).
13. executable/assembly strategy: new `PathVeer.*.exe` at new paths; `irandirect`
    CLI compat alias; remove old exes with old service.
14. telemetry strategy: Option B — `service.name=PathVeer.Service`; retain
    `IranDirect.Core` source/meter + `irandirect_*` series.
15. dashboard/alert strategy: keep `irandirect_*`; later recording-rule aliases if
    renamed; no history split in v1.
16. JSON compatibility: brand-neutral; no property rename.
17. route ownership compatibility: brand-neutral; no route reset.
18. prefix-state compatibility: `prefixes/{ISO2}/` unchanged; legacy file retained.
19. CLI strategy: `pathveer` + `irandirect` alias; help text rebrand.
20. Tray strategy: text + `PathVeer-Support-*.zip`; no UX redesign.
21. support/log strategy: bundle filename rebrand; EventLog source follows
    service name; legacy logs retained.
22. env-var strategy: none today; future `PATHVEER_*` with dual-accept.
23. installer findings: **none exists**; must be added in 36.7.
24. GitHub rename implications: documented; do not rename here; redirects exist.
25. pathveer.com future naming plan: reserved (app/api/docs/status); no changes.
26. rollback boundary: safe floor = final globalized IranDirect build (post-35.7);
    do not roll below country-generalization.
27. minimum supported upgrade version: final globalized IranDirect build.
28. security implications: same ACL/pipe/file models; no permission widening;
    verify OTel token audience after service.name change.
29. implementation test matrix: §30.
30. brand-free core findings: domain types kept; rename is namespace-root +
    external-identity only (§32).
31. Phase 36 implementation slices: 36.2–36.8 (§31).
32. baseline build/test counts: build clean 0 warn; Core 2365; Service 50.
33. exact files changed: `docs/branding/phase-36.1-pathveer-rename-audit.md`,
    `AI-START-HERE.md` (+1).
34. scope proof: only the two doc files; zero production change.
35. risks: (a) no installer today → 36.7 must add upgrade orchestration; (b)
    concurrent-authority if old service not removed → mitigated by §9 sequence;
    (c) telemetry history split if `irandirect_*` renamed prematurely → mitigated
    by §13 Option B; (d) rollback below globalized build unsafe → documented §27.
36. recommended next slice: **36.2** (source namespaces/project/assembly rename) —
    lowest external-risk, unblocks later slices, keeps build green.
37. recommended commit message: `docs(branding): design IranDirect to PathVeer
    migration`.

---

## Explicit legacy identifiers that MUST remain
- `iran-ipv4-prefixes.txt` — legacy IR prefix migration detection (CountryPrefixStore).
- `IranDirect.Control.v1` — IPC pipe compat alias during transition window.
- `IranDirect` Windows service SID — during upgrade window (old removed before new).
- `irandirect_*` Prometheus series + `IranDirect.Core` telemetry source/meter — v1
  operational contract retained (recording-rule aliases only if later renamed).
