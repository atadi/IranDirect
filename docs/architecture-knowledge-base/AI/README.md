# AI Workspace

This directory contains durable collaboration and continuation documents.

## Two Sources of Current Truth

### Committed repository truth

Use committed source, tests, ADRs, and project-state documents to understand:

- what is implemented;
- why the architecture was selected;
- what milestone is committed;
- what constraints must be preserved.

### Local runtime truth

Use the generated root file:

```text
AI-LOCAL-STATE.md
```

to understand:

- the developer's checked-out branch and commit;
- uncommitted changes;
- build and test results;
- service/process state;
- ProgramData files;
- selected route-table evidence.

`AI-LOCAL-STATE.md` is not committed.

Generate it with:

```powershell
.\tools\update-ai-local-state.ps1
```

## New-Session Access Levels

A new AI session should identify its access level:

- **A — Direct checkout and terminal access**
- **B — Repository access plus an uploaded AI-LOCAL-STATE.md**
- **C — Public web access only**

Only level A can independently verify the local machine.

Level B should compare committed repository truth with the uploaded snapshot.

Level C must request a fresh `AI-LOCAL-STATE.md` before implementation.