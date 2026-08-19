# Architecture Review Checklists

## Before Adding a New Component

- What single responsibility does it own?
- What is its source of truth?
- Is it domain logic, orchestration, or platform adaptation?
- Does it perform side effects?
- Can it be tested independently?
- Which invariant does it protect?
- Is an existing component the proper owner?
- What starts and terminates its lifecycle?

## Before Adding a Model Property

- Is the value desired, observed, computed, or persisted?
- Who owns the value?
- Is it stable domain vocabulary?
- Does it expose a platform implementation detail?
- Does it belong in configuration, requested/effective state, runtime, inventory, or diagnostics?

## Before Adding Controller Logic

- Is this orchestration or a business decision?
- Should the planner own the decision?
- Should reconciliation own the desired-vs-observed difference?
- Should execution own the platform mutation?
- Should an adapter own the Windows-specific operation?
- Can the controller remain thin and readable?

## Before Mutating Infrastructure

- Is desired state valid?
- Are all safety blockers cleared?
- Is the resource already correct?
- Is the resource explicitly owned by PathVeer or by a recognized legacy compatibility identity?
- Can the change be verified?
- Can failure leave the system safe?
- Is rollback/recovery behavior understood?

## Before Adding a Timeout / Retry / Poller

- What lifecycle event normally terminates the operation?
- Is the timeout defensive or accidentally normal control flow?
- Can producer completion be signaled explicitly?
- Are final records/results drained before termination?
- Can cancellation leave a worker alive?
