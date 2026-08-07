# Phase 35.2 — Country Identity and Configuration Contract

Status: implemented (not yet committed)
Branch: `development/service-authority`
Base: `93e2d87 docs(globalization): audit country-agnostic routing support`

This phase introduces a validated, normalized, backward-compatible direct-country
identity into `DesiredConfiguration` while preserving EXACTLY today's routing
behavior for existing (IR) installations. The prefix source remains Iran-only; a
temporary safety gate prevents a non-IR selection from reconciling through the
Iran-only source.

==================================================
1. Selected configuration property name
==================================================

Property: `DirectCountryCode` (on `DesiredConfiguration`).

Rationale: the field means "the destination country whose IP prefixes should
bypass the VPN and use the direct/local route." It is routing *policy*, not the
client's physical location, VPN exit, nationality, locale, language, or
timezone. `DirectCountryCode` reads unambiguously as a routing-policy choice,
and a future SaaS policy such as `DirectCountryCode = "RO"` is immediately
understandable. `CountryCode` was rejected as too easily conflated with client
location; `BypassCountryCode`/`LocalRouteCountryCode` are usable but more
jargon than `Direct` (which mirrors the existing "direct gateway" vocabulary in
the runtime model).

==================================================
2. Country identity representation
==================================================

A single centralized value object `DirectCountryCode` (immutable,
`IEquatable`) owns all ISO 3166-1 alpha-2 semantics:

- canonical form: two uppercase ASCII letters (`"ir"` -> `"IR"`);
- syntactic rule: exactly two ASCII letters (rejects "", "I", "IRQ", "1R",
  "R1", whitespace, non-ASCII lookalikes);
- recognized assignment: membership in a single immutable `HashSet<string>`
  catalog of all ISO 3166-1 alpha-2 codes (not limited to IR/IQ/RO);
- normalization: `char.ToUpperInvariant`, ordinal; no culture-sensitive
  behavior;
- equality/hash: ordinal over `Code`;
- failure mode: `DirectCountryCode.Parse` throws
  `CountryCodeFormatException`; `TryParse` returns false for invalid input.

No country string is validated anywhere else in production code. There is no
giant switch statement; the catalog is the single authority. No new dependency
was introduced (the catalog is a static literal set).

==================================================
3. Persisted JSON representation
==================================================

The property is serialized as a plain ISO alpha-2 string via
`DirectCountryCodeJsonConverter` (registered in
`DesiredConfigurationStore.CreateJsonOptions`):

{
  "SchemaVersion": 1,
  "Enabled": true,
  "VpnProfilePath": "C:\\VPN\\work.ovpn",
  "DirectCountryCode": "IR"
}

No object graph is persisted; the wire format is SaaS/API friendly. An absent
field resolves to the property default (`DirectCountryCode.IR`), which is also
how a fresh `new DesiredConfiguration()` behaves.

==================================================
4. Legacy compatibility (hard gate)
==================================================

Five cases, all satisfied:

A. File missing -> Phase 34.4 `DesiredConfigurationMissingException`
   (`CONFIGURATION_UNAVAILABLE`). Unchanged. Missing != disabled.
B. File present, legacy schema, no `DirectCountryCode` field -> deserializes
   to the property default `IR`. No user action, no reset, no route change.
C. Explicit `IR` -> valid, preserved.
D. Explicit `IQ`/`RO` -> representable and valid, but (pre-35.3) routed through
   the temporary safety gate (see §5).
E. Invalid country (e.g. "IRQ", "XX") -> the converter throws during
   deserialization; the store wraps it as
   `DesiredConfigurationCorruptException` (fail closed, file preserved).
   Phase 34.4 semantics are not weakened.

==================================================
5. Non-IR temporary safety gate (critical)
==================================================

Until the prefix source is generalized (Phase 35.3), only the legacy/default
country (IR, including a null legacy value) is supported for routing. A
recognized non-IR country is valid configuration but must NOT reconcile through
the Iran-only source (which would install Iran prefixes as if they belonged to
that country).

Single seam: `IranDirectWorker`. After loading the configuration it computes
`shouldReconcile = desired is { Enabled: true } &&
DirectCountryRouting.IsDirectCountrySupported(desired.DirectCountryCode)`.
The startup cycle and each periodic cycle run only when `shouldReconcile`.
For an enabled-but-unsupported country the worker logs a bounded warning and
continues polling; the host stays alive; **no route mutation occurs**.

`DirectCountryRouting.IsDirectCountrySupported(code)` is the entire temporary
allowlist: `code is null || code == DirectCountryCode.IR`. It is a single
method with an explicit "REMOVAL POINT (Phase 35.3)" comment.

Why the worker (not `UpdatePrefixesAsync`): `UpdatePrefixesAsync` is a
low-level fetch that historically needs no configuration and must remain usable
without a saved file (the integration tests rely on this). The worker already
owns the authoritative configuration load and the reconciliation loop, so it is
the correct and minimal gate. A direct `update-prefixes` call only refreshes
the cached dataset and causes no install without a cycle, so gating it would be
redundant and would break its contract.

==================================================
6. Configuration-write preservation
==================================================

All mutation paths use record `with` and never touch `DirectCountryCode`
implicitly:
- `DesiredConfigurationService.SetEnabledAsync` / `SetProfilePathAsync` keep
  the existing `DirectCountryCode` (tests `SetEnabledAsync_PreservesCountry`,
  `SetProfilePathAsync_PreservesCountry`).
- First-run bootstrap (missing file) still defaults to `IR` via
  `ConfigurationDefaults.Create()` / `LoadOrDefaultAsync`.
- IPC writes carry the additive JSON field; old payloads without it load as IR.

==================================================
7. IPC / CLI / Tray / Snapshots
==================================================

- IPC: no command/protocol/named-pipe change. `DesiredConfiguration` crosses
  IPC with the additive `DirectCountryCode` string; the converter makes old
  payloads (no field) load as IR and new payloads round-trip. Verified by the
  store round-trip tests.
- CLI/Tray: no UX changes (Phase 35.5 owns country selection). Existing
  enable/disable/set-profile operations preserve the field.
- Snapshots: `RuntimeSnapshot.Configuration` and `SupportSnapshot.Configuration`
  already embed `DesiredConfiguration`, so the field appears automatically. It
  is routing policy, not user geolocation, and exposes no sensitive network
  data.

==================================================
8. Planner / executor isolation (proven)
==================================================

No country awareness was introduced into `RuntimeChangeSetPlanner`,
`RuntimeExecutor`, `WindowsRuntimeExecutionStepHandler`, `RouteMutationJournal`,
`ManagedRoute` inventory, VPN endpoint logic, or custom-route logic. The full
Core + Service suites (2240 + 50 tests) pass, including the existing planner
benchmarks and the Phase 34.4 worker gating tests, proving no behavioral
drift for IR users. The country selection lives entirely above the
desired-prefix construction boundary (in configuration + the worker gate).

==================================================
9. Legacy routing equivalence (primary regression gate)
==================================================

An IR configuration with the existing IR dataset produces identical
desired/reconciliation behavior to before Phase 35.2: no route count/order/
identity change attributable to the country identity. This is demonstrated by
the unchanged pass of all existing routing tests, the unchanged planner
benchmarks, and the unchanged `ValidEnabledConfiguration_Reconciles` worker
test (IR still reconciles).

==================================================
10. Tests added / modified
==================================================

New:
- `IranDirect.Core.Tests/Configuration/DirectCountryCodeTests.cs`
  (normalization, validation, equality, IR default, non-IR unsupported).
- `IranDirect.Core.Tests/Configuration/DesiredConfigurationStoreTests.cs`
  (legacy-absent -> IR; explicit IR; IQ/RO representable; invalid -> Corrupt;
  round-trip; SetEnabled/SetProfile preserve country).
- `IranDirect.Service.Tests/IranDirectWorkerTests.cs`
  (`NonIRDirectCountry_EnabledButUnsupported_SkipsReconciliation`).

Modified (no assertion weakened):
- `DesiredConfiguration.cs` (added property)
- `DesiredConfigurationValidator.cs` (defensive country check)
- `DesiredConfigurationStore.cs` (registered converter)
- `IranDirectWorker.cs` (temporary gate)
- `DirectCountryCode.cs`, `CountryCodeFormatException.cs`,
  `DirectCountryCodeJsonConverter.cs`, `DirectCountryRouting.cs` (new)

==================================================
11. Phase 35.3 removal point
==================================================

Remove the gate by replacing the body of
`DirectCountryRouting.IsDirectCountrySupported` with a real check against the
generalized country-prefix source (e.g. is a source configured/available for
this code?). Once the source supports the selected country, the worker gate
opens and normal reconciliation resumes without further structural change. The
`DirectCountryCode` value object, serialization, configuration field, validator
check, and write-path preservation all remain and require no further edits.

==================================================
12. Risks / limitations
==================================================

- IPv6 is still not supported (unchanged; source fetches ipv4 only).
- A non-IR selection is silently "no routing" until 35.3; this is the intended
  safe transitional state, surfaced via a bounded warning, not an error.
- The ISO catalog is a static literal; it should be kept in sync with the
  official ISO 3166-1 assignment if countries are added/removed (rare).
- Branding (`IranDirect.*`) is intentionally untouched (Phase 36 boundary).
