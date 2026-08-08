# Phase 35.5 — Country Selection UX and Refresh Trigger

Status: COMPLETE (CLI + Tray + IPC + refresh trigger + tests + docs; no
production routing/planner/executor/journal/telemetry-dimension changes)
Branch: `development/service-authority`
Base: `test(globalization): validate country switching and offline safety` (Phase 35.4)

## 1. Goal

Expose the already-implemented `DirectCountryCode` capability through the
existing IranDirect CLI, Tray, and IPC surfaces, with a safe country-change →
prefix-refresh trigger, preserving every Phase 35.4 failure-safe behavior.

## 2. Command surfaces (no new envelope)

- **CLI:** `country get`, `country set <ISO2>`, `country list` (new
  `IranDirect.Cli/CountryCliRunner.cs`, dispatched in `Program.cs`).
- **Tray:** a "Direct country" dropdown under the existing menu, built from the
  centralized ISO catalog; selection invokes the same IPC command path.
- **IPC:** reused the existing `DesiredConfiguration` get/set transport. Added
  exactly one command, `SetConfigurationDirectCountry`, to the existing
  `IranDirectCommand` enum — no pipe-name change, no version break, no new
  JSON envelope. `GetConfiguration` already returns the requested country, so
  `country get` needed no new command.

## 3. CLI contract

| Command | Behavior |
|---|---|
| `country get` | prints the requested ISO alpha-2 code (defaults to `IR` if config missing) |
| `country set <ISO2>` | validates, persists `DirectCountryCode`, triggers a prefix refresh for that country, prints the result |
| `country list` | lists **every** recognized ISO 3166-1 alpha-2 code with its display name |

- Validation is delegated entirely to `DirectCountryCode.TryParse` — no raw
  string logic duplicated anywhere. Lowercase input is normalized.
- Invalid code → usage message, exit 6.
- `country set` preserves `Enabled` and `VpnProfilePath` (only the country
  field is written).
- Changing country does **not** implicitly enable or disable the service.

## 4. `country list` / catalog

Uses `DirectCountryCode.AllSupported` — the same centralized, immutable ISO
catalog introduced in Phase 35.2 (now surfaced via a `Lazy`-backed property so
static initialization order is safe). The list is **not** hardcoded to IR/IQ/RO;
it contains the full ~249-entry catalog. Display names are presentation-only
(`DirectCountryCode.DisplayNames`, a `IR/IQ/RO` advisory map; absent codes fall
back to the code). Identity remains the ISO code; nothing is persisted.

## 5. Country-set ordering (the trigger)

`NamedPipeCommandServer.SetConfigurationDirectCountryAsync` performs, inside
the `OperationCoordinator` (so it is serialized with all other mutating ops):

1. validate the country (reject invalid → `INVALID_COUNTRY_CODE`);
2. persist `DesiredConfiguration.DirectCountryCode = <country>`
   (`DesiredConfigurationService.SetDirectCountryAsync`, which preserves
   `Enabled`/`VpnProfilePath`);
3. attempt `IranDirectController.UpdatePrefixesAsync(country)` — the prefix
   refresh for the **selected** country;
4. only report success when a valid dataset is available; if the refresh
   fails, the config still holds the requested policy and the command returns
   a **partial** result (exit 1 from CLI, message shown in Tray).

This directly fixes the Phase 35.4 gap: the worker no longer needs a separate
manual `prefix-update` after a country change — the set command triggers it. No
new event bus; the smallest architecture-consistent option was chosen.

## 6. Source-unavailable behavior

`country set IQ` with the source down and **no** IQ cache:

- config is persisted as `IQ` (requested policy);
- the refresh throws; it is caught and reported as a partial set;
- **no destructive reconciliation** occurs — the periodic worker observes
  empty IQ prefixes → `PrefixesUnavailable` blocker → reconciliation stays
  `Blocked` → existing IR routes remain. Host stays alive; retry remains
  possible.
- The config is **not** rolled back to IR. This matches Phase 35.4 semantics:
  requested policy = IQ; effective routing stays blocked until the IQ dataset
  exists.

## 7. Cached-target behavior

`country set IQ` with a valid IQ cache present and the source down: the refresh
attempt may fail on the network, but the persisted IQ cache is authoritative, so
the next cycle reconciles IQ from last-known-good — a safe switch with no IR
fallback and no unnecessary network dependency.

## 8. Tray UX

- A "Direct country: <Name> (<CODE>)" menu item with a dropdown of **all**
  supported codes.
- Shows the current requested country (refreshed from `IranDirectStatus
  .RequestedCountryCode` on every status poll, and immediately after a set).
- Disabled service still permits changing country (changing country never
  enables the service).
- A failed/unavailable dataset shows a clear informational message; no route
  details are exposed in the menu.
- Selecting a country invokes the same `SetConfigurationDirectCountry` IPC
  command; if the dataset is unavailable the request is still accepted and the
  user is told the switch is pending.

## 9. Diagnostics / status (Step 16)

`IranDirectStatus.RequestedCountryCode` was added (diagnostic only; no new
telemetry dimension) so the existing `status`/`snapshot` output answers "Requested
direct country: <CODE>" without exposing the prefix list. The CLI `config` and
Tray config dialog also print `Direct country: <CODE>`.

## 10. Serialization / concurrency (Step 14)

Country write, prefix refresh, and runtime cycle all flow through
`OperationCoordinator` (`SemaphoreSlim(1,1)`). The new set handler explicitly
runs persist+refresh inside `_operations.ExecuteAsync`, so a country set, its
refresh, and any concurrent cycle can never mutate shared state at the same
time. No new locking primitive was introduced.

## 11. Retry (Step 15)

No hot retry loop and no automatic destructive re-attempt. A failed set leaves
the requested policy in place and the user can simply re-run `country set <CC>`
(or click the menu item again). The periodic worker remains safely blocked
until the dataset appears.

## 12. Architecture isolation (Step 21)

No country branches were added to: `RuntimeChangeSetPlanner`,
`RuntimeExecutor`, `WindowsRuntimeExecutionStepHandler`,
`RouteMutationJournal`, `RouteMutationRecovery`, the VPN endpoint subsystem, or
the custom-route subsystem. Country UX calls only the configuration/prefix
layers (`DesiredConfigurationService`, `IranDirectController
.UpdatePrefixesAsync`, `GetStatusAsync`). The only additions in Core were:
`DirectCountryCode.AllSupported`/`DisplayNames`/`DisplayName`, the
`SetDirectCountryAsync` service method, `IranDirectStatus.RequestedCountryCode`,
the `SetConfigurationDirectCountry` command + `ServiceResponse.PrefixRefreshed`,
and the command→telemetry-string mapping entry (a command, not a country tag).

## 13. No PathVeer rename

No project, namespace, service, named-pipe, telemetry, folder, or executable
was renamed. User-visible CLI/Tray still say "IranDirect". Phase 36 owns the
rename.

## 14. Tests added

- `IranDirect.Core.Tests/Cli/CountryCliRunnerTests.cs` — get/set/list, lowercase
  normalization, invalid code, preserve Enabled/VpnProfilePath, source-
  unavailable partial set (exit 1, config kept), service failure, full catalog.
- `IranDirect.Core.Tests/Configuration/DirectCountryCodeTests.cs` — catalog
  normalization/validation, `AllSupported` ordering/size, display names.
- `IranDirect.Core.Tests/Globalization/CountrySelectionAcceptanceTests.cs` —
  end-to-end: CLI set IQ → refresh → reconcile; Tray set RO → refresh →
  reconcile; failed set (source down/no cache) → config stays IQ, no
  destructive change; cached set (source down/IQ cache) → safe switch; preserve
  Enabled/ProfilePath; disabled config permits change.
- `IranDirect.Core.Tests/Globalization/CountryMenuBindingTests.cs` —
  Tray dropdown is built from `AllSupported` (not hardcoded), label format,
  default Iran, lowercase normalization.
- Plus the pre-existing `TelemetryEnumMappingTests` was extended by adding the
  `SetConfigurationDirectCountry` mapping (required because the command enum
  grew).

## 15. Verification evidence

- `dotnet test IranDirect.Core.Tests` → **2277 passed** (includes 58 new tests).
- `dotnet test IranDirect.Service.Tests` → **50 passed**.
- `Category=Stress` (Core) → **12 passed**.
- `dotnet build IranDirect.Benchmarks -c Release` → clean.
- `dotnet build IranDirect.slnx -c Debug` → clean.

## 16. Remaining limitations / Phase 35.6 scope

- The refresh trigger runs synchronously inside the set command; a very slow
  source fetch will block the CLI/Tray call until it returns or throws. This is
  acceptable (fail-closed, retryable) but a future enhancement could make the
  refresh best-effort/background and rely on the periodic worker to complete it.
- No automated periodic prefix refresh on *config change* beyond the explicit
  set-command trigger; the worker still only observes the cache on its cycle.
- RIPEstat commercial-use terms review (from 35.3/35.4) remains a follow-up.
- IPv6, multi-country policy, SaaS/cloud source abstraction, and the PathVeer
  rename remain explicitly out of scope.
- Suggested Phase 35.6: refine the refresh UX (progress/background), add a
  config-change → prefix-refresh service-side trigger so the set command does
  not need to block, and possibly surface a clearer "switch pending / dataset
  unavailable" state in the Tray beyond the current message box.
