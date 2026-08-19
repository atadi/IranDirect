# PathVeer Documentation Refresh Bundle

This bundle contains proposed replacement/update files prepared from the supplied `docs.zip` and the current installer-certification evidence in the conversation.

## Replace / add

- `AI-START-HERE.md`
- `docs/architecture-knowledge-base/README.md`
- `docs/architecture-knowledge-base/AI/README.md`
- `docs/architecture-knowledge-base/AI/CURRENT.md`
- `docs/architecture-knowledge-base/AI/SESSION-PROTOCOL.md`
- `docs/architecture-knowledge-base/04-projects/PathVeer/README.md` (new)
- `docs/architecture-knowledge-base/04-projects/IranDirect/README.md`
- `docs/architecture-knowledge-base/04-projects/IranDirect/evolution.md`
- `docs/architecture-knowledge-base/00-architecture-journal/timeline.md`
- `docs/architecture-knowledge-base/00-architecture-journal/journal.md`
- `docs/architecture-knowledge-base/03-reading-map/README.md`
- `docs/architecture-knowledge-base/05-reference/checklists.md`
- `docs/architecture-knowledge-base/05-reference/glossary.md`
- `docs/release/certification-status.md`
- `docs/release/release-readiness.md`
- `docs/release/signing-provisioning.md`
- `docs/release/production-metadata-trust.md`
- `docs/release/certification-devsign.6-proof.md`
- `docs/release/certification-devsign.7-proof.md`
- `docs/release/certification-devsign.8-proof.md`
- `docs/release/certification-merge-readiness.md`
- `docs/release/vm-certification-runbook.md`
- `docs/release/phase-37.8-vm-certification.md`

## Important missing-reference review

The supplied archive did **not** contain:

- `docs/release/phase-37.9-vm-execution.md`
- `docs/release/phase-37.4-release-architecture.md`

Several historical release documents reference one or both. Verify the live repository before deciding whether those files are actually missing or were simply omitted from the archive.

## Authority cleanup

After this refresh:

- root `AI-START-HERE.md` = durable rules/navigation;
- `AI/CURRENT.md` = only current milestone/blocker authority;
- `AI/SESSION-PROTOCOL.md` = durable workflow;
- `AI-LOCAL-STATE.md` = ephemeral machine evidence;
- PathVeer project README = durable current architecture;
- IranDirect directory = legacy/historical architecture;
- release proof docs = immutable historical evidence, not current-status authorities.

Review with `git diff` before committing. Do not perform a global IranDirect -> PathVeer replacement because compatibility and historical identifiers are intentional in many documents.
