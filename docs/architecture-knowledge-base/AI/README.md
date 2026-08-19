# AI Workspace

This directory contains the small set of durable documents used to continue PathVeer engineering across AI sessions.

## Required Reading

1. repository root `AI-START-HERE.md`
2. [CURRENT.md](CURRENT.md)
3. [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md)

Then read only task-relevant source, tests, ADRs, and documentation.

Do not load the whole Architecture Knowledge Base by default.

## Document Responsibilities

### `CURRENT.md`

The single committed authority for the current milestone, blocker, immediate scope, and current acceptance gates.

Keep it concise. Move historical certification detail into `docs/release/`.

### `SESSION-PROTOCOL.md`

Durable engineering collaboration/execution rules: evidence levels, investigation, implementation, testing, operator gates, Git safety, certification, and handoff.

### `AI-LOCAL-STATE.md` (repository root, generated, Gitignored)

Timestamped machine-specific evidence such as branch, HEAD, working tree, selected build/tests, and relevant Windows process/Service state.

Generate with the current repository tool:

```powershell
.\tools\update-ai-local-state.ps1
```

### `AI-EVIDENCE/` (generated, Gitignored)

Detailed local diagnostic evidence when the snapshot generator uses it. It is supporting evidence, not a source of architectural ownership.

## Access Levels

- **A** — direct checkout and terminal;
- **B** — repository plus fresh local snapshot/operator evidence;
- **C** — repository only;
- **D** — conversation/evidence only.

See `SESSION-PROTOCOL.md` for claim limits and workflow at each level.

## Rule

Only `CURRENT.md` answers:

> What are we working on right now?

Historical phase documents, release records, timelines, and old chats must not compete with it as current-status authorities.
