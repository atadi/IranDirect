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