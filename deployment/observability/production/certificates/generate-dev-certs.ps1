<#
IranDirect observability - DEV-ONLY certificate generator (Phase 33.5).

Creates a self-signed CA + a Collector server certificate (and an optional
client certificate for mTLS trials) for EXERCISING the TLS OTLP handshake
locally. Output goes to certificates/generated/ which is git-ignored.

Uses openssl (must be on PATH). THESE CERTIFICATES ARE UNTRUSTED AND
SELF-SIGNED. NEVER use them to protect a real deployment. For production,
obtain certs from your org CA / a public CA.
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $root "generated"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$days = 730
$caCnf = Join-Path $out "ca.cnf"
@"
[req]
distinguished_name = dn
x509_extensions = v3_ca
[dn]
[v3_ca]
basicConstraints = critical, CA:TRUE
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
"@ | Set-Content -Path $caCnf

$srvCnf = Join-Path $out "srv.cnf"
@"
[req]
distinguished_name = dn
[v3_srv]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectKeyIdentifier = hash
subjectAltName = @alt_names
[alt_names]
DNS.1 = localhost
DNS.2 = collector
IP.1 = 127.0.0.1
"@ | Set-Content -Path $srvCnf

$cliCnf = Join-Path $out "cli.cnf"
@"
[req]
distinguished_name = dn
[v3_cli]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature
extendedKeyUsage = clientAuth
subjectKeyIdentifier = hash
"@ | Set-Content -Path $cliCnf

# 1) CA
openssl genrsa -out (Join-Path $out "ca-key.pem") 2048 2>$null
openssl req -x509 -new -nodes -key (Join-Path $out "ca-key.pem") -sha256 -days $days `
    -subj "/CN=irandirect-dev-ca" -config $caCnf -extensions v3_ca `
    -out (Join-Path $out "ca.pem") 2>$null

# 2) Collector server cert (signed by CA)
openssl genrsa -out (Join-Path $out "collector_key.pem") 2048 2>$null
openssl req -new -key (Join-Path $out "collector_key.pem") -sha256 `
    -subj "/CN=collector" -out (Join-Path $out "collector.csr") 2>$null
openssl x509 -req -in (Join-Path $out "collector.csr") -CA (Join-Path $out "ca.pem") `
    -CAkey (Join-Path $out "ca-key.pem") -CAcreateserial -days $days -sha256 `
    -extfile $srvCnf -extensions v3_srv -out (Join-Path $out "collector_cert.pem") 2>$null

# 3) Optional client cert (mTLS trials)
openssl genrsa -out (Join-Path $out "client_key.pem") 2048 2>$null
openssl req -new -key (Join-Path $out "client_key.pem") -sha256 `
    -subj "/CN=irandirect-service" -out (Join-Path $out "client.csr") 2>$null
openssl x509 -req -in (Join-Path $out "client.csr") -CA (Join-Path $out "ca.pem") `
    -CAkey (Join-Path $out "ca-key.pem") -CAcreateserial -days $days -sha256 `
    -extfile $cliCnf -extensions v3_cli -out (Join-Path $out "client_cert.pem") 2>$null

Write-Host "DEV certs written to: $out"
Write-Host "  ca.pem / ca-key.pem"
Write-Host "  collector_cert.pem / collector_key.pem"
Write-Host "  client_cert.pem / client_key.pem (for mTLS trials)"
Write-Host "Copy collector_cert.pem + collector_key.pem into ../secrets/ to run the production overlay locally."
