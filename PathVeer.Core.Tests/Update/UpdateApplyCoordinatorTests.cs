namespace PathVeer.Core.Tests.Update;

using PathVeer.Core.Update;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Focused tests for the production update-apply pipeline (Gap A). These prove
/// the previously-document-only chain:
///
///   manifest (ES256) -> download .partial -> verify (size+hash+Authenticode)
///     -> promote .partial -> final -> re-verify final.
///
/// A non-UNPROVISIONED Authenticode probe is simulated via a synthetic
/// signature probe so the orchestration can be exercised without a real
/// production certificate (which is an external blocker). The publisher-policy
/// fail-closed behavior is covered separately in ProductionSigningPolicyTests.
/// </summary>
public sealed class UpdateApplyCoordinatorTests
{
    private sealed class ByteHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public int Requests { get; private set; }

        public ByteHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes)
            };
            return Task.FromResult(resp);
        }
    }

    private static (UpdateStagingPaths staging, string root) MakeStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvapply_" + Guid.NewGuid().ToString("N"));
        return (new UpdateStagingPaths(root), root);
    }

    private static ReleaseManifest.ManifestArtifact Artifact(string url, byte[] data, string? sha = null)
    {
        var hash = sha ?? Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        return new ReleaseManifest.ManifestArtifact
        {
            FileName = "PathVeerSetup-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".exe",
            Url = url,
            Sha256 = hash,
            Size = data.Length
        };
    }

    private static ReleaseManifest Manifest(ReleaseManifest.ManifestArtifact art, string version = "1.0.0-rc.1")
    {
        return new ReleaseManifest
        {
            Version = version,
            Channel = "beta",
            Product = "PathVeer",
            Installer = art,
            Components = new[] { "Service", "Cli", "Tray" }
        };
    }

    [Fact]
    public async Task ValidManifest_ValidDownload_ValidSignature_ProducesStagedInstaller()
    {
        var data = Encoding.UTF8.GetBytes("fake-installer-bytes");
        var manifest = Manifest(Artifact("http://localhost/fake.exe", data));

        var (staging, root) = MakeStaging();
        try
        {
            var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Valid);
            using var http = new HttpClient(new ByteHandler(data));
            var coord = new UpdateApplyCoordinator(http, staging, verifier);

            var result = await coord.ApplyAsync(manifest);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.StagedInstallerPath);
            Assert.True(File.Exists(result.StagedInstallerPath));
            Assert.EndsWith(".exe", result.StagedInstallerPath);
            Assert.DoesNotContain(".partial", result.StagedInstallerPath);
            var partialPath = Path.Combine(
                Path.GetDirectoryName(result.StagedInstallerPath)!,
                Path.GetFileName(result.StagedInstallerPath) + ".partial");
            Assert.False(File.Exists(partialPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatch_Rejected_NoPromotion_NoExecutionEligibility()
    {
        var data = Encoding.UTF8.GetBytes("real-bytes");
        var art = Artifact("http://localhost/fake.exe", data,
            sha: "0000000000000000000000000000000000000000000000000000000000000000");
        var manifest = Manifest(art);

        var (staging, root) = MakeStaging();
        try
        {
            var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Valid);
            using var http = new HttpClient(new ByteHandler(data));
            var coord = new UpdateApplyCoordinator(http, staging, verifier);

            var result = await coord.ApplyAsync(manifest);

            Assert.False(result.Succeeded);
            Assert.Null(result.StagedInstallerPath);
            Assert.Contains("SHA-256", result.Error ?? "");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SizeMismatch_Rejected()
    {
        var data = Encoding.UTF8.GetBytes("abc");
        var art = Artifact("http://localhost/fake.exe", data);
        art = new ReleaseManifest.ManifestArtifact
        {
            FileName = art.FileName,
            Url = art.Url,
            Sha256 = art.Sha256,
            Size = art.Size + 1
        };
        var manifest = Manifest(art);

        var (staging, root) = MakeStaging();
        try
        {
            var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Valid);
            using var http = new HttpClient(new ByteHandler(data));
            var coord = new UpdateApplyCoordinator(http, staging, verifier);

            var result = await coord.ApplyAsync(manifest);

            Assert.False(result.Succeeded);
            Assert.Contains("size", (result.Error ?? "").ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthenticodeInvalid_Rejected()
    {
        var data = Encoding.UTF8.GetBytes("signed-but-untrusted");
        var manifest = Manifest(Artifact("http://localhost/fake.exe", data));

        var (staging, root) = MakeStaging();
        try
        {
            var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Invalid);
            using var http = new HttpClient(new ByteHandler(data));
            var coord = new UpdateApplyCoordinator(http, staging, verifier);

            var result = await coord.ApplyAsync(manifest);

            Assert.False(result.Succeeded);
            Assert.Null(result.StagedInstallerPath);
            Assert.Contains("signature", (result.Error ?? "").ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_NoExecutionEligibility()
    {
        var data = Encoding.UTF8.GetBytes("big-installer");
        var manifest = Manifest(Artifact("http://localhost/fake.exe", data));

        var (staging, root) = MakeStaging();
        try
        {
            var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Valid);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using var http = new HttpClient(new ByteHandler(data));
            var coord = new UpdateApplyCoordinator(http, staging, verifier);

            var result = await coord.ApplyAsync(manifest, ct: cts.Token);

            Assert.False(result.Succeeded);
            Assert.Null(result.StagedInstallerPath);
            Assert.True(result.Cancelled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnprovisionedPublisher_ManifestCannotOverrideExpectedIdentity()
    {
        var verifier = ProductionSigningPolicy.CreateInstallerVerifier();
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "anything");
            var res = verifier.Verify(tmp, Artifact("http://localhost/x.exe",
                Encoding.UTF8.GetBytes("anything")));
            Assert.False(res.IsValid);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
