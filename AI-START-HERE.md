# PathVeer AI Start Here

This is the canonical bootstrap document for AI assistants and contributors working on PathVeer.

Keep this file durable. It defines project-wide rules, sources of truth, safety boundaries, and navigation. It must not become a changelog, machine-state snapshot, or current-milestone tracker.

Current work belongs in:

`docs/architecture-knowledge-base/AI/CURRENT.md`

Machine-specific evidence belongs in:

`AI-LOCAL-STATE.md`

Execution workflow belongs in:

`docs/architecture-knowledge-base/AI/SESSION-PROTOCOL.md`

---

## 1. Product Identity

PathVeer is the current product identity.

IranDirect is the legacy predecessor. Some IranDirect names intentionally remain for migration, rollback, persisted-data compatibility, IPC compatibility, legacy Service compatibility, compatibility shims, and historical documentation.

Do not rename an IranDirect identifier merely for cosmetic consistency. First classify it as:

1. compatibility contract;
2. persisted schema/data;
3. migration or rollback contract;
4. historical documentation;
5. genuine stale branding.

Only the last category is automatically a branding-cleanup candidate.

---

## 2. Sources of Truth

Use different authorities for different questions.

### Current implementation

1. checked-out source;
2. automated tests;
3. fresh runtime/operator evidence;
4. current committed documentation;
5. historical summaries.

### Current milestone

1. `docs/architecture-knowledge-base/AI/CURRENT.md`;
2. explicit current-session user instructions;
3. current session handoff when present.

### Architectural intent

1. accepted ADRs;
2. non-negotiable invariants;
3. current architecture documentation;
4. evolution/history;
5. old chats and summaries.

### Current machine/runtime state

1. direct terminal evidence;
2. fresh `AI-LOCAL-STATE.md`;
3. operator logs/screenshots.

A committed repository cannot prove current Windows runtime state.

---

## 3. Required Session Bootstrap

Read, in order:

1. `AI-START-HERE.md`
2. `docs/architecture-knowledge-base/AI/CURRENT.md`
3. `docs/architecture-knowledge-base/AI/SESSION-PROTOCOL.md`

Then read only task-relevant source, tests, ADRs, and documentation.

Do not recursively load the entire Architecture Knowledge Base.

With direct checkout access, verify:

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git log -1 --oneline
git branch -vv
git rev-list --left-right --count HEAD...@{upstream}
```

The development branch has historically been:

```text
development/service-authority
```

Always verify it; do not treat it as permanent.

---

## 4. Dirty Working Tree Safety

A dirty working tree is evidence, not something to erase automatically.

If changes exist:

1. inspect them;
2. identify their owner/purpose;
3. preserve user and prior-agent work;
4. do not overwrite unexplained changes;
5. report conflicts that prevent safe implementation.

Do not automatically use destructive `reset`, `clean`, `restore`, branch switching, rebase, or history rewriting.

---

## 5. Product Mission

PathVeer is a Windows network control-plane application.

Its routing purpose is to allow configured country IPv4 traffic to bypass a VPN through the physical ISP gateway while other traffic remains on the VPN path.

The primary safety invariant is:

> VPN endpoint connectivity has priority over direct-prefix routing.

If PathVeer cannot safely determine or protect the VPN endpoint, direct-routing mutation must fail closed.

---

## 6. Core Authority Model

The PathVeer Windows Service is the machine-authoritative routing controller.

```text
CLI ----\
         \
          -> IPC -> PathVeer Service -> observe -> plan -> reconcile -> execute
         /
Tray ---/
```

CLI and Tray are control surfaces, not independent route-mutation authorities.

The installer is a lifecycle/deployment authority, not a routing authority.

---

## 7. Non-Negotiable Architecture Invariants

1. The Windows Service is the only machine authority allowed to mutate managed routes.
2. CLI and Tray express intent through defined IPC/service contracts.
3. VPN endpoint reachability has priority over direct-prefix routing.
4. PathVeer removes only resources it can identify as owned.
5. Observation is read-only.
6. Planning is deterministic and side-effect free.
7. Reconciliation is idempotent.
8. Unsafe or blocked plans do not mutate infrastructure.
9. Configuration, requested state, effective state, observed runtime, desired runtime, and inventory are distinct concepts.
10. Controllers/coordinators remain thin.
11. Platform-specific behavior remains behind explicit adapters.
12. Destructive mutation happens only after required validation.
13. Fail closed when routing safety, package integrity, trust, or authority is uncertain.
14. Changes remain small, reviewable, and testable.
15. No broad rewrite without explicit architecture review and user approval.

---

## 8. Service / Tray Boundary

The Service is machine-scoped and authoritative.

The Tray is per-user UI/controller and should normally run non-elevated.

Therefore:

- closing Tray must not stop the Service;
- repeated Tray launches must converge to one intended instance per interactive session;
- installer/autorun/Start Menu launches must not create competing Tray instances;
- Tray lifecycle must not determine routing authority;
- unnecessary elevation of the Tray is a security/UX defect.

---

## 9. Legacy Compatibility

Known compatibility-sensitive identities may include:

```text
IranDirect.Control.v1
%ProgramData%\IranDirect
iran-ipv4-prefixes.txt
AddedByIranDirect
irandirect.cmd
legacy IranDirect Windows Service identity
legacy migration/rollback state
```

Their existence is not itself technical debt.

Before changing one, identify current readers/writers and evaluate persisted-data, upgrade, rollback, and mixed-version effects.

---

## 10. Installation / Migration Authority

IranDirect -> PathVeer migration must preserve a single Service authority.

Conceptually:

```text
legacy IranDirect authority
-> stop/disable/verify
-> stage and verify PathVeer
-> start PathVeer
-> readiness verification
-> retire/reconcile legacy authority
```

Do not intentionally leave IranDirect and PathVeer operating as competing routing authorities.

Installer state classification should distinguish fresh, PathVeer-present, IranDirect-only migration, and conflicting/partial states.

---

## 11. Release / Certification Invariants

Unless explicitly changed by the user:

- do not publish releases;
- do not modify production release pointers;
- do not modify `R2/latest.json`;
- do not use production private signing keys for development certification;
- keep development and production trust isolated;
- do not overwrite an already-tested certification artifact;
- use a new disposable version after each materially changed certification build;
- do not claim Explorer/UAC/GUI/operator behavior as proven merely "by construction".

Production metadata verification remains fail closed.

---

## 12. Frozen beta.1

`1.0.0-beta.1` is frozen and must not be rebuilt, re-signed, overwritten, modified, or republished.

Expected frozen SHA-256:

```text
7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d
```

Any new candidate after a source/signing change uses a new version.

---

## 13. Git Safety

Unless explicitly authorized:

- no Git worktrees;
- no rebase;
- no force push;
- no history rewriting;
- no destructive reset;
- no automatic branch switching;
- no deleting unexplained files.

For completed committed work, verify after push:

```powershell
git status --short
git rev-parse HEAD
git rev-parse @{upstream}
git rev-list --left-right --count HEAD...@{upstream}
```

Normally require clean tree and `0/0` ahead/behind.

---

## 14. Investigation Before Modification

For a defect:

1. capture exact evidence;
2. identify the failing boundary;
3. identify producer and consumer;
4. inspect the relevant source path;
5. distinguish symptom from root cause;
6. gather evidence that separates competing hypotheses;
7. implement the smallest correct repair.

Prefer direct evidence: exit codes, Event Log, process tree, service state, registry state, hashes, embedded manifests, artifact contents, source history, focused reproductions.

---

## 15. Verification Strategy

Use progressive verification.

During investigation, do not automatically run the largest test suite.

During implementation:

```text
specific regression
-> affected test class
-> affected project
-> relevant subsystem
```

At milestone/release closure, run the broader suites required by `CURRENT.md` and the applicable certification harness.

Do not run `dotnet clean` routinely. Use it when a clean/reproducible build is part of the actual gate or stale-output investigation.

Do not dismiss a test as flaky without identifying and reproducing it.

---

## 16. Artifact-Level Proof

Source tests do not prove all Windows installer behaviors.

Where applicable, certify the actual built artifact for:

- PE subsystem and manifest;
- Explorer/UAC startup;
- Authenticode;
- package integrity;
- service installation/readiness;
- Start Menu and Apps & Features;
- PATH changes;
- Tray process/elevation behavior;
- repair/upgrade/uninstall;
- terminal UI result;
- reboot persistence.

Distinguish `source-inspected`, `unit-tested`, `integration-tested`, `artifact-tested`, `operator-tested`, and `runtime-proven`.

---

## 17. Package Integrity and Signing

Internal package hashes describe final distributed bytes.

Release ordering must preserve the equivalent of:

```text
build
-> finalize contents
-> Authenticode-sign applicable binaries
-> regenerate/verify internal hashes over signed bytes
-> assemble distribution
-> generate outer checksums
-> generate/sign release metadata
```

Installer verification must reject missing files, hash mismatches, rooted paths, traversal, malformed records, and unsafe path resolution.

---

## 18. Long-Running Lifecycle Rule

Every long-running workflow must define producer lifetime, consumer lifetime, progress, completion signal, success, failure, cancellation, timeout, cleanup, and final result.

Do not permit:

```text
backend completed + UI remains Working
producer exited + consumer has no completion signal
operation failed + process reports Success
```

A defensive timeout must not accidentally become normal lifecycle control.

---

## 19. Documentation Boundaries

Use:

```text
AI-START-HERE.md
    durable rules and navigation

AI/CURRENT.md
    current milestone/blocker

AI/SESSION-PROTOCOL.md
    durable engineering workflow

AI-LOCAL-STATE.md
    ephemeral machine evidence

ADRs
    accepted architecture decisions

project evolution/history
    historical progression

docs/release/
    release architecture and immutable certification evidence
```

Historical documents preserve the terminology and truth of their time. Current navigation/status documents use PathVeer and current truth.

---

## 20. New-Session Response

After bootstrap and repository verification, summarize:

### Verified Repository State
- branch;
- HEAD;
- upstream;
- ahead/behind;
- working-tree condition.

### Current Work
- milestone;
- immediate blocker/goal;
- affected subsystem.

### Architectural Boundary
- responsibility;
- owner;
- source of truth;
- invariant;
- explicitly untouched behavior.

### Risks / Drift
Only actual findings: documentation drift, ADR conflict, uncommitted work, baseline failures, unsafe assumptions, or missing acceptance evidence.

---

## 21. First Principle

When uncertain, ask:

> What responsibility is changing, who owns it, what is the source of truth, what terminates its lifecycle, and what invariant must never break?
