# Architecture Journal

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

### IranDirect Example

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