# IranDirect AI Session Protocol

## Purpose

This protocol defines how a developer and an AI assistant cooperate safely and consistently across sessions.

It is a durable process document, not a one-time prompt.

The protocol exists to ensure that every session:

- starts from verified evidence;
- preserves architectural direction;
- introduces one small change at a time;
- keeps the developer in control;
- captures durable architectural knowledge;
- ends with a reproducible handoff.

---

## 1. Roles

### Developer

The developer remains the final authority for:

- accepting architectural decisions;
- applying repository changes;
- executing commands on the Windows machine;
- approving runtime mutation tests;
- committing and pushing changes;
- deciding when a milestone is complete.

### AI Assistant

The AI assistant acts as:

- architecture reviewer;
- implementation planner;
- mentor;
- code and patch author;
- test-design partner;
- documentation assistant;
- risk and drift detector.

The AI assistant must not claim authority it does not possess.

---

## 2. Access Levels

Every session begins by declaring one access level.

### Level A — Direct Checkout and Terminal

The AI can directly inspect:

- the checked-out repository;
- current branch and commit;
- working-tree state;
- build and tests;
- local files;
- runtime evidence.

The AI verifies the environment itself.

### Level B — Repository Plus Local Snapshot

The AI can inspect committed repository content and receives a recent:

```text
AI-LOCAL-STATE.md
```

The AI compares:

- committed repository truth;
- local checkout and runtime evidence.

This is the preferred remote-chat workflow.

### Level C — Repository Only

The AI can inspect committed repository content but has no local-state evidence.

The AI may:

- review architecture;
- review committed source;
- plan the next milestone;
- identify likely files and risks.

The AI must not:

- claim the local checkout is verified;
- claim build or tests pass locally;
- claim service or route state;
- begin mutation-sensitive implementation without fresh local evidence.

---

## 3. Required Boot Sequence

Every session follows this order.

1. Determine access level.
2. Read repository root `AI-START-HERE.md`.
3. Read `AI/CURRENT.md`.
4. Read `AI/SESSION-PROTOCOL.md`.
5. Read only task-relevant AKB documents.
6. Inspect task-relevant source and tests.
7. Read `AI-LOCAL-STATE.md` when operating at Level B.
8. Compare committed and local evidence.
9. Report drift, uncertainty, or blockers.
10. Summarize the current architecture and immediate milestone.
11. Do not implement until the summary is grounded.

---

## 4. Source-of-Truth Boundaries

### Committed Source and Tests

Authoritative for:

- what is implemented;
- current contracts;
- current test coverage;
- committed behavior.

### Accepted ADRs

Authoritative for:

- accepted architectural intent;
- rationale;
- tradeoffs;
- constraints.

### AI/CURRENT.md

Authoritative for:

- committed executive architecture summary;
- immediate committed milestone direction;
- known architectural debt.

### AI-LOCAL-STATE.md

Authoritative only for the local machine at its generated timestamp:

- local branch;
- local commit;
- working tree;
- local build and tests;
- service/process state;
- selected runtime evidence.

### AI-EVIDENCE

Supporting diagnostic evidence only.

Evidence never establishes ownership by itself.

---

## 5. Session Start — Developer Actions

Before opening or continuing a remote AI chat:

```powershell
cd C:\codespace\IranDirect

git pull
git status --short

Get-Process IranDirect.Service -ErrorAction SilentlyContinue |
    Stop-Process -Force

.\tools\update-ai-local-state.ps1 `
    -CurrentMilestone "<current milestone>" `
    -NextMilestone "<next milestone>"
```

Then:

1. confirm the snapshot timestamp is current;
2. upload `AI-LOCAL-STATE.md`;
3. provide the compact continuation prompt;
4. provide evidence files only if requested.

Do not upload all evidence files by default.

---

## 6. Session Start — AI Actions

The AI begins with:

### Access Declaration

Example:

```text
Access level: B — committed repository plus local snapshot.
```

### Grounded Repository Summary

The AI reports:

- committed branch and relevant commit;
- local branch and commit from the snapshot;
- whether they match;
- working-tree state;
- build and test result;
- current architecture;
- immediate next milestone;
- documentation or architecture drift;
- risks or missing evidence.

### Stop Condition

The AI does not implement when:

- local and committed state conflict without explanation;
- build or tests fail unexpectedly;
- the working tree contains unexplained changes;
- the requested change violates an invariant;
- the current milestone is ambiguous;
- required source or ADR context is unavailable.

---

## 7. Architecture Review Gate

Before implementation, answer these questions.

1. What responsibility is entering or changing?
2. Who owns it?
3. What existing component becomes smaller?
4. What source of truth does it use?
5. What side effects does it perform?
6. Which invariant must never break?
7. What behavior must remain unchanged?
8. How will the change be verified?
9. Is the abstraction earning its cost?
10. Does the change fit the current milestone?

If these questions cannot be answered clearly, do not write code yet.

---

## 8. Milestone Design Rules

Each milestone should be:

- architecturally coherent;
- small enough to review;
- independently testable;
- safe to revert;
- narrow in file scope;
- explicit about unchanged behavior.

Preferred sequence:

```text
Vocabulary
→ pure domain logic
→ tests
→ read-only wiring
→ execution contract
→ controlled runtime activation
```

Avoid:

- large rewrites;
- unrelated cleanup;
- introducing multiple new authorities;
- mixing planning and execution;
- performing real route mutation during structural validation;
- adding abstractions without a current responsibility.

---

## 9. Implementation Loop

The normal collaboration loop is:

```text
Architecture review
→ agree on one slice
→ AI prepares patch or precise edits
→ developer applies changes
→ build
→ tests
→ static diff review
→ controlled runtime verification
→ architecture review
→ commit
→ push
→ regenerate local snapshot
```

The AI stops after one coherent slice unless the developer explicitly approves continuation.

---

## 10. Verification Commands

After applying a code change:

```powershell
dotnet clean
dotnet build
dotnet test

git diff --check
git status --short
git diff --stat
```

For runtime-sensitive changes, add only the minimum safe verification required.

Never test all 1,944 routes when a controlled one-prefix fixture can prove the structural behavior.

Always restore runtime fixtures after testing.

---

## 11. Runtime Safety Rules

1. VPN endpoint protection is established before prefix routes.
2. A blocked or invalid plan performs no infrastructure mutation.
3. Disable removes only explicitly owned prefix routes.
4. Endpoint protection lifecycle remains independent of prefix enablement.
5. Diagnostic route discovery does not establish ownership.
6. Platform mutations occur only in the Windows Service.
7. Structural tests use minimal controlled fixtures where possible.
8. Existing VPN connectivity must be tested before and after mutation-sensitive milestones.

---

## 12. Milestone Completion Gate

A milestone is complete only when applicable requirements pass.

### Implementation

- build passes;
- tests pass;
- diff is clean;
- no unexpected files changed;
- runtime behavior is verified;
- fixtures are restored.

### Architecture

- ownership is clear;
- invariants remain true;
- controller responsibility does not grow;
- planning remains pure;
- observation remains read-only;
- execution remains focused;
- remaining debt is recorded.

### Documentation

Update only when architecturally meaningful:

- `AI/CURRENT.md`;
- Architecture Journal;
- ADRs;
- project evolution;
- pattern library;
- mental models;
- reading map;
- architecture debt.

### Repository

- helper scripts removed unless intentionally retained;
- changes committed;
- branch pushed;
- local snapshot regenerated after the final commit.

---

## 13. End-of-Session — Developer Actions

After the final milestone commit:

```powershell
git push

.\tools\update-ai-local-state.ps1 `
    -CurrentMilestone "<current milestone>" `
    -NextMilestone "<next milestone>"
```

Confirm:

- branch is correct;
- snapshot references the final commit;
- working tree is clean;
- build passes;
- tests pass.

Preserve `AI-LOCAL-STATE.md` for the next session.

---

## 14. End-of-Session — AI Actions

The AI provides a compact handoff:

### Completed

What changed and what responsibility moved.

### Verification

Build, tests, runtime checks, and important evidence.

### Architecture

What became smaller, clearer, or safer.

### Remaining Debt

What is intentionally not solved yet.

### Next Milestone

One immediate next architectural slice.

### AKB Updates

Which durable knowledge artifacts were updated or should be updated.

---

## 15. Practical Division of Work

### AI Should Do

- inspect committed architecture;
- compare snapshot evidence;
- identify drift;
- propose small milestones;
- write patches;
- explain tradeoffs;
- design tests;
- review command output;
- update durable documentation;
- stop when evidence is insufficient.

### Developer Should Do

- run local commands;
- protect real machine state;
- apply patches;
- inspect generated changes;
- approve runtime tests;
- report exact output;
- commit and push;
- regenerate snapshots;
- challenge architectural proposals.

### Both Should Do

- review ownership;
- verify invariants;
- keep milestones small;
- identify architecture debt;
- preserve the AKB;
- disagree constructively when a simpler design may exist.

---

## 16. Session Contract

The assistant must explicitly state what evidence supports its conclusions.

The assistant must never claim local verification without Level A access or a fresh Level B snapshot.

The developer must not treat AI-generated patches as trusted until build, tests, diff review, and runtime verification succeed.

Neither side should optimize for speed at the expense of VPN safety or architectural clarity.

---

## 17. Core Question

Before every significant change, ask:

> What responsibility is entering the system, who owns it, and what invariant must never break?