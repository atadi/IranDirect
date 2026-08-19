# PathVeer — Production Signing & Trust Provisioning Contract

This document defines the durable provisioning boundaries for PathVeer production signing. It does not own current blocker status and does not authorize publication.

For current certification/release state see:

`../architecture-knowledge-base/AI/CURRENT.md`

---

## 1. Separate Trust Domains

PathVeer uses separate trust mechanisms for separate purposes:

1. **Windows Authenticode** — PE publisher/code-signing trust.
2. **Release metadata ES256** — authenticity of update/release metadata.

Do not reuse a private key across these domains merely for convenience.

---

## 2. Publisher / Legal Identity

Assembly display metadata (for example `Authors`, `Company`, `Product`) is not a substitute for a verified legal code-signing identity.

Production Authenticode enrollment must use the actual legal subject accepted by the chosen certificate/signing provider.

Do not guess or fabricate the legal subject.

Required business input when production enrollment is needed:

- exact legal individual/organization;
- jurisdiction/identity documentation required by provider;
- approved signing provider/account.

---

## 3. Authenticode Provisioning

Current signing tooling supports the provider/backend contracts implemented in `tools/Sign-PathVeerArtifacts.ps1`. Inspect current source before provisioning.

Applicable release PE targets generally include:

```text
PathVeer Setup
PathVeer.Service.exe
PathVeer.Cli.exe
PathVeer.Tray.exe
```

Production requirements should include:

- SHA-256 signing;
- RFC3161 timestamp according to current policy;
- private key held only in an approved secure provider/store;
- no private key/PFX committed to Git;
- post-sign verification;
- package hashes generated over final signed bytes.

Development/private certification may use a separate development signing identity. That identity is not production authority.

---

## 4. Production ES256 Metadata Trust

Production update verification is built into the client.

Current production identity:

```text
pv-meta-prod-2026-01
```

Production verifier contract:

```text
ReleaseSignatureVerifier.ForProduction()
-> built-in committed public trust anchors only
-> allowUnsigned = false
```

`PATHVEER_TRUSTED_META_KEYS` is a **development/test/staging** mechanism and must not be merged into `ForProduction()`.

The shipped client needs only public verification material. Production private signing material must remain outside the repository.

See `production-metadata-trust.md` for the current public-anchor and recovery design.

---

## 5. Metadata Private-Key Storage / Recovery

Production metadata signing should use the storage model implemented and documented by current release tooling, with these invariants:

- private key never committed;
- private key not printed to logs;
- access restricted to intended release operator/provider;
- recoverable backup exists where policy requires it;
- backup round-trip verifies the expected public-key fingerprint;
- rotation uses a new key ID and overlap rather than overwriting an existing identity.

A managed/KMS signer is acceptable when supported by the current signing contract and does not export raw private key material.

---

## 6. Development / Staging Separation

Development/staging metadata keys must not be accepted by production verification simply because an environment variable is set.

Development Authenticode certificates must not be confused with a public production publisher identity.

A Development/Signed artifact is certification evidence, not automatically a production release candidate.

---

## 7. Distribution Infrastructure

Distribution configuration (for example R2/S3-compatible storage, DNS, and `releases.pathveer.com`) is separate from signing-key provisioning.

Production credentials must remain outside the repository and outside disposable certification guests unless explicitly required and protected.

The publish pipeline should enforce immutable versioned objects and update channel pointers last.

---

## 8. Disposable VM / Certification Guests

Certification VMs need public artifacts and public trust anchors only.

Do not copy into disposable guests unless a specific gate requires it:

- production ES256 private key;
- production Authenticode private key/PFX;
- R2/S3 secret access keys;
- password vaults;
- unrelated production credentials.

---

## 9. IranDirect Legacy Artifact

Some migration gates require an authentic historical IranDirect artifact.

Do not synthesize a historical build and claim it represents a previously distributed product unless the gate explicitly permits source-reconstructed fixtures.

If an authentic artifact is unavailable, classify that gate honestly rather than weakening the migration test.

---

## 10. External Data / Legal Inputs

Business/legal review for third-party data services is separate from cryptographic signing readiness.

Record such prerequisites in the current release-status document rather than changing trust architecture to bypass them.

---

## 11. Provisioning Checklist

Before production publication confirm:

- legal publisher identity approved;
- Authenticode provider/material available and verified;
- production ES256 key/recovery capability healthy;
- client contains intended production public anchor;
- production verifier rejects unsigned/staging metadata;
- publication credentials are provisioned securely;
- current release artifact passes artifact/runtime certification;
- explicit operator publication authorization exists.

No item in this document itself authorizes publication.
