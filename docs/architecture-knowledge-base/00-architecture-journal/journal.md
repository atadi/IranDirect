# Architecture Journal

> **Historical note.** Journal entries are append-only and may correctly use IranDirect terminology for work completed before the PathVeer rename. Current product/milestone authority is `../AI/CURRENT.md`.

This journal is chronological and append-only.

Each entry captures a transferable architectural lesson discovered through real project work.

## Entry Format

- Date
- Lesson number and title
- Context
- Insight
- IranDirect example
- Broader application
- Tradeoffs
- Related ADRs and patterns

---

## Lesson 001 — Architecture Organizes Change

Architecture is not merely the organization of code. It is the organization of future change.

Code represents today's implementation. Architecture determines whether tomorrow's change remains understandable, safe, and incremental.

---

## Lesson 002 — Complexity Does Not Disappear

Complexity cannot be eliminated; it can only be placed.

Good architecture concentrates necessary complexity in the component that truly owns it. Poor architecture duplicates or distributes complexity across unrelated components.

---

## Lesson 003 — Single Authority

A mutable system resource should have one authority.

For IranDirect, the Windows Service is the only component allowed to mutate routes. CLI and Tray express intent.

---

## Lesson 004 — Desired State Beats Commands

Commands such as Enable, Disable, and Repair are user intentions.

Internally, the system should describe the desired world and reconcile reality toward it.

---

## Lesson 005 — Ownership Beats Discovery

Never infer ownership from similarity.

Persist explicit inventories of resources created or managed by the system.

---

## Lesson 006 — VPN Safety Is an Invariant

If IranDirect cannot safely protect the VPN endpoint, it must not install direct-routing prefixes.

A failed enable operation is preferable to broken VPN connectivity.

---

## Lesson 007 — Diagnostics Must Be Read-Only

Diagnostics explain reality and health. They never repair or mutate the system.

---

## Lesson 008 — Configuration Is Not Runtime State

Configuration describes what the user wants.

Runtime state describes what the system has achieved.

Mixing them creates ambiguity and unreliable reconciliation.

---

## Lesson 009 — Human-Owned Configuration Should Be Semantic

Persist meaningful values such as `"OpenVpn"` instead of implementation details such as `0`.

Human-readable configuration is easier to understand, review, and migrate.

---

## Lesson 010 — Observation Reports Facts

Observation should report platform facts and failures without deciding what ought to happen.

---

## Lesson 011 — Planning Is Pure

The planner describes the desired runtime and blockers.

It should be deterministic and side-effect free.

---

## Lesson 012 — Describe the Destination

The planner describes the infrastructure that should exist.

The reconciler decides the minimal operations required to reach it.

---

## Lesson 013 — Move Decisions Toward the Domain

Domain components answer why.

Orchestrators answer who should be called next.

Executors answer how.

---

## Lesson 014 — Thin Orchestrators

A controller should coordinate collaborators rather than contain business rules or platform operations.

---

## Lesson 015 — Stable Contracts, Replaceable Implementations

Contracts such as `ObservedRuntime`, `DesiredRuntime`, and `RuntimePlanSnapshot` should remain stable while platform-specific implementations evolve.

---

## Lesson 016 — Decide, Plan, Execute, Verify, Record

Deciding what must change, deciding how to change it, performing the change, verifying it, and recording ownership are separate lifecycle stages.

Each stage has its own vocabulary, invariants, and failure modes.

### Context

IranDirect's runtime reconciliation originally produced a `RuntimeChangeSet` that combined what should change and assumed execution would follow immediately. As the system grew, the need to preview changes, order them safely, verify postconditions, and record ownership became distinct concerns.

### Insight

A single "reconcile and execute" step conflates five lifecycle stages:

1. **Decide** what infrastructure differs from desired state (reconciliation).
2. **Plan** the order and grouping of platform operations (execution planning).
3. **Execute** platform operations (route add/delete).
4. **Verify** post-conditions (route exists or is absent).
5. **Record** ownership in durable inventory.

### Project Example

- `RuntimeChangeSetPlanner` decides what changes are needed.
- `RuntimeExecutionPlan` (domain model) defines the ordered steps.
- `RuntimeExecutor` (future) will execute them.
- `RuntimeVerifier` (future) will confirm post-conditions.
- `RouteInventoryStore` records ownership (existing).

### Broader Application

Any system that reconciles state toward a goal benefits from separating change detection, change ordering, change execution, and change verification. Conflating them makes previewing changes, safe partial execution, and rollback harder to implement.

### Tradeoffs

More stages mean more vocabulary and more types. The benefit is the ability to preview, order, verify, and partially recover without coupling stages that evolve at different speeds.

### Related ADRs and Patterns

- Phase 6 in project evolution
- Execution Plan pattern

---

## Lesson 017 — Domain Decisions Are Not DTOs

A DTO exists because a transport boundary needs data in a particular shape.

A Domain Decision exists because lifecycle stages need one authoritative, immutable, internally consistent decision contract.

### Context

IranDirect's pre-execution state was represented by three separate artifacts: `RuntimePlanSnapshot`, `RuntimeReconciliationResult`, and `RuntimeExecutionPlan`. Each was independently valid, but there was no contract-level guarantee that the execution plan actually corresponded to the reconciliation's change set. A consumer had to recompute that relationship or trust coincidence.

### Insight

When multiple upstream artifacts combine to form a single "what should happen next" answer, the architecture benefits from a validated domain contract that bundles them together. The validation is not transport shaping — it enforces domain invariants such as "a blocked reconciliation cannot have execution steps" and "every change must have a corresponding step."

### Broader Application

Any system with a detect-decide-act pipeline should consider a validated decision contract at the decide boundary. Without it, consumers must independently re-derive consistency constraints that the system already knows.

### Tradeoffs

- One more type in the domain.
- Validation cost on construction (negligible for typical plan sizes).
- Requires discipline to keep the contract transport-independent.

### Related ADRs and Patterns

- Phase 7 in project evolution
- Execution Plan pattern

---

## Lesson 018 — Orchestrators Compose Immutable Artifacts

An orchestrator's primary responsibility is to sequence collaborators and combine their results into a coherent output. It should not validate, transform, or re-derive the outputs of the components it orchestrates.

### Context

IranDirect needed a component that produces a complete pre-execution `RuntimeDecision` from three upstream artifacts: `RuntimePlanSnapshot`, `RuntimeReconciliationResult`, and `RuntimeExecutionPlan`. Each was already validated and self-consistent. The initial temptation was to add status-specific branching and re-validate the relationship inside the orchestrator.

### Insight

When each upstream artifact is independently valid and the composition contract is owned by the final domain type (`RuntimeDecision.Create`), the orchestrator becomes a pure sequential pipeline:

1. Call each collaborator in lifecycle order.
2. Pass the exact result of each step to the next.
3. Obtain the timestamp after all phases complete.
4. Return the composed domain artifact.

No status-specific branching, no duplicate validation, no re-derivation of invariants already enforced by the domain.

### Project Example

- `RuntimeDecisionBuilder` calls `IRuntimePlanCoordinator`, `IRuntimeReconciler`, `RuntimeExecutionPlanner`, and `TimeProvider` in sequence.
- RuntimeDecision.Create validates the composed result — the builder neither duplicates this check nor branches on status.
- ChangesApplied is rejected by RuntimeDecision.Create, not by the builder.
- The builder has no knowledge of route identities, execution ordering, or status semantics.

### Broader Application

Any orchestration that composes already-validated domain artifacts should:
- Delegate validation to the domain contract (which knows the rules).
- Delegate transformation to the owning component (which knows the shape).
- Keep the orchestrator focused on ordering, reference preservation, and side-effect boundaries.

### Tradeoffs

- Requires discipline not to add "one more check" inside the orchestrator.
- If the domain contract's validation becomes expensive, the orchestrator may need a fast-path guard — but that guard should be a separate concern, not status-specific branching.
- Orchestrators without validation logic are trivial to test (only call-count, reference, token, and ordering tests needed).

### Related ADRs and Patterns

- Phase 7, Phase 8 in project evolution
- Thin Orchestrator pattern
- Stable Contract pattern

---

## Lesson 019 — Remove Competing Sources of Truth

When a richer validated lifecycle artifact supersedes an earlier intermediate result, migrate the orchestration boundary to the new contract and remove the old contract rather than maintaining competing sources of truth.

### Context

IranDirect had two types representing the output of a read-only runtime cycle: `RuntimeCycleResult` (original) and `RuntimeDecision` (new, richer). Both existed in committed code. `RuntimeCycleCoordinator` returned `RuntimeCycleResult` while `RuntimeDecisionBuilder` produced `RuntimeDecision`.

The coordinator's internal flow matched the builder's flow identically — it called the same plan coordinator and reconciler, but stopped at `RuntimeCycleResult` instead of continuing through execution planning and validation into `RuntimeDecision`. This meant:

- Two types answering "what happened in the last cycle"
- No validation of the pre-execution contract in the coordinator's output
- No execution planning or traceability guarantee
- Future coordinator consumers would need to reach into the builder directly or trust incomplete state

### Insight

A competing source of truth creates ambiguity about which contract consumers should depend on. The older contract loses value as the richer one proves stable. Migration is the simplifying direction — not coexistence or adapters.

The migration itself is small when preceded by good boundaries:

1. `RuntimeDecisionBuilder` existed as the authoritative lifecycle composition.
2. `RuntimeCycleCoordinator` needed only to change its constructor dependency and return type.
3. `RuntimeCycleResult` had zero production consumers beyond the coordinator.
4. No adapter was needed — no external consumer depended on `RuntimeCycleResult`.

### Project Example

- `RuntimeCycleResult` deleted (3 files changed: coordinator, tests, definition).
- `RuntimeCycleCoordinator` now delegates entirely to `IRuntimeDecisionBuilder`.
- No compatibility shim. No deprecation period. No dead code.
- All tests pass (171).

### Broader Application

When a new contract fully covers the responsibilities of an older contract:

1. Verify the old contract has no external consumers beyond the migration boundary.
2. Migrate the boundary.
3. Remove the old contract.
4. Do not add adapters that convert old → new unless a real backward-compatibility contract exists (IPC wire format, public API, plugin contract).

A deprecation period is valuable when the old type is widely consumed or serialized. When it is contained within a single component boundary, deletion is cleaner.

### Tradeoffs

- Requires confidence that no consumer depends on the old type.
- If the old type is embedded in a serialization contract, removal requires version negotiation.
- Deleting a type is psychologically harder than marking it obsolete — but dead code has a maintenance cost.
- The migration window must be narrow; competing truths that persist across multiple milestones create drift.

### Related ADRs and Patterns

- Phase 9 in project evolution
- Thin Orchestrator pattern
- Stable Contract pattern
