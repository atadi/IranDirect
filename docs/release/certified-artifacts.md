# PathVeer — Certified / Frozen Artifact Registry

This is the single canonical record of frozen and certified release specimens.
It is the authority for "what is frozen and why", referenced by
`AI/CURRENT.md`, `AI-START-HERE.md`, and release-readiness docs.

Freeze semantics (immutable):

- bytes remain unchanged;
- version is never rebuilt into the same release identity;
- signatures are never replaced;
- metadata is never rewritten;
- release output is never overwritten;
- certification evidence remains attributable to the original bytes;
- corrections are made in documentation, not by altering the specimen.

`FROZEN + CERTIFIED DEVELOPMENT/SIGNED` does NOT mean: production-signed,
production-released, or production-published.

All SHA-256 values below were computed from the actual artifact bytes at the
time of this record. They are not copied from memory.

---

## 1.0.0-beta.1

Status:

```text
FROZEN HISTORICAL ARTIFACT
```

SHA-256 (PathVeerSetup-1.0.0-beta.1-win-x64.exe):

```text
7E5B4301756DFA617509C8A533AED3000E7025D2B3470150B0E6933361FFA24D
```

Rules: no rebuild, no re-sign, no overwrite, no republish.
Any new candidate after a source/signing change uses a NEW version.

---

## 1.0.0-devsign.8

Status:

```text
FROZEN FAILED REGRESSION SPECIMEN
```

Reason: successful backend deployment could leave the Setup GUI in `Working...`
due to a progress-reader lifecycle / result-processing defect (the
`Finished` record was lost and `Completed` never fired). This defect was closed
in a later specimen.

SHA-256 (PathVeerSetup-1.0.0-devsign.8-win-x64.exe):

```text
05B5BAD6C0A2D499265A5C9E49E18DC441879C4CAA379BC1D42E6F404C8A3A75
```

---

## 1.0.0-devsign.9

Status:

```text
FROZEN FAILED REGRESSION SPECIMEN
```

Important devsign.9 outcomes:

- the progress-reader defect was closed;
- but live operator testing found:
  - Tray launched `Elevated=True` (the elevated Setup child launched it);
  - same-version Repair failed while the old installed Tray locked
    `Accessibility.dll`.

Both defects were closed in devsign.10.

Note: during the documentation refresh, the detailed
`certification-devsign.9-proof.md` file was removed from the repository; the
artifact directory (`artifacts/releases/1.0.0-devsign.9`) remains frozen. This
registry preserves its freeze status.

SHA-256 (PathVeerSetup-1.0.0-devsign.9-win-x64.exe):

```text
A937F851330EFF03D6D177186C134644ED66F9F3F990688220422F65C3DFF342
```

---

## 1.0.0-devsign.10

Status:

```text
FROZEN CERTIFIED DEVELOPMENT/SIGNED SPECIMEN
```

Artifact:

```text
PathVeerSetup-1.0.0-devsign.10-win-x64.exe
```

Artifact source commit:

```text
aea0e8c1151327920ef1b0f1bd67ea2acec4b243
```

Certification evidence/tooling HEAD (post-artifact fixes; does NOT rebuild the
binary):

```text
4474403f89142b6e6522d949c586ebb90180f6e2
```

Trust:

```text
Development/Signed
pv-meta-dev-2026-01
```

Important: certified **development** artifact — NOT a production publication
artifact.

SHA-256 (PathVeerSetup-1.0.0-devsign.10-win-x64.exe):

```text
0E49CADA3CD7B12090130F1F2883908C609A6229E1CE11D8D5B686E9B177049F
```

Live operator acceptance: PASS (TESTS 1-5). Detailed evidence in
`certification-devsign.10-proof.md`.

---

## Overwrite Safety

`tools/New-PathVeerRelease.ps1` now refuses to build a version whose target
arch release root already exists (fail-closed; no `-Force`/`-Overwrite` bypass).
A changed build must use a NEW version. Verified by
`tools/Test-PathVeerReleaseOverwriteGuard.ps1`.

---

## Summary Table

| Version            | Status                                | Trust                | Setup EXE SHA-256 (upper)                                                  |
|--------------------|---------------------------------------|---------------------|----------------------------------------------------------------------------|
| 1.0.0-beta.1       | FROZEN HISTORICAL                     | (immutable)         | 7E5B4301756DFA617509C8A533AED3000E7025D2B3470150B0E6933361FFA24D           |
| 1.0.0-devsign.8    | FROZEN FAILED REGRESSION SPECIMEN     | (n/a)               | 05B5BAD6C0A2D499265A5C9E49E18DC441879C4CAA379BC1D42E6F404C8A3A75           |
| 1.0.0-devsign.9    | FROZEN FAILED REGRESSION SPECIMEN     | (n/a)               | A937F851330EFF03D6D177186C134644ED66F9F3F990688220422F65C3DFF342           |
| 1.0.0-devsign.10   | FROZEN CERTIFIED DEVELOPMENT/SIGNED   | pv-meta-dev-2026-01 | 0E49CADA3CD7B12090130F1F2883908C609A6229E1CE11D8D5B686E9B177049F           |
