# PathVeer Phase 37.1 — Windows Release Packaging Architecture (ADR)

Branch: `development/service-authority`
Base: `fa7299d` (Phase 36.8 acceptance)
Scope: establish the production Windows release packaging architecture for the
PathVeer rename/migration program. No UX polish (that is 37.2), no remote
update service (37.3), no SaaS (38.x).

---

## 1. Problem & requirements

PathVeer is not a simple per-user desktop app. The release must:

- install a **Windows Service** (machine-level, persistent routing authority);
- perform privileged **native routing** operations at install/upgrade;
- deploy under **`%ProgramFiles%\PathVeer`** (Service/Cli/Tray);
- preserve **`%ProgramData%\PathVeer`** persistent state (no destruction on default uninstall);
- support a **legacy IranDirect → PathVeer upgrade** with strict ordering
  (stop → disable → stage → verify → swap → start → readiness gate → retire legacy);
- guarantee **single authority** (legacy + PathVeer never both run);
- integrate **CLI + PATH**, a **compatibility launcher** (`irandirect.cmd`),
  per-user optional **Tray startup**, and a **compatibility IPC pipe**
  (`IranDirect.Control.v1`);
- be **Authenticode-signable** (SHA-256, timestamping) without committing secrets;
- produce **deterministic, hash-verified, versioned artifacts** suitable for
  GitHub Releases / pathveer.com / a release CDN later.

## 2. Alternatives considered

| Candidate | Fit | Verdict |
| --- | --- | --- |
| **WiX Toolset (MSI + Burn)** | Strong for file servicing/repair, but PathVeer has NO per-file MSI patching need, NO COM/driver registration. Burn would still need a custom bootstrapper to run the legacy migration + readiness gate, so the ordering logic ends up BOTH in Burn and in product code → dual implementation risk. | Rejected for 37.1 |
| **MSIX** | Per-user/app-container model fights the machine-level Service + privileged routing + legacy SCM migration. | Rejected |
| **Inno Setup / NSIS** | Capable, but a separate Pascal/NSIS script would re-encode the entire upgrade/migration contract → second source of install truth. | Rejected |
| **Self-contained .NET bootstrap EXE wrapping PowerShell** | Keeps PowerShell as the single install-truth (already unit-tested in `PathVeer.Core.Installation`), adds only UAC elevation + script delegation, native Authenticode signing, zero new toolchain. | **CHOSEN** |

Rationale ties directly to §2 of the phase prompt: "One source of installation
truth is strongly preferred." The PowerShell deployment contract (`Install-PathVeer.ps1`
+ `PathVeer.Core.Installation`) already encodes every load-bearing behavior and
is covered by `InstallOrchestratorTests` / `StateRootMigrationTests` /
`RouteMutationRecoveryTests`. Re-implementing that in an MSI/Inno engine would
duplicate it and risk drift. The bootstrapper is therefore a thin, signable host,
not an installer engine.

## 3. Chosen architecture

```
artifacts/releases/<version>/<runtime>/
    PathVeerSetup-<version>-<runtime>.exe     self-contained signed bootstrap
    PathVeer-<version>-<runtime>.zip          portable component package
    PathVeerSetup-<version>-<runtime>.exe.sha256
    <version>-<runtime>.zip.sha256
    checksums.txt                            every file hashed
    release-manifest.json                    machine-readable release record
    PathVeer-<version>/                      the package the bootstrapper consumes
        Service/  Cli/  Tray/  package-hashes.sha256  package.json
```

Flow:
```
PathVeerSetup.exe (double-click)
  -> UAC elevation (runas) if not admin
  -> extract embedded Install-PathVeer.ps1 to %TEMP%
  -> locate co-located PathVeer-<version> package
  -> invoke Install-PathVeer.ps1 -PackageDirectory ... (-InstallTray, -Uninstall, -PurgeState)
  -> report exit code
```

The embedded script is a verbatim copy of `tools/Install-PathVeer.ps1`
(committed under `PathVeer.Setup/Resources/`), so the dev/CI path and the
shipped path are byte-identical in behavior.

## 4. Versioning

Single authoritative source: **`Directory.Build.props`** `<Version>`.
- `AssemblyVersion` = `MAJOR.0.0.0` (CLR binding compat breaks only on MAJOR).
- `FileVersion` = full version (incl. prerelease tag).
- `InformationalVersion` = version + commit when `GIT_COMMIT` is set.
- Prerelease via `VersionSuffix` (`1.0.0-beta.1`).
- The package builder, release orchestrator and bootstrapper all read `<Version>`,
  so assemblies, installer metadata and release artifacts cannot drift.

## 5. Deployment-contract integration (existing behavior preserved)

| Contract | How preserved |
| --- | --- |
| Fresh install | delegated to `Install-PathVeer.ps1` unchanged |
| PathVeer upgrade | delegated; idempotent convergence in `InstallOrchestrator` |
| Legacy IranDirect upgrade | delegated; stop→disable→swap→start→readiness→retire |
| Single authority | `InstallOrchestratorTests` single-authority harness still authoritative |
| Readiness gate | unchanged (process + primary pipe + CLI status) |
| State preservation | `%ProgramData%\PathVeer` never touched by installer |
| Explicit purge | `-PurgeState` only path deleting state |
| Rollback boundary | legacy `%ProgramData%\IranDirect` retained |
| Compatibility launcher | `irandirect.cmd` still created by the same script |
| Legacy IPC pipe | `IranDirect.Control.v1` served by the same Service |

## 6. Code signing (secret-safe)

`tools/Sign-PathVeerArtifacts.ps1`:
- No PFX/key/password in repo. Credentials come from env vars
  (`AZURE_*`, or `PATHVEER_SIGN_PFX`/`PATHVEER_SIGN_PASSWORD`, or
  `PATHVEER_SIGN_THUMBPRINT`).
- Backends: Azure Sign Tool (CI, no file on disk) > PFX > installed cert by thumbprint.
- `-FailIfUnavailable` makes `Release/Signed` hard-fail when no credentials exist,
  so an unsigned artifact can never be published as production-ready.
- Local/unsigned dev mode is the default and is clearly labelled.

## 7. Framework deployment decision

**Self-contained single-file** for the *bootstrapper* (`PathVeerSetup.exe`), so a
normal user has a double-clickable installer with **no .NET runtime prerequisite**.
The *component package* remains **framework-dependent** (per 36.7) to keep it
small and let the shared runtime be serviced independently for security updates;
the bootstrapper's only job is to install those components, not run them. This
hybrid minimizes consumer friction for the installer while keeping the runtime
story clean for the service/CLI/Tray.

## 8. Machine vs user scope

Installer runs elevated (machine-level): Service, Program Files, PATH, Start Menu.
Tray startup remains per-user (HKCU Run), wired by `-InstallTray` — never forced
globally. Bootstrapper elevates only the install, then delegates to the script.

## 9. Upgrade / downgrade

- Product identity = version in `install-manifest.json` + `Directory.Build.props`.
- Upgrade detection: `InstallOrchestrator` reads existing manifest, converges.
- Downgrade policy: blocked by `ReleasePackagingTests.IsUpgradeAllowed`
  (older-over-newer, including major, is rejected; same version idempotent).
- No remote update check (deferred to 37.3).

## 10. Future compatibility

- `release-manifest.json` carries `minimumUpgradeVersion` and `signed` so 37.3's
  update metadata can consume it directly.
- Artifact naming (`PathVeerSetup-<ver>-<rid>.exe`) is RID-parameterised; adding
  `win-arm64` is a build-flag change, not a redesign.
- `irandirect-*-data` Docker volumes and all retained legacy identifiers are
  untouched by release packaging.

## 11. Known limitations / deferred

- Installer UX (wizard pages, Start Menu polish, Tray-startup UX) → **37.2**.
- Remote update metadata / channels / background updater → **37.3**.
- pathveer.com distribution / CDN upload → **37.4**.
- Production EV/OV code-signing certificate procurement (not yet provisioned).
- Phase 36.8 four VM gates (real legacy upgrade, native-route mutation on upgrade,
  reboot persistence, purge/reinstall) remain release-certification gates.
