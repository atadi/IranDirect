#!/usr/bin/env bash
# Static validation of the Phase 36.7 PowerShell installer scripts.
# PowerShell syntax check + a portable check that every identified legacy
# IranDirect string the phase expects to REMAIN is present, and that the
# developer-checkout publish path is gone from the active installer.
cd /c/codespace/PathVeer || exit 1

if command -v pwsh >/dev/null 2>&1; then
  PWSHELL=pwsh
elif command -v powershell >/dev/null 2>&1; then
  PWSHELL=powershell
else
  echo "NO_PWSH"
  exit 2
fi

for f in tools/Install-PathVeer.ps1 tools/New-PathVeerPackage.ps1 tools/Install-PathVeerService.ps1; do
  if "$PWSHELL" -NoLogo -NoProfile -Command "&\$PSScriptRoot" >/dev/null 2>&1; then :; fi
  "$PWSHELL" -NoLogo -NoProfile -Command "[ScriptBlock]::Create((Get-Content -Raw '$f'))" >/dev/null 2>&1
  echo "$f -> syntax=$?"
done

echo "--- brand expectations ---"
grep -nq 'IranDirect.Control.v1' tools/Install-PathVeer.ps1 && echo "OK legacy pipe retained" || echo "MISSING legacy pipe"
grep -nq 'irandirect.cmd' tools/Install-PathVeer.ps1 && echo "OK legacy CLI shim present" || echo "MISSING shim"
grep -nq 'ProgramFiles' tools/Install-PathVeer.ps1 && echo "OK install root is ProgramFiles" || echo "MISSING ProgramFiles"
grep -nq 'codespace' tools/Install-PathVeer.ps1 && echo "DEFECT dev path in installer" || echo "OK no dev path in installer"