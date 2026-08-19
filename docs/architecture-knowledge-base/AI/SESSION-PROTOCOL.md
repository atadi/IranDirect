# PathVeer AI Session Protocol

## Purpose

This durable protocol defines how developers and AI assistants cooperate across PathVeer engineering sessions.

Current milestone/status belongs in `AI/CURRENT.md`. Machine state belongs in `AI-LOCAL-STATE.md`. Durable project invariants belong in root `AI-START-HERE.md`.

The protocol exists so sessions start from evidence, preserve authority/safety boundaries, make one coherent change at a time, distinguish proof levels, and end in a reproducible state.

---

## 1. Roles

### Developer / Operator

Final authority for product direction, significant architectural decisions, destructive actions, production credentials/publication, human-only Windows actions, and final release approval.

### AI Assistant / Engineering Agent

May act as investigator, architect/reviewer, implementation/test author, release-engineering reviewer, documentation maintainer, runtime-evidence analyst, and—when explicitly authorized with direct access—ordinary Git operator.

The AI must not claim evidence or authority it does not possess.

---

## 2. Access Levels

### Level A — Direct checkout and terminal

AI can inspect and, when authorized, edit/build/test the checked-out repository and inspect local runtime evidence.

### Level B — Repository plus fresh local snapshot

AI can inspect committed source plus a recent `AI-LOCAL-STATE.md` or equivalent operator evidence. Runtime claims are limited to what that evidence proves.

### Level C — Repository only

AI may review committed architecture/source/tests and prepare plans/patches, but must not claim current local checkout/build/test/Service/Tray/route state.

### Level D — Conversation/evidence only

AI reasons from supplied logs, source excerpts, screenshots, command output, and uploaded files. Request the smallest additional evidence needed to distinguish hypotheses.

---

## 3. Boot Sequence

1. determine access level;
2. read `AI-START-HERE.md`;
3. read `AI/CURRENT.md`;
4. read this protocol;
5. verify repository state when possible;
6. read `AI-LOCAL-STATE.md` when relevant;
7. load only task-relevant source/tests/ADRs/docs;
8. compare evidence with `CURRENT.md`;
9. report material drift/blockers;
10. state responsibility, owner, source of truth, invariant, and scope;
11. implement only after the boundary is grounded.

Do not recursively load the whole knowledge base.

---

## 4. Repository Verification

With direct access:

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git log -1 --oneline
git branch -vv
git rev-list --left-right --count HEAD...@{upstream}
```

Do not automatically `git pull`. Inspect branch/HEAD/upstream/ahead-behind/working-tree first.

Do not automatically stop PathVeer/IranDirect/VPN processes at session start; runtime state may be diagnostic evidence.

---

## 5. Dirty Working Tree

Inspect and preserve unexplained changes. Never automatically use destructive reset/clean/restore or overwrite user/prior-agent work.

If local changes make the task unsafe or ambiguous, stop and report the conflict.

---

## 6. Evidence Vocabulary

Use precise labels:

- **source-inspected** — code read;
- **unit-tested** — isolated tests passed;
- **integration-tested** — multiple components exercised in a controlled test;
- **artifact-tested** — actual built binary/package exercised;
- **operator-tested** — required human GUI/UAC/reboot/etc. action performed;
- **runtime-proven** — direct process/event/registry/hash/service/etc. evidence.

"By construction" may describe a logical implementation property, but it does not replace a required live Windows acceptance step.

---

## 7. Session Opening Summary

Before implementation report:

### Verified Repository State
branch, HEAD, upstream, ahead/behind, working tree—only to the level actually verified.

### Current Milestone
current goal/blocker/subsystem from `CURRENT.md` plus current evidence.

### Responsibility Boundary
responsibility, owner, source of truth, invariant, and intentionally untouched behavior.

### Evidence Gaps
only gaps that materially affect the next decision.

---

## 8. Architecture Review Gate

Before a non-trivial change answer:

1. What responsibility changes?
2. Who owns it?
3. Is that the correct authority?
4. What is the source of truth?
5. What side effects occur?
6. What starts and terminates the lifecycle?
7. What invariant must never break?
8. Which failure modes matter?
9. What must remain unchanged?
10. What regression test reproduces the original boundary?
11. Does the abstraction earn its cost?
12. Does it fit the current milestone?

Continue investigation if these cannot be answered sufficiently.

---

## 9. Investigation Protocol

For a defect:

1. capture exact symptom/evidence;
2. locate the failing boundary;
3. inspect the smallest relevant source path;
4. gather evidence that separates competing hypotheses;
5. state root cause only to the level proven;
6. implement the smallest correct repair.

Prefer process trees, Event Log, exit codes, hashes, actual artifact contents, registry/Service state, and focused reproduction over speculative edits.

Preserve the failing runtime state until decisive evidence is collected.

---

## 10. Implementation Loop

When authorized:

```text
inspect
-> establish baseline
-> reproduce/prove defect
-> root cause
-> smallest fix
-> focused regression
-> affected tests
-> adversarial review
-> diff review
-> required runtime/artifact proof
-> commit
-> push
-> synchronization check
```

Do not create unnecessary confirmation loops for ordinary authorized engineering work.

Stop for genuinely human-only or protected operations.

---

## 11. Verification Strategy

Use progressive verification.

During iteration:

```text
specific regression
-> affected test class
-> affected project
-> relevant subsystem
```

At milestone/release closure, run broader suites required by `CURRENT.md` and the certification harness.

Do not run `dotnet clean` ritually. Use it for actual clean/reproducible release gates, project/SDK changes, or stale-output investigation.

A large suite is not a substitute for a regression test at the failing boundary.

Do not call a test flaky without identifying/reproducing it.

---

## 12. Runtime Safety

1. VPN endpoint safety has priority over prefix routing.
2. The Service is the only machine route-mutation authority.
3. Unsafe/blocked plans do not mutate infrastructure.
4. PathVeer removes only resources it identifies as owned.
5. Observation/diagnostics are read-only unless explicitly designed otherwise.
6. Planning remains deterministic/side-effect free.
7. Reconciliation remains idempotent.
8. CLI/Tray express intent, not direct routing mutation.
9. Prefer the smallest safe runtime fixture or disposable VM capable of proving the claim.

---

## 13. Long-Running Operation Contract

Every long-running workflow defines:

```text
producer
consumer
progress
completion signal
success/failure/cancellation
timeout
cleanup
final result
```

Never accept designs where producer completion leaves a consumer polling indefinitely, backend success leaves the UI permanently Working, or failure returns Success.

For streamed progress:

```text
producer runs
-> consumer streams
-> producer completes
-> consumer drains final records
-> consumer terminates
-> final result processed
```

A defensive timeout must not become normal lifecycle control.

---

## 14. Service / Tray Boundary

Service: machine-scoped, authoritative, may run without Tray.

Tray: per-user/session UI/controller, normally non-elevated.

Validate when relevant:

```text
0 -> first Tray launch -> 1
10 duplicates -> still 1
Repair -> still 1
second Repair -> still 1
exit -> 0
relaunch -> 1
```

Closing Tray must not stop Service. Installer/autorun/Start Menu must not create competing Tray processes.

---

## 15. Windows Installer Certification

Unit tests do not certify every installer boundary. Exercise the actual artifact when applicable.

Validate:

- PE subsystem, icon, embedded manifest, provenance;
- Explorer/UAC/self-elevation and single-installer behavior;
- package integrity and safe paths;
- Authenticode and metadata trust;
- install classification, Service transition/readiness, binary swap;
- PATH, Start Menu, Apps & Features, legacy shim, Tray autorun;
- terminal UI state and process exit code;
- same-version Repair;
- versioned Upgrade;
- IranDirect legacy migration when an authentic artifact exists;
- uninstall and reboot persistence.

Never substitute source/unit proof for an operator-only gate.

---

## 16. Artifact Immutability

Once a disposable certification artifact has been tested, preserve it as evidence.

```text
devsign.N
-> defect
-> source fix/tests/commit/push
-> devsign.N+1
```

Do not rebuild `devsign.N` with different bytes.

Artifact provenance must identify the exact committed source used to build it.

Frozen release artifacts follow the stricter invariant in `CURRENT.md`/`AI-START-HERE.md`.

---

## 17. Package Integrity / Trust

Internal hashes prove final distributed bytes; Authenticode and signed release metadata prove authenticity according to separate trust contracts.

Signing changes PE bytes, so package hashes must describe post-signing bytes.

Production trust must remain fail closed and isolated from development/staging override mechanisms.

Certification does not authorize publication.

---

## 18. Git Protocol

Unless explicitly authorized:

```text
NO worktrees
NO rebase
NO force push
NO destructive reset/history rewrite
```

Before commit inspect:

```powershell
git diff --check
git status --short
git diff --stat
git diff
```

Check for unrelated edits, secrets, debug residue, weakened tests, and generated artifacts.

After push:

```powershell
git status --short
git rev-parse HEAD
git rev-parse @{upstream}
git rev-list --left-right --count HEAD...@{upstream}
```

A completed committed milestone normally ends clean and `0/0` ahead/behind.

---

## 19. Human-Only Gates

Stop for actions the AI cannot safely perform, such as UAC confirmation, Explorer/GUI interaction, physical reboot/login, unavailable credentials/private signing material, irreversible publication, or production actions.

When requesting operator evidence:

1. explain why;
2. give the minimal exact step/command;
3. state expected output;
4. wait for actual evidence;
5. resume from it.

Prefer one decisive gate at a time.

---

## 20. Adversarial Review

Before closure, attack the changed boundary with relevant cases:

- path variations/traversal/rooted/missing;
- producer exit/hang/cancellation/missing result;
- fresh/same/older/newer/partial/legacy install states;
- duplicate Tray launches/process crash/elevation;
- wrong signer/unsigned/wrong metadata key/corrupt package.

Use only cases relevant to the responsibility under change.

---

## 21. Documentation Updates

Update `AI/CURRENT.md` when the blocker, milestone, or certification artifact meaningfully changes.

Update ADRs for accepted architecture decisions, evolution/history for completed architecture phases, and certification records for durable artifact/runtime proof.

Do not put transient debugging history into `AI-START-HERE.md`.

---

## 22. Local-State / Remote Handoff

When useful, generate:

```powershell
.\tools\update-ai-local-state.ps1
```

`AI-LOCAL-STATE.md` should remain Gitignored and timestamped.

When direct checkout/repository access is unavailable and the repo supports it, use:

```powershell
.\tools\export-ai-context.ps1
```

A stale snapshot/bundle is not current runtime evidence.

---

## 23. Completion Report

Report:

### Result
`PASS`, `FAIL`, or `READY FOR OPERATOR GATE`.

### Root Cause
Only what was proven.

### Change
Responsibility/files/untouched areas.

### Verification
Separate unit, integration, artifact, and runtime/operator evidence.

### Git
Commit/branch/upstream/ahead-behind/working tree when applicable.

### Remaining Gates
Anything not yet proven.

### Next Milestone
One immediate next engineering slice.

Do not hide an open operator/artifact gate inside a successful-looking summary.

---

## 24. Stop Conditions

Stop and report when:

- unexplained user work would be overwritten;
- requested behavior violates a non-negotiable invariant;
- destructive/production action lacks authorization;
- production credentials are required but unavailable;
- architecture authority is ambiguous;
- evidence disproves the current hypothesis;
- a human-only gate is reached.

Do not stop merely because another ordinary authorized edit/test/commit is needed.

---

## 25. Core Question

> What responsibility is changing, who owns it, what is the source of truth, what terminates its lifecycle, and what invariant must never break?
