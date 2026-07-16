# Architecture Review Checklists

## Before Adding a New Component

- What single responsibility does it own?
- What is its source of truth?
- Is it domain logic, orchestration, or platform adaptation?
- Does it perform side effects?
- Can it be tested independently?
- Which invariant does it protect?
- Is an existing component the proper owner?

## Before Adding a Model Property

- Is the value desired, observed, computed, or persisted?
- Who owns the value?
- Is it stable domain vocabulary?
- Does it expose a platform implementation detail?
- Does it belong in configuration, runtime, inventory, or diagnostics?

## Before Adding Controller Logic

- Is this orchestration or a business decision?
- Should the planner own the decision?
- Should a reconciler own the execution?
- Should an adapter own the platform operation?
- Can the controller remain readable on one screen?

## Before Mutating Infrastructure

- Is the desired state valid?
- Are all safety blockers cleared?
- Is the resource already correct?
- Is the resource owned by IranDirect?
- Can the change be verified?
- Can failure leave the system safe?