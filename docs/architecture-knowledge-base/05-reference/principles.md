# Architecture Principles

1. VPN safety has absolute priority.
2. One mutable resource has one authority.
3. Configuration, observation, planning, and execution are separate concerns.
4. Diagnostics are read-only.
5. Ownership is explicit.
6. Reconciliation is idempotent.
7. Planners are pure.
8. Executors are focused.
9. Orchestrators are thin.
10. Contracts should be stable and implementations replaceable.
11. Changes are introduced in small, testable milestones.
12. Unsafe ambiguity results in no mutation.