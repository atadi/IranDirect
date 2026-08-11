# PathVeer — Authenticode feasibility & provider matrix

Authoritative research (August 2026) into whether a **publicly trusted Windows
Authenticode code-signing certificate** can be issued to:

* Legal publisher: **Alireza Tadi**
* Publisher type: **individual developer**
* Legal residence: **Iran**

No false residency, nominee, borrowed, or other-person certificate is considered.
Self-signed certificates are explicitly NOT equivalent to public trust.

## Conclusion

**NONE CONFIRMED.** No publicly trusted issuer was found that confirms issuance
to an individual legally resident in Iran. This is an **EXTERNAL BLOCKER** outside
PathVeer engineering: it stems from US sanctions embargoes on Iran (comprehensive
sanctions block all US-based CAs) and from the org-only issuance policies of the
EU CAs that do not carry an explicit Iran ban.

Until a confirmed eligible route exists, PathVeer continues to ship:

* unsigned beta/dev builds,
* ES256-protected release metadata (production trust root embedded in the client),
* SHA-256 verified downloads (InstallerDownloadVerifier),

without ever representing an unsigned build as Authenticode-trusted.

## Provider matrix (sources dated 2024–2026)

| Provider | Individual? | Iran? | Status | Reason | Official source |
|---|---|---|---|---|---|
| Microsoft Artifact Signing (Trusted Signing) | US/CA only | No | **NO** | Public Trust only for orgs in US/CA/EU/UK + individual devs in US/CA | learn.microsoft.com/…/artifact-signing/faq + quickstart |
| SSL.com | — | **No** | **NO** | Iran (IR) explicitly listed under "cannot issue certificates" | ssl.com/country-codes |
| Sectigo | — | **No** | **NO** | Iran (IR) on Banned Country List (US export restrictions) | sectigo.com/knowledge-base/detail/Banned-Country-List (updated 2024-08-21) |
| DigiCert | — | **No** | **NO** | Iran (IR) on Comprehensive Sanctions embargoed list | knowledge.digicert.com/solution/embargoed-countries-and-regions (last modified 2026-04-26) |
| Entrust | — | **No** | **NO** | No longer processes docs issued by US-sanctioned countries incl. Iran | support.identity.entrust.com/…/Documents-Issued-by-US-Sanctioned-Countries-FAQs |
| GlobalSign | **No** | Org unclear | **NO** (individual) / CONTACT REQUIRED (Iranian org) | Code-signing certs issued only to legally registered organizations; region picker excludes Iran | globalsign.com/en/code-signing |
| Certum (Asseco) | unclear | unclear | **CONTACT REQUIRED** | EU CA, org validation typical; no explicit Iran public ban found, individual issuance + Iran residency not confirmed | certum.eu |
| HARICA | unclear | unclear | **CONTACT REQUIRED** | EU CA; no explicit Iran public ban found; individual/Iran eligibility unconfirmed | harica.gr |
| Actalis | unclear | unclear | **CONTACT REQUIRED** | EU CA, org validation; no explicit Iran public ban found | actalis.com |
| SwissSign | unclear | unclear | **CONTACT REQUIRED** | Swiss CA, org validation; no explicit Iran public ban found | swisssign.ch |

All "CONTACT REQUIRED" EU providers would still require a **legally registered
organization** (not an individual), so even a positive response would not satisfy
the stated individual-publisher facts without forming a company — which this
research does not do.

## If a legitimate route is later confirmed

If authoritative evidence supports individual/Iran issuance, STOP before purchase
and report: provider, product, current price, identity requirements, key
protection, signing mechanism, authoritative source. Ask the user for
authorization to proceed with the external application.

## CA/B Forum Code Signing Baseline Requirements — production implications

(Not legal advice; engineering-relevant points only.)

* **Private-key protection:** since 2023, code-signing keys must be generated and
  stored in a hardware cryptomodule (FIPS 140-2 L2 / Common Criteria EAL 4+), e.g.
  a USB token or cloud HSM. Non-exportable keys are the norm. PathVeer's signing
  pipeline already supports this via AzureSignTool (key vault/HSM) and the
  thumbprint backend (installed non-exportable cert) — it does NOT require a
  plain exportable PFX.
* **Subscriber verification:** OV/EV requires identity validation; EV adds
  rigorous vetting and yields stronger install-time indicators.
* **Certificate lifecycle:** max validity 460 days (CA/B Forum; GlobalSign and
  others moved to ~366-day issuance after 2025-12-26). Timestamping (RFC3161,
  SHA-256) is required so signatures survive past certificate expiry.
* **Timestamping:** PathVeer already wires RFC3161 SHA-256 to
  `timestamp.digicert.com`; the URL is config-driven and signing fails if
  timestamping is required but does not succeed (fail-closed).

## Authenticode ≠ SmartScreen reputation

A valid Authenticode certificate establishes a cryptographic publisher identity
and a Windows trust-chain signature. It does **not** guarantee the absence of
SmartScreen warnings, which depend on a separate Microsoft reputation system.
PathVeer will not promise warning-free installs.
