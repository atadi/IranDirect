# Glossary

## Desired Configuration

The durable expression of user intent.

## Observed Runtime

A read-only snapshot of actual platform facts.

## Desired Runtime

The infrastructure that should exist now, given configuration and observation.

## Runtime Plan

A coherent snapshot containing configuration, observation, and desired runtime.

## Reconciliation

The process of comparing desired and observed runtime and applying minimal verified changes.

## Inventory

A durable record of resources owned by IranDirect.

## Blocker

A condition that prevents safe reconciliation.

## Orchestrator

A component that sequences collaborators without owning their business decisions.

## Execution Plan

An ordered, immutable sequence of execution steps derived from a reconciled change set.

## Execution Step

A single unit of platform work with a defined kind (add/remove endpoint/prefix), route fields, and verification requirement.

## Execution Result

The outcome of executing an execution plan, including plan-level status and per-step results.

## Executor

A component that performs platform operations defined by execution steps (future).

## Verifier

A component that confirms platform state matches expected post-execution state (future).

## Runtime Decision

The complete validated pre-execution contract containing the plan snapshot, reconciliation result, and ordered execution plan. Guarantees internal consistency between reconciliation intent and execution steps.