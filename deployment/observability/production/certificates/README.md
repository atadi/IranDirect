# TLS certificates — PathVeer observability (Phase 33.5)

This directory holds **certificate material** for the production observability
stack. It is a documentation + tooling directory; **no certificate, key, CA, or
token is ever committed.**

## Layout

```
certificates/
  README.md                      # this file
  generate-dev-certs.ps1        # DEV-ONLY cert generator (writes to generated/)
  generated/                    # git-ignored; created by the script (NEVER commit)
    ca.pem, ca-key.pem
    collector_cert.pem, collector_key.pem
    (optional) client_cert.pem, client_key.pem
```

`generated/` is matched by `deployment/observability/.gitignore`
(`certificates/generated/`). If you create certs by hand, keep them out of the
repo.

## What TLS protects

- **PathVeer.Service → Collector OTLP** (gRPC 4317 / HTTP 4318) is encrypted
  with one-way TLS: the Collector presents a server certificate; the Service
  trusts the issuing CA (placed in the Windows host trust store, or the Service
  is run with the CA available to its TLS validation). See
  `../collector-otlp-tls.yaml`.

## Development certificates (NOT for production)

`generate-dev-certs.ps1` creates a self-signed CA + a Collector server
certificate with SANs for `localhost`, `127.0.0.1`, and `collector`, plus an
optional client certificate for mTLS trials. These are **untrusted, self-signed**
and must never protect a real deployment. They exist only to exercise the TLS
handshake locally (Phase 33.5 live verification).

## Production certificates

For production, obtain certificates from your organization's CA or a public CA:

- **Server cert** for the Collector's FQDN (the name PathVeer.Service connects
  to), with a SAN covering that hostname (and any aliases behind a reverse
  proxy).
- **Key size** ≥ 2048-bit RSA or an equivalent EC curve; **SHA-256+** signature.
- **Validity** ≤ 365 days (shorter is better); automate renewal.
- **Permissions:** cert `0644`, key `0600`, owned by the service account; mounted
  read-only into the container (`/run/secrets/...`).

## Rotation

- Renew before expiry (alert on expiry via a reliable `x509_cert_not_after`
  metric if you add one; otherwise calendar-based renewal).
- Rolling replacement: deploy the new cert/key, restart the Collector (graceful
  shutdown drains in-flight exports), confirm health + TLS handshake, then
  revoke the old cert if compromised.
- The Service only needs the CA in its trust store updated if the issuing CA
  changes (not on every leaf renewal).

## Revocation

- Keep the CA key offline. To revoke a leaf cert before expiry, reissue and
  rotate; for mTLS, also remove the client cert from the Collector's trusted
  client-CA bundle.
- Emergency: stop the Collector's public exposure (firewall) and rotate the
  server cert + bearer token together.

## mTLS (optional)

One-way TLS + a bounded bearer token is the default and sufficient. Enable mTLS
(client-certificate auth) only if a second, independent identity is required
(e.g. many untrusted Service hosts). For mTLS: issue client certs from a
dedicated client CA, mount the client-CA bundle into the Collector
(`tls.client_ca_file`), and configure the Service with its client cert/key.
