# IranDirect AI Start Here

This file is the canonical bootstrap document for AI assistants and new contributors continuing IranDirect development.

Do not begin implementation until the repository state and required architecture documents have been reviewed.

---

## 1. Repository Is the Source of Truth

The checked-out repository is authoritative for the current implementation.

Before making assumptions:

1. Verify the active branch.
2. Verify the current commit.
3. Verify the working tree.
4. Build the solution.
5. Run the automated tests.
6. Compare documentation with the current source.

If this document, an old chat, or another summary conflicts with the current repository:

- use tested source code to determine what is currently implemented;
- use accepted ADRs to determine the intended architectural direction;
- report architecture or documentation drift explicitly;
- do not silently reinterpret conflicting information.

---

## 2. Required Initial Commands

Run these commands from the repository root:

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git log -1 --oneline

dotnet build
dotnet test
```

Do not proceed with implementation if:

- the branch is unexpected;
- the working tree contains unexplained changes;
- the solution does not build;
- tests fail.

First summarize the repository condition and resolve or explicitly account for the discrepancy.

---

## 3. Expected Development Branch

The active development branch has historically been:

```text
development/service-authority
```

This is not a permanent assumption.

Always verify the current branch and its upstream before continuing:

```powershell
git branch --show-current
git branch -vv
git status
```

Never switch branches, reset changes, delete files, or rewrite history without explicit user approval.

---

## 4. Required Reading Order

Read the following documents in order.

### Required for Every New Session

1. `AI-START-HERE.md`
2. `docs/architecture-knowledge-base/README.md`
3. `docs/architecture-knowledge-base/04-projects/IranDirect/README.md`
4. `docs/architecture-knowledge-base/04-projects/IranDirect/evolution.md`
5. `docs/architecture-knowledge-base/05-reference/principles.md`
6. `docs/architecture-knowledge-base/05-reference/checklists.md`

### Read When Present

The following AI workspace documents may be added under:

```text
docs/architecture-knowledge-base/AI/
```

Read them in this order:

1. `collaboration-contract.md`
2. `project-state.md`
3. `session-handoff.md`
4. `roadmap.md`
5. `architecture-debt.md`
6. `engineering-standards.md`
7. `definition-of-done.md`

### Task-Specific Reading

Read only the ADRs, patterns, journal entries, source files, and tests relevant to the immediate milestone.

Do not load the entire knowledge base without a reason. Prefer focused, task-relevant context.

---

## 5. Authority and Precedence

Use this precedence when information conflicts.

### Current Implementation Facts

1. Current checked-out source code
2. Automated tests
3. Current runtime evidence supplied by the user
4. `project-state.md`
5. `session-handoff.md`
6. Roadmap and historical summaries

### Architectural Intent

1. Accepted ADRs
2. Architecture principles and invariants
3. Current project architecture documentation
4. Patterns and mental models
5. Architecture Journal
6. Old chat summaries

Code explains what currently exists.

ADRs explain why an architectural direction was accepted.

If current code conflicts with an accepted ADR, report:

```text
Architecture drift detected.
```

Then identify:

- the conflicting implementation;
- the accepted architectural decision;
- whether the implementation should be corrected;
- or whether the ADR should be superseded.

---

## 6. Project Mission

IranDirect is a Windows network control-plane application.

Its primary purpose is:

- bypass OpenVPN for Iranian IPv4 prefixes;
- send Iranian traffic through the ISP gateway;
- send other traffic through the VPN;
- guarantee that VPN endpoint connectivity is never broken by IranDirect routing.

VPN safety has absolute priority.

If IranDirect cannot safely determine or protect the VPN endpoint, it must not install direct-routing prefixes.

---

## 7. Current Architectural Direction

IranDirect is evolving from imperative commands toward a declarative control loop.

Current direction:

```text
DesiredConfiguration
        |
        v
RuntimeCoordinator
   |             |
   v             v
RuntimeObserver  RuntimePlanner
   |             |
   v             v
ObservedRuntime  DesiredRuntime
        \       /
         \     /
      RuntimePlanSnapshot
              |
              v
     RuntimeReconciler
              |
              v
 Windows Networking Platform
```

`RuntimeReconciler` is the next major architectural responsibility unless the repository shows that it has already been implemented.

---

## 8. Non-Negotiable Invariants

Every implementation must preserve these invariants.

1. The Windows Service is the only authority allowed to mutate routes.
2. CLI and Tray express intent through IPC.
3. VPN endpoint reachability has priority over prefix routing.
4. IranDirect removes only routes it explicitly owns.
5. Diagnostics are read-only.
6. Observation reports facts and performs no planning.
7. Planning is deterministic and side-effect free.
8. Reconciliation is idempotent.
9. Unsafe or blocked plans must not mutate infrastructure.
10. Configuration, observation, desired runtime, actual runtime, and inventory are distinct concepts.
11. Controllers and coordinators remain thin.
12. Platform-specific behavior remains behind adapters.
13. Changes are introduced in small, reviewable, testable milestones.
14. No large rewrite is permitted without an explicit architecture review and user approval.

---

## 9. Working Method

Use this sequence for each milestone.

### Before Implementation

1. Verify repository state.
2. State the immediate architectural goal.
3. Identify the new or changing responsibility.
4. Identify its owner.
5. Identify the invariant being protected.
6. Identify files likely to change.
7. Identify behavior that must not change.
8. Review relevant tests and ADRs.

### During Implementation

1. Keep the change small.
2. Prefer pure domain logic before wiring side effects.
3. Add or update automated tests.
4. Avoid unrelated cleanup.
5. Do not modify real routing during structural tests.
6. Preserve a clean migration path from the current design.

### After Implementation

Run:

```powershell
dotnet clean
dotnet build
dotnet test
git diff --check
git status --short
git diff --stat
```

Then perform only the necessary runtime validation.

Do not commit until:

- the build succeeds;
- tests pass;
- runtime behavior is verified where required;
- unexpected file changes are explained.

---

## 10. Definition of Done

A milestone is complete only when applicable items are satisfied.

### Product

- Code compiles.
- Automated tests pass.
- Existing behavior is preserved unless intentionally changed.
- VPN safety is verified.
- The change is idempotent where relevant.
- No unrelated changes are included.

### Architecture

- Responsibility and ownership are clear.
- New coupling is justified.
- Contracts remain stable where practical.
- Architectural debt is reduced or explicitly recorded.
- The controller does not absorb new business logic.

### Knowledge Base

- Architecture Journal updated for transferable lessons.
- ADR created or updated for significant decisions.
- Pattern documented when reusable beyond IranDirect.
- Project evolution updated for meaningful structural changes.
- Reading map updated when a new concept is introduced.
- Mental model added when it materially improves understanding.
- Architecture debt updated when debt remains.

Not every small code change requires every document.

Only create durable knowledge artifacts for information expected to remain useful over time.

---

## 11. Documentation Rules

Use the Architecture Knowledge Base for durable architectural knowledge.

Do not use it for:

- transient build errors;
- typo fixes;
- routine package updates;
- temporary commands;
- low-level implementation history already captured by Git.

Document:

- decisions;
- context;
- alternatives;
- tradeoffs;
- invariants;
- reusable principles;
- mental models;
- architectural evolution.

Apply the ten-year test:

> Will this still be useful to an architect ten years from now?

If not, it probably belongs in Git history, an issue, or a temporary session handoff—not the permanent AKB.

---

## 12. Architecture Compass

Evaluate significant proposals with these questions:

- Does this clarify ownership?
- Does this increase cohesion?
- Does this reduce coupling?
- Does this improve testability?
- Does this preserve stable contracts?
- Does this simplify orchestration?
- Does this isolate side effects?
- Does this improve observability?
- Does this preserve VPN safety?
- Does this reduce architectural debt?
- Is the abstraction earning its cost?
- Will the design remain understandable as the project grows?

Do not accept a design merely because it is elegant or fashionable.

Prefer the smallest architecture that can evolve safely.

---

## 13. Mentorship and Collaboration Contract

The collaboration has two simultaneous goals:

1. Build IranDirect to production quality.
2. Develop transferable architectural judgment.

When proposing architecture:

- explain why;
- explain tradeoffs;
- explain when the approach should not be used;
- relate it to IranDirect;
- distinguish principle from implementation;
- invite architectural challenge and disagreement.

Do not teach pattern memorization.

Derive architecture from:

- responsibilities;
- ownership;
- invariants;
- sources of truth;
- expected change;
- failure modes;
- operational constraints.

The goal is not dependency on the assistant.

The goal is for the developer to independently recognize and design appropriate system boundaries.

---

## 14. New-Session Bootstrap Response

After reading the required files and verifying the repository, begin with a concise grounded summary containing:

### Verified Repository State

- branch;
- commit;
- working-tree status;
- build result;
- test result.

### Current Architecture

- current control flow;
- implemented responsibilities;
- remaining responsibility still located in the wrong component.

### Immediate Next Milestone

- goal;
- proposed scope;
- files or components likely affected;
- what will explicitly remain unchanged.

### Risks or Drift

- documentation drift;
- ADR conflicts;
- uncommitted work;
- unsafe runtime assumptions;
- missing tests.

Do not begin implementation until this summary is grounded in the repository.

---

## 15. Current Expected Next Direction

At the time this bootstrap file was introduced, the expected next direction was:

```text
Introduce RuntimeReconciler.
```

The reconciler should consume `RuntimePlanSnapshot`, compare desired and observed runtime, and apply minimal verified changes.

The long-term target is a controller that reads approximately as:

```csharp
RuntimePlanSnapshot plan =
    await coordinator.BuildPlanAsync(cancellationToken);

RuntimeReconciliationResult result =
    await reconciler.ReconcileAsync(
        plan,
        cancellationToken);
```

The controller should not own:

- profile validation;
- endpoint planning;
- route calculations;
- route differences;
- Windows command construction;
- inventory decisions;
- diagnostics rules.

Verify the repository before assuming this work remains outstanding.

---

## 16. First Principle

When uncertain, return to this question:

> What responsibility is entering the system, who should own it, and what invariant must never break?

That question takes precedence over framework conventions and design-pattern labels.
---

## Local-State Snapshot

A public or connected repository cannot prove the developer's current checkout
or Windows runtime state.

Before implementation, obtain one of:

1. direct terminal access to the checked-out repository; or
2. a freshly generated `AI-LOCAL-STATE.md`.

Generate it from the repository root:

```powershell
.\tools\update-ai-local-state.ps1 `
    -CurrentMilestone "M5.5 Runtime Reconciliation" `
    -NextMilestone "Introduce RuntimeReconciler"
```

`AI-LOCAL-STATE.md` is intentionally ignored by Git because it contains
machine-specific and time-sensitive evidence.

Use separate authorities:

- committed repository: implementation and architectural knowledge;
- `AI-LOCAL-STATE.md`: current checkout, build, tests, services, and runtime;
- accepted ADRs: architectural intent.

Do not ask the user to paste many individual command outputs when a recent
local-state snapshot is available.