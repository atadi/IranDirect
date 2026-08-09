using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using PathVeer.Core.State;

namespace PathVeer.Core.Installation;

/// <summary>
/// Real filesystem/PATH/registry implementation of
/// <see cref="IInstallFileSystem"/>.
///
/// Package integrity uses the SHA-256 manifest produced by the packaging
/// script. Note this is integrity only, not authenticity: it detects a
/// corrupt or truncated package, it does NOT prove provenance. Authenticity
/// requires Authenticode signing, which is documented as a release
/// prerequisite and is deliberately not faked here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstallFileSystem : IInstallFileSystem
{
    /// <summary>SHA-256 manifest emitted next to the package payload.</summary>
    public const string PackageHashFileName = "package-hashes.sha256";

    /// <summary>Registry key holding per-user autorun entries.</summary>
    public const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Autorun value name used for the PathVeer tray.</summary>
    public const string TrayRunValueName = "PathVeer Tray";

    /// <summary>Autorun value name used by the legacy IranDirect tray.</summary>
    public const string LegacyTrayRunValueName = "IranDirect Tray";

    private static readonly JsonSerializerOptions ManifestJsonOptions =
        new() { WriteIndented = true };

    public async Task<InstallManifest?> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(
            manifestPath,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(
                json,
                ManifestJsonOptions);
        }
        catch (JsonException)
        {
            // A corrupt manifest must not block a repair install; treat it as
            // "no known previous installation" and let the upgrade rewrite it.
            return null;
        }
    }

    public async Task WriteManifestAsync(
        string manifestPath,
        InstallManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        string? directory = Path.GetDirectoryName(manifestPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            manifest,
            ManifestJsonOptions);

        await File.WriteAllTextAsync(
            manifestPath,
            json,
            cancellationToken);
    }

    public async Task<bool> VerifyPackageAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(packageDirectory))
        {
            return false;
        }

        string hashFile = Path.Combine(
            packageDirectory,
            PackageHashFileName);

        if (!File.Exists(hashFile))
        {
            // No manifest means we cannot make an integrity claim. Refuse
            // rather than silently installing an unverified payload.
            return false;
        }

        foreach (string line in await File.ReadAllLinesAsync(
                     hashFile,
                     cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(
                ' ',
                2,
                StringSplitOptions.TrimEntries);

            if (parts.Length != 2)
            {
                return false;
            }

            string expectedHash = parts[0];
            string relativePath = parts[1].TrimStart('*');

            string fullPath = Path.Combine(
                packageDirectory,
                relativePath);

            if (!File.Exists(fullPath))
            {
                return false;
            }

            string actualHash =
                await ComputeSha256Async(fullPath, cancellationToken);

            if (!string.Equals(
                    expectedHash,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public Task StageAsync(
        string packageDirectory,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        CopyDirectory(packageDirectory, stagingDirectory);

        return Task.CompletedTask;
    }

    public Task<bool> VerifyStagedLayoutAsync(
        InstallLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        string staging = layout.StagingDirectory;

        bool valid =
            File.Exists(Path.Combine(
                staging,
                InstallLayout.ServiceDirectoryName,
                InstallLayout.ServiceExecutableName))
            && File.Exists(Path.Combine(
                staging,
                InstallLayout.CliDirectoryName,
                InstallLayout.CliExecutableName))
            && File.Exists(Path.Combine(
                staging,
                InstallLayout.TrayDirectoryName,
                InstallLayout.TrayExecutableName));

        return Task.FromResult(valid);
    }

    public Task SwapAsync(
        string stagingDirectory,
        string installRoot,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(installRoot);

        // Replace each component directory individually. The manifest and any
        // sibling content in the install root survive the swap.
        foreach (string component in new[]
                 {
                     InstallLayout.ServiceDirectoryName,
                     InstallLayout.CliDirectoryName,
                     InstallLayout.TrayDirectoryName
                 })
        {
            string source = Path.Combine(stagingDirectory, component);
            string destination = Path.Combine(installRoot, component);

            if (!Directory.Exists(source))
            {
                continue;
            }

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(source, destination);
        }

        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task DiscardStagingAsync(
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task RemoveDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public void EnsurePathEntry(string entry)
    {
        string? current = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.Machine);

        if (PathEnvironmentEditor.ContainsEntry(current, entry))
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            PathEnvironmentEditor.AddEntry(current, entry),
            EnvironmentVariableTarget.Machine);
    }

    public void RemovePathEntry(string entry)
    {
        string? current = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.Machine);

        if (!PathEnvironmentEditor.ContainsEntry(current, entry))
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            PathEnvironmentEditor.RemoveEntry(current, entry),
            EnvironmentVariableTarget.Machine);
    }

    public void RemoveTrayStartupEntry()
    {
        // The per-user autorun entry lives under HKCU\...\Run. PathVeer.Core
        // targets plain net10.0 (no Windows registry surface), and autorun is a
        // per-user concern rather than a machine-service one, so the actual
        // registry write is performed by the installer script, which runs in
        // the user's context. Keeping this a no-op here preserves the seam
        // without pulling a Windows-only dependency into Core.
        //
        // See Install-PathVeer.ps1 -> Remove-TrayStartupEntry, which removes
        // both "PathVeer Tray" and the legacy "IranDirect Tray" values.
    }

    public Task PurgeStateAsync(
        CancellationToken cancellationToken = default)
    {
        // Only ever reached on an explicit purge request. The LEGACY root
        // (%ProgramData%\IranDirect) is intentionally never deleted here.
        StateRootResolver resolver = new();

        if (Directory.Exists(resolver.CurrentRoot))
        {
            Directory.Delete(resolver.CurrentRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);

        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
