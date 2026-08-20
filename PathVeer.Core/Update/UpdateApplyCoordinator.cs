namespace PathVeer.Core.Update;

using System.Diagnostics;
using System.IO;
using System.Net.Http;

/// <summary>
/// Orchestrates the full product update-apply path that was previously only a
/// documented seam:
///
///   manifest (already ES256-verified by UpdateChecker)
///     -> download installer to .partial (bounded streaming)
///     -> verify: expected size + SHA-256 + production Authenticode + publisher
///     -> atomically promote .partial -> final installer
///     -> re-verify the final path (TOCTOU closure)
///     -> return the staged installer so the caller may launch Setup.
///
/// SECURITY INVARIANTS (fail closed):
///   * A verification failure NEVER promotes and NEVER returns an executable.
///   * Process.Start is never called here — execution is the caller's explicit,
///     user-initiated action after a successful stage.
///   * The expected publisher is taken ONLY from <see cref="ProductionSigningPolicy"/>,
///     never from the manifest or any remote source.
///   * A .partial file is never treated as a valid, executable artifact.
///
/// Service/Setup boundary is preserved: this coordinator only downloads, verifies,
/// and stages; it does not execute install logic. Tray invokes Setup with its
/// normal UAC flow once a staged installer is returned.
/// </summary>
public sealed class UpdateApplyCoordinator
{
    private readonly UpdateDownloader _downloader;
    private readonly UpdateStagingPaths _staging;
    private readonly InstallerDownloadVerifier _verifier;

    public UpdateApplyCoordinator(
        HttpClient http,
        UpdateStagingPaths staging,
        InstallerDownloadVerifier verifier,
        long maxBytes = UpdateDownloader.MaxDownloadBytes)
    {
        _downloader = new UpdateDownloader(http, maxBytes);
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    /// <summary>
    /// Downloads and verifies the installer described by <paramref name="manifest"/>.
    /// On success returns <see cref="UpdateApplyResult.Success"/> with the final
    /// staged installer path. On any failure returns a failed result and leaves no
    /// promoted executable. The caller must NOT launch Setup unless
    /// <see cref="UpdateApplyResult.Success"/> is true.
    /// </summary>
    public async Task<UpdateApplyResult> ApplyAsync(
        ReleaseManifest manifest,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        if (manifest?.Installer is null)
            return UpdateApplyResult.Failure("Manifest has no installer artifact.");

        var artifact = manifest.Installer;
        if (string.IsNullOrWhiteSpace(artifact.Url) || string.IsNullOrWhiteSpace(artifact.FileName))
            return UpdateApplyResult.Failure("Manifest installer artifact is missing url/fileName.");

        var version = string.IsNullOrWhiteSpace(manifest.Version) ? "unknown" : manifest.Version;
        _staging.EnsureVersionDirectory(version);

        var finalPath = _staging.FinalPath(version, artifact.FileName);
        var partialPath = _staging.PartialPath(version, artifact.FileName);

        // Never promote over an existing good artifact; clean any stale partial
        // from a previous interrupted attempt first.
        _staging.CleanPartial(version, artifact.FileName);
        if (File.Exists(finalPath)) { try { File.Delete(finalPath); } catch { /* best effort */ } }

        try
        {
            await _downloader.DownloadAsync(
                artifact.Url, partialPath, artifact.Size, progress, ct)
                .ConfigureAwait(false);

            // Verify BEFORE promotion: hash + size + Authenticode + publisher.
            var verify = _verifier.Verify(partialPath, artifact);
            if (!verify.IsValid)
                return UpdateApplyResult.Failure(verify.Error ?? "Installer verification failed.");

            // Atomic promotion. .partial -> final.
            File.Move(partialPath, finalPath, overwrite: true);

            // TOCTOU closure: re-verify the promoted path.
            var verifyFinal = _verifier.Verify(finalPath, artifact);
            if (!verifyFinal.IsValid)
            {
                // Roll back the promoted artifact so no unverified file lingers.
                try { File.Delete(finalPath); } catch { /* best effort */ }
                return UpdateApplyResult.Failure(
                    verifyFinal.Error ?? "Promoted installer failed re-verification.");
            }

            return UpdateApplyResult.Success(finalPath, verify.Hash!);
        }
        catch (OperationCanceledException)
        {
            _staging.CleanPartial(version, artifact.FileName);
            return UpdateApplyResult.Failure("Update download was cancelled.", cancelled: true);
        }
        catch (UpdateDownloadException ex)
        {
            _staging.CleanPartial(version, artifact.FileName);
            return UpdateApplyResult.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _staging.CleanPartial(version, artifact.FileName);
            return UpdateApplyResult.Failure($"Update apply failed: {ex.Message}");
        }
    }
}

public sealed class UpdateApplyResult
{
    public bool Succeeded { get; }
    public string? StagedInstallerPath { get; }
    public string? Hash { get; }
    public string? Error { get; }
    public bool Cancelled { get; }

    private UpdateApplyResult(bool succeeded, string? path, string? hash, string? error, bool cancelled)
    {
        Succeeded = succeeded;
        StagedInstallerPath = path;
        Hash = hash;
        Error = error;
        Cancelled = cancelled;
    }

    public static UpdateApplyResult Success(string path, string hash) =>
        new(true, path, hash, null, false);

    public static UpdateApplyResult Failure(string error, bool cancelled = false) =>
        new(false, null, null, error, cancelled);
}
