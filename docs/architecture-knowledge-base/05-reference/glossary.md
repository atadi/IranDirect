# Glossary

## Desired Configuration
The durable expression of user intent.

## Requested State
The state the user/API has asked PathVeer to reach.

## Effective State
The state PathVeer can safely apply after validation, availability, and safety constraints.

## Observed Runtime
A read-only snapshot of actual platform facts.

## Desired Runtime
The infrastructure that should exist now, given configuration and observation.

## Runtime Plan
A coherent snapshot containing configuration, observation, and desired runtime.

## Reconciliation
The deterministic comparison of desired and observed runtime that produces the minimal required change set. Reconciliation itself does not perform Windows mutation.

## Inventory
A durable record of resources explicitly owned by PathVeer, including recognized legacy ownership where compatibility contracts require it.

## Blocker
A condition that prevents safe reconciliation/execution.

## Orchestrator
A component that sequences collaborators without owning their business decisions.

## Execution Plan
An ordered, immutable sequence of execution steps derived from a reconciled change set.

## Execution Step
A single unit of platform work with a defined kind, route fields, ordering constraints, and verification requirement.

## Execution Result
The outcome of executing an execution plan, including plan-level status and per-step results.

## Executor
A component that performs the platform mutations described by an execution plan through the appropriate platform handlers/adapters, returning explicit execution results.

## Verifier
Logic that confirms actual platform state matches the expected post-mutation state before ownership/persistence is advanced.

## Runtime Decision
The validated pre-execution contract containing the runtime plan snapshot, reconciliation result, and ordered execution plan.

## Runtime Decision Builder
A read-only orchestrator that composes planning, reconciliation, execution planning, and timestamp assignment into a validated `RuntimeDecision`, delegating domain validation to the domain contract.

## Runtime Cycle Result (removed)
An earlier intermediate container for plan snapshot and reconciliation result. It was superseded by `RuntimeDecision` and removed after coordinator migration.

## Machine Authority
The component permitted to mutate machine-level managed routing state. In PathVeer this is the Windows Service.

## Control Surface
A user-facing component such as CLI or Tray that expresses intent to the Service rather than independently mutating routing.

## Legacy Compatibility Identity
An IranDirect-era name/schema/service/IPC/state identifier intentionally retained so upgrade, rollback, persisted state, or mixed-version compatibility remains safe.
