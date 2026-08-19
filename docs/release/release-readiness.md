# PathVeer — Release / Signing Readiness Guide

This is a durable operator-facing release-readiness guide. It does **not** declare the current candidate ready, does not authorize publication, and does not own the current blocker list.

For current engineering/certification status use:

`../architecture-knowledge-base/AI/CURRENT.md`

For immutable historical proof use the applicable `certification-*.md` and phase records under this directory.

---

## 1. Release Authorities

Separate these concerns:

- **source provenance** — exact committed source used for the artifact;
- **package integrity** — manifest/checksums over final distributed bytes;
- **Authenticode** — Windows PE publisher/code-signing trust;
- **release metadata signature** — ES256 update/feed authenticity;
- **publication** — immutable upload and channel pointer mutation;
- **runtime certification** — actual Windows install/upgrade/repair/uninstall behavior.

Passing one does not imply another.

---

## 2. Tooling

Relevant tools may include:

| Tool | Responsibility |
|---|---|
| `tools/New-PathVeerRelease.ps1` | build release/certification artifact |
| `tools/New-PathVeerPackage.ps1` | create package and internal integrity manifest |
| `tools/Sign-PathVeerArtifacts.ps1` | Authenticode-sign applicable PE files |
| `tools/Sign-ReleaseManifest.ps1` | ES256-sign/verify release metadata |
| `tools/Publish-PathVeerRelease.ps1` | validated immutable publication; channel pointer last |
| `tools/Test-PathVeerAuthenticodeReadiness.ps1` | signing readiness probe |

Always inspect current source/tool contracts before relying on this list.

---

## 3. Trust Model

### Production metadata trust

Production client trust is fail closed:

```text
keyId = pv-meta-prod-2026-01
allowUnsigned = false
```

Production verification uses committed/built-in public trust anchors. Environment-provided development/staging keys must not be merged into the production trust set.

### Development metadata trust

Development/Signed certification uses the isolated development authority defined by current source/certification policy (currently `pv-meta-dev-2026-01`).

Development private material must not be substituted for production trust.

### Authenticode

Authenticode is independent from ES256 release metadata signing. A development-signed artifact may be suitable for private certification while still not being a production-signed public release.

---

## 4. Correct Build/Sign/Hash Ordering

The final integrity manifest must describe final bytes.

Preserve the equivalent of:

```text
build
-> finalize content
-> Authenticode-sign applicable binaries
-> regenerate internal package hashes over signed bytes
-> verify package integrity
-> assemble distribution
-> generate outer checksums
-> generate/sign release metadata
```

Do not generate an internal package hash manifest and then modify its PE files without regenerating the hashes.

---

## 5. Immutability

For a versioned published path or tested certification artifact:

- same version + same bytes may be treated idempotently where tooling permits;
- same version + different bytes must never silently overwrite evidence/publication;
- after a materially changed source/signing build, use a new version.

The frozen `1.0.0-beta.1` artifact must not be rebuilt, re-signed, overwritten, or republished. See `AI/CURRENT.md` for the canonical hash.

---

## 6. Channel Pointer Ordering

Versioned immutable objects and signed metadata must be fully validated before a mutable channel pointer is changed.

Conceptually:

```text
immutable artifacts
-> integrity verification
-> signed metadata
-> metadata verification
-> channel latest pointer LAST
```

A failure before the final pointer update must leave consumers on the previous known-good release.

---

## 7. Certification vs Publication

Certification does not authorize publication.

Unless the user explicitly authorizes publication:

- do not upload a production release;
- do not modify `R2/latest.json` or equivalent production pointers;
- do not create/tag a public release;
- do not use production private signing material merely to complete development certification.

---

## 8. Artifact Acceptance Checklist

Before calling a candidate technically certifiable, verify as applicable:

### Provenance
- clean/synchronized source at build time;
- embedded/product provenance matches exact commit.

### Package integrity
- all manifest paths relative/safe;
- no traversal/rooted entries;
- no missing files;
- no hash mismatches;
- hashes cover final signed bytes.

### Authenticode
- intended PE targets signed;
- `Get-AuthenticodeSignature` valid under intended trust;
- `signtool verify /pa` succeeds where required;
- timestamp policy satisfied for production signing.

### Metadata
- expected environment/mode;
- expected key ID;
- signature verifies under intended trust;
- unsigned metadata rejected under production policy.

### Windows executable/runtime
- valid manifest/SideBySide activation;
- Explorer/UAC flow;
- install/upgrade/repair/uninstall;
- Service readiness;
- terminal Setup UI/exit code;
- Tray single-instance and privilege level;
- reboot persistence where required.

---

## 9. Production Preconditions

Production publication may additionally require external/business inputs such as:

- valid production Authenticode identity/material;
- production metadata signing/recovery capability;
- approved distribution/DNS/storage configuration;
- legal/business clearance for external data dependencies;
- authentic historical artifacts for compatibility gates where those gates remain required.

Their current status belongs in `AI/CURRENT.md` or a current operator release checklist, not in this durable guide.

---

## 10. Final Authorization

A release is not authorized because:

- build is green;
- tests pass;
- certification passes;
- branch is merged;
- artifact exists;
- a previous readiness document said "ready".

Publication requires an explicit operator decision after current evidence is reviewed.
