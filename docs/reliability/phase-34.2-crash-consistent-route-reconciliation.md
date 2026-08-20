# Phase 34.2 — Crash-Consistent Route Mutation Recovery

Status: IMPLEMENTED (not yet committed)
Branch: development/service-authority
Precedes: Phase 34.1 (production risk audit) — selected slice R1
Follows spec: Phase 34.2 (crash window + deterministic recovery + external-route safety)

## 1. Problem

A native route mutation (add/delete) is committed to the OS routing table
BEFORE the authoritative IranDirect ownership inventory is persisted. Between
those two steps the process can die (crash / kill / power loss). The two
resulting orphan states were read-only-diagnosed but never auto-healed:

- **Orphaned route (ADD interrupted):** native route present, inventory missing.
  Next cycles see ownership absent and — because the planner only issues a
  route when it is *not owned* — never re-add it. The route leaks and silently
  carries traffic outside IranDirect's control.
- **Blackhole / double-ownership (DELETE interrupted):** native route gone,
  inventory still claims ownership. If the route is still desired, the planner
  sees ownership present and skips the re-add, leaving traffic blackholed.
  If it is not desired, the stale "owned" record blocks cleanup.

The naive fix — "on startup, delete any route that looks like an IranDirect
route but isn't in the inventory" — is unsafe: an operator or another tool may
create a route with an identical destination/gateway/interface. Adopting or
deleting it based on shape alone would corrupt externally-managed state.

## 2. Design

A **durable write-ahead mutation journal** proves *IranDirect initiated* an
interrupted mutation. Recovery only acts on journal entries, never on route
shape.

### Files added (IranDirect.Core)
- `Runtime/Execution/RouteMutationJournal.cs` — `RouteMutationKind` (Add/Delete),
  `RouteMutationInventoryKind` (Prefix/Endpoint), `RouteMutationJournalEntry`
  record (schema version, kind, inventory kind, route identity, destination
  prefix, gateway, interface index, metric, description, mutation id, created
  timestamp), and `RouteMutationJournalCorruptException`.
- `Runtime/Execution/IRouteMutationJournal.cs` — `WriteIntentAsync`,
  `ClearIntentAsync`, `LoadAllAsync`, `ClearAsync`.
- `Runtime/Execution/RouteMutationJournalStore.cs` — file-backed store with
  atomic tmp + `File.Move` write. Unlike `JsonStore`, a corrupt or
  schema-mismatched file throws `RouteMutationJournalCorruptException` instead
  of silently resetting to empty (a silent reset would erase proof of an
  in-flight mutation). Reuses the same fault-injection points and retry/backoff.
  Serialized by a dedicated `SemaphoreSlim(1,1)` so parallel prefix adds are
  safe.
- `Runtime/Execution/RouteMutationRecovery.cs` — the recovery service
  (see §3).
- `Runtime/Execution/NullRouteMutationJournal.cs` — no-op for non-host callers
  and unit tests that manage durability themselves.

### Files modified
- `WindowsRuntimeExecutionStepHandler.cs` — `IRouteMutationJournal` dependency
  (null → `NullRouteMutationJournal.Instance`). Writes a durable intent
  **before** the native mutation and clears it **only after** the authoritative
  inventory persist commits, on every add/remove path (prefix and endpoint).
- `ServiceCompositionRoot.cs` — registers `RouteMutationJournalStore`
  (`route-mutation-journal.json`) and `RouteMutationRecovery`; wires the real
  journal into the step handler.
- `IranDirectWorker.cs` — runs recovery once at startup (before the first
  cycle), through the `OperationCoordinator` gate so it cannot race a cycle or
  an early IPC command.

## 3. Crash window & recovery contract

### Mutation sequence (per route)
1. Persist intent atomically (kind, identity, inventory kind, fields).
2. Perform native mutation.
3. Persist authoritative inventory state.
4. Clear the journal intent atomically.

If the process dies anywhere in 1–4, the journal intent survives.

### Recovery (on startup, idempotent)
For each outstanding intent, observe current native routes + relevant
inventory (the journal says *which* inventory — prefix vs endpoint):

- **ADD + native present + inventory missing** → adopt: record ownership.
  Authorized because the durable pre-mutation intent proves IranDirect
  initiated the add. (Was: permanent orphan.)
- **ADD + native absent** → stale: clear intent, no ownership claimed.
- **ADD + inventory already owns** → committed: clear intent.
- **DELETE + native absent + inventory claims ownership** → complete the
  delete by removing the stale ownership record. (Was: blackhole.)
- **DELETE + native present + inventory owns** → preserve ownership, clear
  intent; normal reconciliation retries the native delete next cycle.
- **DELETE + inventory already clean** → committed: clear intent.

External routes: with **no** journal entry, an externally-created route that
happens to match a desired identity is **never** adopted or deleted. The
journal — and only the journal — is the proof of IranDirect initiation. This is
covered by regression tests (`ExternalRoute_*`).

A single intent whose adopt/complete fails (e.g. transient inventory error) is
logged and left in the journal; recovery of the other intents is not blocked,
and the deferred entry retries on the next startup.

If the journal file is corrupt/unreadable, recovery logs a deterministic
operational error (no route identity, no exception text in the log), quarantines
the file to `*.corrupt`, and makes **no** route changes.

## 4. Tests (IranDirect.Core.Tests)
- `RouteMutationJournalStoreTests` — corrupt → throws (not silent reset),
  schema-mismatch → throws, write/clear, idempotent load, replace-by-identity.
- `RouteMutationRecoveryTests` — ADD (A1 native+missing→adopt, A2 native
  absent→stale-clear, A3 committed→clear), DELETE (D1 native-absent→complete,
  D2 native-present→defer-clear, D3 committed→clear), endpoint variants,
  **external-route safety** (identical / different metric / different gateway /
  externally-recreated-after-inventory-loss → all untouched), idempotency ×2,
  corrupt-journal → quarantined, no-journal → unchanged.
- `RouteMutationStepHandlerJournalTests` — pre-fix crash window reproduced
  without killing the process (native add succeeds, inventory persist prevented,
  graceful compensation bypassed; journal remains; next recovery adopts),
  graceful-compensation success clears the journal, and happy-path
  add (prefix + endpoint) writes-then-clears the journal.

Full Core suite: 2189 passed, 0 failed.

## 5. Out of scope (per spec)
Not addressed: general cross-store transactions; planner semantics; prefix/DNS
logic; IPC wire protocol; telemetry contract; dashboards/alerts; observability
deployment; desired-configuration schema; endpoint-protection semantics beyond
what the shared journal naturally covers.

## 6. Recommended commit message (do NOT auto-commit)
```
fix(runtime): make route ownership crash-consistent

Add a durable write-ahead mutation journal so a native route mutation that
succeeds but whose inventory persist is interrupted by process death can be
deterministically recovered (completed or rolled forward) on the next startup.
Recovery only acts on journal-proven IranDirect mutations, never on route shape,
so externally-created routes are never adopted or deleted.
```
