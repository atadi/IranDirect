# Cloudflare R2 real-distribution certification (beta / staging)

Status date: this document records the first REAL object-storage publication of a
PathVeer release and the evidence gathered against the live public domain.

**Scope: beta/staging only. `windows/stable/latest.json` was never written.**

---

## 1. Infrastructure

| Item | Value |
|---|---|
| Object store | Cloudflare R2, Standard storage class |
| Bucket | `pathveer-releases` |
| Public custom domain | `https://releases.pathveer.com` (ACTIVE) |
| Credential | Bucket-scoped Account API token, Object Read & Write, scoped to `pathveer-releases` only |

The token used before this work was rotated by the operator. The replacement
credential has never been shared in chat, never printed, and never written to
the repository or to any environment variable.

## 2. Credential handling

Credentials are loaded from the operator's Windows DPAPI-protected secret store.

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\PathVeer\Secrets\r2-credential.xml` | DPAPI `PSCredential`: `UserName` = Access Key ID, `Password` = Secret Access Key |
| `%LOCALAPPDATA%\PathVeer\Secrets\r2-config.json` | Non-secret: `Endpoint`, `Bucket`, `PublicBaseUrl` |

Resolution precedence (`Resolve-PathVeerR2CredentialPath`):

1. explicit `-CredentialPath`
2. default DPAPI path
3. *(reserved for a future external provider)*

There is deliberately **no** plaintext or environment-variable fallback: a
missing or malformed credential is a hard failure, never a silent downgrade.
`Import-Clixml` on Windows only decrypts a DPAPI blob for the same user on the
same machine, so a copied credential file cannot be read elsewhere.

Diagnostics are redacted by construction:

```
Credential source    DPAPI
Credential loaded    yes
Bucket               pathveer-releases
Endpoint configured  yes
Public base URL      https://releases.pathveer.com
```

The Secret Access Key is held only as a `SecureString`, marshalled to a plain
string for the duration of a single SDK call, and scrubbed from any exception
text by `Protect-PathVeerSecretText` before the error can reach stdout, stderr,
the audit record or the publication summary.

## 3. S3 client mechanism

**Chosen: the `AWS.Tools.S3` PowerShell module (v5.0.273), CurrentUser scope.**

Rationale:

* The AWS CLI is not installed on this workstation, and `aws` is a much larger
  dependency than a single modular PowerShell module.
* `AWS.Tools.S3` is the *modular* AWS SDK package (S3 only), not the monolithic
  `AWSPowerShell`.
* It supports everything R2 needs directly: `-EndpointUrl`,
  `-ForcePathStyleAddressing`, `-AccessKey`/`-SecretKey`, custom metadata and
  header collections.
* Manual AWS SigV4 was therefore not required and was **not** implemented.

Two R2-specific compatibility details were required and are documented in the
module:

* `-DisablePayloadSigning` — Cloudflare R2 does not implement
  `STREAMING-AWS4-HMAC-SHA256-PAYLOAD`, which recent AWS SDKs use by default.
  Requests are plain SigV4 over HTTPS; integrity is still fully enforced by
  PathVeer's own SHA-256 read-back verification.
* A bounded 30-minute `AmazonS3Config.Timeout`, because the ~126 MiB installer
  exceeds the SDK's default per-request timeout on a slow link.

## 4. Object layout

```
windows/beta/latest.json                                              (mutable pointer)
windows/stable/latest.json                                            (NOT written)
windows/<version>/win-x64/release-manifest.json                       (immutable)
windows/<version>/win-x64/PathVeerSetup-<version>-win-x64.exe         (immutable)
windows/<version>/win-x64/PathVeer-<version>-win-x64.zip              (immutable)
```

`New-PathVeerRelease.ps1` previously generated installer/package URLs under the
*channel* prefix, which did not match where the publisher writes objects. Because
those URLs are inside the signed payload, the generator was corrected to emit the
versioned path **before** signing. Signed manifest bytes are never edited after
signing; a wrong base URL requires regenerate → re-sign.

### Content types

| Extension | Content-Type |
|---|---|
| `.json` | `application/json` |
| `.exe` | `application/octet-stream` |
| `.zip` | `application/zip` |
| `.txt`, `.sha256` | `text/plain` |

### Cache policy

| Object class | Cache-Control |
|---|---|
| Versioned immutable artifacts | `public, max-age=31536000, immutable` |
| Channel pointers (`windows/*/latest.json`) | `public, max-age=60, must-revalidate` |

## 5. Publication order (enforced)

1. validate the frozen local release
2. verify the ES256 manifest signature
3. verify local artifact SHA-256 hashes
4. upload immutable artifacts
5. verify remote objects (real byte read-back, not ETag)
6. upload the immutable version manifest
7. verify the remote manifest
8. update `windows/beta/latest.json` **LAST**
9. fetch the pointer through `releases.pathveer.com`
10. run PathVeer client validation

## 6. Immutability

Versioned object paths are immutable. Before writing an existing path the
publisher compares PathVeer's **own** SHA-256 (never the S3 ETag — multipart
ETags are not content hashes):

| Condition | Behaviour |
|---|---|
| same path, same SHA-256 | no-op (`unchanged:`), zero bytes uploaded |
| same path, different SHA-256 | **HARD FAIL** — `IMMUTABILITY VIOLATION` |

Optional non-secret object metadata (`pathveer-sha256`, `pathveer-version`,
`pathveer-channel`) is attached for cheap inspection, but the strongest
certification check downloads and hashes the real public bytes.

## 7. Storage budget

Cloudflare R2 Standard includes a 10 GB-month allowance on the current plan.
PathVeer enforces a conservative application-level threshold of **8 GiB**:

```
current release storage + new unique release bytes = projected storage
projected > 8 GiB  ->  publication STOPS
```

The block is deliberate and non-destructive: it prints the eligible cleanup set
and requires an explicit retention application rather than deleting anything
automatically.

## 8. Retention

Semantic, release-version-level retention — never blind age-based deletion:

| Channel | Keep |
|---|---|
| stable | latest 15 |
| beta / rc | latest 5 |

Protected (never auto-deleted): the current stable `latest` target, the current
beta `latest` target, the supported upgrade-floor release when hosted, and any
explicitly pinned release. A release is only ever removed as a coherent object
set. Retention has a preview mode and requires `-ApplyRetention` to delete.

Cloudflare-side lifecycle deletion is **not** configured for `windows/stable`,
`windows/beta` or `windows/<version>` — the publisher owns semantic retention.
The existing incomplete-multipart cleanup rule is untouched.

## 9. Real publication evidence — PathVeer 1.0.0-beta.1

Objects published (all read back and hash-verified):

```
windows/1.0.0-beta.1/win-x64/PathVeerSetup-1.0.0-beta.1-win-x64.exe   125.91 MiB
windows/1.0.0-beta.1/win-x64/PathVeer-1.0.0-beta.1-win-x64.zip          2.72 MiB
windows/1.0.0-beta.1/win-x64/release-manifest.json                      1.14 KiB
windows/beta/latest.json                                              (written LAST)
```

* installer SHA-256: `03adadd3ea23d1dcf72d177e4ab94531f881fa8702632d937137ba647045ecc7`
* installer size: 132,030,496 bytes
* storage: 0 B → 128.63 MiB (budget 8.00 GiB)
* `windows/stable/latest.json`: HTTP 404 before and after — never created.

### Public HTTPS (through `releases.pathveer.com`, not the S3 endpoint)

| Object | Status | Content-Type | Cache-Control |
|---|---|---|---|
| `windows/beta/latest.json` | 200 | `application/json` | `public, must-revalidate, max-age=60` |
| `windows/1.0.0-beta.1/win-x64/release-manifest.json` | 200 | `application/json` | `public, max-age=31536000, immutable` |
| installer `.exe` | 200 | `application/octet-stream` | `public, max-age=31536000, immutable` |

TLS valid; no HTML/SPA fallback (JSON bodies verified, not just headers);
`Content-Length` matches the signed manifest size exactly. The channel pointer
bytes are byte-identical to the versioned manifest.

### Immutability, proven against real R2

* **same bytes republished** → `objects to upload 0`, `already identical 3`,
  three `unchanged:` lines, storage unchanged at 128.63 MiB.
* **different bytes at the same version path** (a temp copy with one flipped
  byte, manifest regenerated and re-signed) → publication aborted with
  `IMMUTABILITY VIOLATION`. The published installer was re-downloaded afterwards
  and still hashes to `03adadd3…`.

### Client path (real `HttpReleaseSource`)

Exercised against `https://releases.pathveer.com/windows/beta/latest.json`:

```
schemaVersion=1 product=PathVeer channel=beta platform=windows arch=x64 version=1.0.0-beta.1
ES256 signature: VALID (keyId=pv-meta-staging-2026)
UpdateChecker state=CurrentVersionNewer installed=1.0.0 available=1.0.0-beta.1
```

Production signature requirements were not weakened: the verifier was
constructed with `allowUnsigned = false` and a real trusted-key set.

### Download verification

The installer was downloaded through the public domain and verified by the real
`InstallerDownloadVerifier`: size and SHA-256 both match the signed manifest and
the artifact is **accepted**. With an Authenticode probe wired in it is correctly
**rejected** (`Installer signature invalid: Unsigned`) — the honest current state.

### Tamper rejection

Performed against a **disposable** `_tamper-test/<guid>/` object; the real
published release was never mutated. A one-byte-flipped installer served over
`releases.pathveer.com` was rejected:

```
Installer SHA-256 mismatch: expected 03adadd3…, got 3ae96eb3…
```

Setup was never launched. The disposable object was deleted afterwards, and the
real installer was re-verified as unchanged.

### Storage-budget guard

Re-running publication with the threshold forced to 1 MiB produced:

```
STORAGE BUDGET EXCEEDED
  projected 128.63 MiB exceeds the 1.00 MiB conservative threshold.
  No release sets are eligible for cleanup (all protected or within keep counts).
Publication STOPPED: projected R2 storage exceeds the 8 GiB application safety threshold.
```

Retention preview correctly classified the release:

```
1.0.0-beta.1  beta  rank=1  bytes=128.63 MiB  latest=True  protected=True  eligible=False
```

## 10. Metadata signing key

Production ES256 metadata signing material is still not provisioned. This
certification used a **staging** key (`pv-meta-staging-2026`) generated into the
operator's DPAPI store at
`%LOCALAPPDATA%\PathVeer\StagingKeys\pv-meta-staging-2026.xml`. It is outside the
repository and is not a production trust root. Provisioning the production key
remains a release prerequisite.

## 11. Gate status

### GATE-11 — Real staging HTTPS feed → verified download

**PASS (metadata/transport)**, with Authenticode explicitly outstanding.

Evidence: live DNS + TLS on `releases.pathveer.com`; HTTP 200 with correct
Content-Type/Cache-Control/Content-Length for pointer, versioned manifest and
installer; real `HttpReleaseSource` fetch; ES256 verification against a real
trusted-key set; full installer download with matching size and SHA-256; tamper
rejection proven on a disposable object.

Not claimed: production Authenticode. The downloaded installer is `NotSigned`,
and with the Authenticode gate enabled the verifier refuses it. If the formal
gate definition includes Authenticode, GATE-11 is **PARTIAL/BLOCKED** on that
clause alone; every non-Authenticode clause passed against the real environment.

### GATE-12 — Production-like CDN/object-store immutable publication

**PASS.**

Evidence: real Cloudflare R2 + custom-domain retrieval (no local filesystem
stand-in); same-bytes republish is a no-op; different bytes at an immutable path
are rejected; `latest.json` written last; beta/stable isolation proven
(`windows/stable/latest.json` never created); correct cache policy for both
immutable artifacts and channel pointers.

## 12. Test coverage

`tools/PathVeerR2.psm1` policy surface is covered by 49 deterministic xUnit tests
in `PathVeer.Core.Tests/Update/R2PublicationPolicyTests.cs`, using **fake**
secrets only. They never contact Cloudflare and never read the real credential
store, so `dotnet test` has no dependency on Cloudflare availability or on the
operator's secrets.

Real-environment verification is separate and explicitly invoked:

```
tools/certification/Test-PathVeerR2Distribution.ps1   # live feed, download, tamper
tools/certification/Test-PathVeerSecretLeak.ps1       # real-secret leak scan
```

## 13. Remaining blockers

* **Production Authenticode** — unchanged external blocker. Cloudflare TLS and
  ES256 metadata trust do not substitute for Windows Authenticode, and the
  restriction must not be worked around by misrepresenting residency, company or
  identity, by borrowing a certificate, or by presenting a self-signed
  certificate as trusted.
* **Production ES256 metadata key** — staging key used here.
* Disposable VM gates, IranDirect artifact, and remaining VM certification items
  are unchanged by this work.
