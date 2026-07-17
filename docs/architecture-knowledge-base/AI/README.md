# AI Workspace

This directory contains durable collaboration and continuation documents.

## Required Reading

1. repository root `AI-START-HERE.md`
2. [CURRENT.md](CURRENT.md)
3. the remaining task-relevant files listed by `AI-START-HERE.md`

## Durable Current State

`CURRENT.md` is the committed executive summary.

It contains:

- current mission;
- current architecture;
- implemented responsibilities;
- architectural debt;
- immediate next milestone;
- non-negotiable invariants.

Update it after architecturally significant milestones.

## Local Runtime State

Use the generated root file:

```text
AI-LOCAL-STATE.md
```

Generate it with:

```powershell
.\tools\update-ai-local-state.ps1
```

Successful snapshots remain compact.

Full evidence is written under:

```text
AI-EVIDENCE/
```

Both generated locations are ignored by Git.

## Access Levels

- A — Direct checkout and terminal
- B — Repository plus uploaded AI-LOCAL-STATE.md
- C — Public repository only

Only level A independently verifies the local machine.

Level B compares committed repository truth with the uploaded snapshot.

Level C requests a fresh snapshot before implementation.
## Session Protocol

Read [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md) for:

- human and AI responsibilities;
- access levels;
- session boot sequence;
- architecture review gate;
- implementation workflow;
- completion gate;
- end-of-session handoff.