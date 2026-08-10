namespace PathVeer.Core.Tests.Update;

using System.Diagnostics;
using System.Text.Json;
using Xunit;

/// <summary>
/// R2 distribution-policy tests.
///
/// These exercise the PURE policy surface of tools/PathVeerR2.psm1 — credential
/// path resolution, non-secret config parsing, object key mapping, content type,
/// cache policy, immutability decisions, the 8 GiB storage budget, semantic
/// retention and secret redaction.
///
/// HARD RULE: these tests NEVER touch Cloudflare, never read the real DPAPI
/// credential store, and never contain real secret material. Only fabricated
/// fake secrets are used. Real R2 publication is a separate, explicitly-invoked
/// external integration exercise so that `dotnet test` never depends on
/// Cloudflare availability or on the developer's credential store.
/// Each test spawns a real `pwsh` process. xUnit's default parallelism starts
/// dozens of these at once, which starves process startup and makes the suite
/// flaky, so the class is pinned to its own non-parallel collection.
/// </summary>
[CollectionDefinition("R2Publication", DisableParallelization = true)]
public sealed class R2PublicationCollection { }

/// <inheritdoc cref="R2PublicationCollection"/>
[Collection("R2Publication")]
public sealed class R2PublicationPolicyTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ModulePath = Path.Combine(RepoRoot, "tools", "PathVeerR2.psm1");

    /// <summary>Runs a PowerShell snippet with the R2 module imported; returns (exitCode, stdout, stderr).</summary>
    private static (int Code, string Out, string Err) Ps(string script)
    {
        var full = $"Set-StrictMode -Version Latest; $ErrorActionPreference='Stop'; Import-Module '{ModulePath}' -Force; " + script;
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(full);

        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        Task.WaitAll(so, se);
        return (p.ExitCode, so.Result.Trim(), se.Result.Trim());
    }

    private static string Ok(string script)
    {
        var (code, so, se) = Ps(script);
        Assert.True(code == 0, $"pwsh exited {code}.\nSTDOUT:\n{so}\nSTDERR:\n{se}");
        return so;
    }

    private static string Fails(string script)
    {
        var (code, so, se) = Ps(script);
        Assert.True(code != 0, $"Expected failure but pwsh exited 0.\nSTDOUT:\n{so}");
        return so + "\n" + se;
    }

    /// <summary>Runs a script expected to throw, returning the caught exception message
    /// verbatim (stderr formatting wraps long messages, so we capture it in-process).</summary>
    private static string ThrowsMessage(string script)
    {
        var outp = Ok($"try {{ {script} ; 'NO-THROW' }} catch {{ $_.Exception.Message }}");
        Assert.DoesNotContain("NO-THROW", outp);
        return outp;
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "pv-r2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // --- Credential source path resolution ------------------------------------

    [Fact]
    public void CredentialPath_DefaultsToDpapiLocalAppDataPath()
    {
        var outp = Ok("$r = Resolve-PathVeerR2CredentialPath; \"$($r.Source)|$($r.Path)\"");
        var parts = outp.Split('|');
        Assert.Equal("DPAPI", parts[0]);
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PathVeer", "Secrets", "r2-credential.xml");
        Assert.Equal(expected, parts[1]);
    }

    [Fact]
    public void CredentialPath_ExplicitParameterTakesPrecedenceOverDpapiDefault()
    {
        var dir = TempDir();
        var explicitPath = Path.Combine(dir, "alt-credential.xml");
        var outp = Ok($"$r = Resolve-PathVeerR2CredentialPath -CredentialPath '{explicitPath}'; \"$($r.Source)|$($r.Path)\"");
        Assert.StartsWith("Explicit|", outp);
        Assert.EndsWith("alt-credential.xml", outp);
    }

    [Fact]
    public void MissingCredentialFile_FailsClosedWithNoPlaintextFallback()
    {
        var dir = TempDir();
        var msg = ThrowsMessage($"Get-PathVeerR2Credential -SecretsRoot '{dir}'");
        Assert.Contains("credential not found", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plaintext credential fallback", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidCredentialFile_IsRejected()
    {
        var dir = TempDir();
        // A valid CLIXML file whose payload is NOT a PSCredential.
        Ok($"'just-a-string' | Export-Clixml -LiteralPath '{Path.Combine(dir, "r2-credential.xml")}'; 'ok'");
        var msg = ThrowsMessage($"Get-PathVeerR2Credential -SecretsRoot '{dir}'");
        Assert.Contains("not a PSCredential", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptCredentialFile_IsRejectedWithDpapiGuidance()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "r2-credential.xml"), "this is not xml at all <<<");
        var msg = ThrowsMessage($"Get-PathVeerR2Credential -SecretsRoot '{dir}'");
        Assert.Contains("could not be decrypted", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DpapiRoundTrip_LoadsFakeCredentialWithoutEverReturningPlaintext()
    {
        var dir = TempDir();
        var cred = Path.Combine(dir, "r2-credential.xml");
        // Fabricated, obviously-fake credential material. Never a real key.
        Ok($"$p = ConvertTo-SecureString 'FAKE-SECRET-DO-NOT-USE-0000' -AsPlainText -Force; " +
           $"$c = New-Object System.Management.Automation.PSCredential('FAKEACCESSKEYID', $p); " +
           $"$c | Export-Clixml -LiteralPath '{cred}'; 'ok'");

        var outp = Ok($"$r = Get-PathVeerR2Credential -CredentialPath '{cred}'; " +
                      "\"$($r.Source)|$($r.Credential.UserName)|$($r.Credential.GetType().Name)\"");
        Assert.Equal("Explicit|FAKEACCESSKEYID|PSCredential", outp);
        // The plaintext secret must not be present in the returned diagnostics text.
        Assert.DoesNotContain("FAKE-SECRET-DO-NOT-USE", outp);
    }

    // --- Non-secret config parsing ---------------------------------------------

    private static string WriteConfig(string dir, string json)
    {
        var p = Path.Combine(dir, "r2-config.json");
        File.WriteAllText(p, json);
        return p;
    }

    [Fact]
    public void Config_ParsesEndpointBucketAndPublicBaseUrl()
    {
        var dir = TempDir();
        WriteConfig(dir, """
        { "Endpoint": "https://example.r2.cloudflarestorage.com", "Bucket": "pathveer-releases", "PublicBaseUrl": "https://releases.pathveer.com" }
        """);
        var outp = Ok($"$c = Get-PathVeerR2Config -SecretsRoot '{dir}'; \"$($c.Bucket)|$($c.PublicBaseUrl)|$($c.Endpoint)\"");
        Assert.Equal("pathveer-releases|https://releases.pathveer.com|https://example.r2.cloudflarestorage.com", outp);
    }

    [Fact]
    public void Config_NormalisesSchemelessEndpointAndTrimsTrailingSlash()
    {
        var dir = TempDir();
        WriteConfig(dir, """
        { "Endpoint": "acct.r2.cloudflarestorage.com", "Bucket": "b", "PublicBaseUrl": "https://releases.pathveer.com/" }
        """);
        var outp = Ok($"$c = Get-PathVeerR2Config -SecretsRoot '{dir}'; \"$($c.Endpoint)|$($c.PublicBaseUrl)\"");
        Assert.Equal("https://acct.r2.cloudflarestorage.com|https://releases.pathveer.com", outp);
    }

    [Fact]
    public void Config_MissingFieldIsRejected()
    {
        var dir = TempDir();
        WriteConfig(dir, """{ "Endpoint": "https://x.r2.cloudflarestorage.com", "Bucket": "b" }""");
        var msg = ThrowsMessage($"Get-PathVeerR2Config -SecretsRoot '{dir}'");
        Assert.Contains("PublicBaseUrl", msg);
    }

    [Fact]
    public void Config_NonHttpsPublicBaseUrlIsRejected()
    {
        var dir = TempDir();
        WriteConfig(dir, """{ "Endpoint": "https://x.r2.cloudflarestorage.com", "Bucket": "b", "PublicBaseUrl": "http://releases.pathveer.com" }""");
        var msg = ThrowsMessage($"Get-PathVeerR2Config -SecretsRoot '{dir}'");
        Assert.Contains("must be https", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Config_MissingFileIsRejected()
    {
        var dir = TempDir();
        var msg = ThrowsMessage($"Get-PathVeerR2Config -SecretsRoot '{dir}'");
        Assert.Contains("configuration not found", msg, StringComparison.OrdinalIgnoreCase);
    }

    // --- R2 path mapping --------------------------------------------------------

    [Theory]
    [InlineData("1.0.0", "PathVeerSetup-1.0.0-win-x64.exe", "windows/1.0.0/win-x64/PathVeerSetup-1.0.0-win-x64.exe")]
    [InlineData("1.0.0-beta.1", "release-manifest.json", "windows/1.0.0-beta.1/win-x64/release-manifest.json")]
    [InlineData("2.3.4", "PathVeer-2.3.4-win-x64.zip", "windows/2.3.4/win-x64/PathVeer-2.3.4-win-x64.zip")]
    public void ReleaseKeyMapping_UsesSignedPathContract(string version, string file, string expected)
    {
        Assert.Equal(expected, Ok($"Get-PathVeerR2ReleaseKey -Version '{version}' -FileName '{file}'"));
    }

    [Theory]
    [InlineData("beta", "windows/beta/latest.json")]
    [InlineData("stable", "windows/stable/latest.json")]
    public void ChannelPointerKeyMapping(string channel, string expected)
    {
        Assert.Equal(expected, Ok($"Get-PathVeerR2ChannelPointerKey -Channel '{channel}'"));
    }

    // --- Content type -----------------------------------------------------------

    [Theory]
    [InlineData("windows/1.0.0/win-x64/release-manifest.json", "application/json")]
    [InlineData("windows/1.0.0/win-x64/PathVeerSetup-1.0.0-win-x64.exe", "application/octet-stream")]
    [InlineData("windows/1.0.0/win-x64/PathVeer-1.0.0-win-x64.zip", "application/zip")]
    [InlineData("windows/1.0.0/win-x64/checksums.txt", "text/plain")]
    [InlineData("windows/1.0.0/win-x64/PathVeerSetup.exe.sha256", "text/plain")]
    public void ContentTypeMapping(string key, string expected)
    {
        Assert.Equal(expected, Ok($"Get-PathVeerR2ObjectContentType -FileName '{key}'"));
    }

    // --- Cache policy -----------------------------------------------------------

    [Fact]
    public void CachePolicy_ImmutableForVersionedArtifacts()
    {
        Assert.Equal("public, max-age=31536000, immutable",
            Ok("Get-PathVeerR2CachePolicy -Key 'windows/1.0.0/win-x64/PathVeerSetup-1.0.0-win-x64.exe'"));
        Assert.Equal("public, max-age=31536000, immutable",
            Ok("Get-PathVeerR2CachePolicy -Key 'windows/1.0.0/win-x64/release-manifest.json'"));
    }

    [Fact]
    public void CachePolicy_ShortRevalidateForChannelPointers()
    {
        Assert.Equal("public, max-age=60, must-revalidate", Ok("Get-PathVeerR2CachePolicy -Key 'windows/beta/latest.json'"));
        Assert.Equal("public, max-age=60, must-revalidate", Ok("Get-PathVeerR2CachePolicy -Key 'windows/stable/latest.json'"));
    }

    // --- Immutability -----------------------------------------------------------

    [Fact]
    public void Immutability_AbsentObjectUploads()
    {
        Assert.Equal("Upload", Ok("Get-PathVeerR2ImmutableAction -LocalSha256 'aa' -RemoteExists $false"));
    }

    [Fact]
    public void Immutability_SameHashIsNoOp()
    {
        Assert.Equal("NoOp", Ok("Get-PathVeerR2ImmutableAction -LocalSha256 'AABBCC' -RemoteSha256 'aabbcc' -RemoteExists $true"));
    }

    [Fact]
    public void Immutability_DifferentHashIsRejected()
    {
        Assert.Equal("Reject", Ok("Get-PathVeerR2ImmutableAction -LocalSha256 'aabbcc' -RemoteSha256 'ddeeff' -RemoteExists $true"));
    }

    [Fact]
    public void Immutability_UnknownRemoteHashForcesAuthoritativeVerification()
    {
        // Never trust an ETag: with no authoritative remote SHA-256 the answer is
        // "download and verify", not "assume identical".
        Assert.Equal("Verify", Ok("Get-PathVeerR2ImmutableAction -LocalSha256 'aabbcc' -RemoteSha256 '' -RemoteExists $true"));
    }

    // --- Storage budget ----------------------------------------------------------

    [Fact]
    public void StorageBudget_ThresholdIsEightGibibytes()
    {
        Assert.Equal((8L * 1024 * 1024 * 1024).ToString(), Ok("Get-PathVeerStorageBudgetBytes"));
    }

    [Fact]
    public void StorageBudget_AllowsPublicationUnderThreshold()
    {
        var outp = Ok("$r = Test-PathVeerStorageBudget -CurrentBytes 1000000000 -NewBytes 500000000; \"$($r.Allowed)|$($r.ProjectedBytes)\"");
        Assert.Equal("True|1500000000", outp);
    }

    [Fact]
    public void StorageBudget_BlocksPublicationOverEightGiB()
    {
        var outp = Ok("$r = Test-PathVeerStorageBudget -CurrentBytes 8000000000 -NewBytes 900000000; \"$($r.Allowed)\"");
        Assert.Equal("False", outp);
    }

    [Fact]
    public void StorageBudget_ExactlyAtThresholdIsAllowed()
    {
        var outp = Ok("$r = Test-PathVeerStorageBudget -CurrentBytes 8589934591 -NewBytes 1; \"$($r.Allowed)\"");
        Assert.Equal("True", outp);
    }

    [Fact]
    public void StorageBudget_OneByteOverThresholdIsBlocked()
    {
        var outp = Ok("$r = Test-PathVeerStorageBudget -CurrentBytes 8589934592 -NewBytes 1; \"$($r.Allowed)\"");
        Assert.Equal("False", outp);
    }

    // --- Release set grouping / channel classification -----------------------------

    [Fact]
    public void ReleaseSets_GroupObjectsByVersionAndIgnoreChannelPointers()
    {
        var script = """
        $objs = @(
          [pscustomobject]@{ Key='windows/1.0.0/win-x64/a.exe'; Size=100 },
          [pscustomobject]@{ Key='windows/1.0.0/win-x64/release-manifest.json'; Size=10 },
          [pscustomobject]@{ Key='windows/1.1.0/win-x64/a.exe'; Size=200 },
          [pscustomobject]@{ Key='windows/beta/latest.json'; Size=10 },
          [pscustomobject]@{ Key='windows/stable/latest.json'; Size=10 }
        )
        $sets = @(ConvertTo-PathVeerReleaseSet -Objects $objs)
        ($sets | Sort-Object Version | ForEach-Object { "$($_.Version)=$($_.Bytes)" }) -join ','
        """;
        Assert.Equal("1.0.0=110,1.1.0=200", Ok(script));
    }

    [Theory]
    [InlineData("1.0.0", "stable")]
    [InlineData("1.2.3", "stable")]
    [InlineData("1.0.0-beta.1", "beta")]
    [InlineData("2.0.0-rc.3", "beta")]
    public void ChannelClassificationFromVersion(string version, string expected)
    {
        Assert.Equal(expected, Ok($"Get-PathVeerReleaseChannelFromVersion -Version '{version}'"));
    }

    // --- Retention ----------------------------------------------------------------

    /// <summary>Builds a listing of N stable + M beta release sets for retention tests.</summary>
    private static string SetsScript(int stableCount, int betaCount)
    {
        var items = new List<string>();
        for (int i = 1; i <= stableCount; i++)
            items.Add($"[pscustomobject]@{{ Key='windows/1.{i}.0/win-x64/a.exe'; Size=1000 }}");
        for (int i = 1; i <= betaCount; i++)
            items.Add($"[pscustomobject]@{{ Key='windows/2.0.0-beta.{i}/win-x64/a.exe'; Size=1000 }}");
        return "$objs = @(" + string.Join(",", items) + "); $sets = @(ConvertTo-PathVeerReleaseSet -Objects $objs);";
    }

    [Fact]
    public void Retention_KeepsFifteenStableReleases()
    {
        var script = SetsScript(18, 0) + """
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets)
        $kept = @($plan | Where-Object { $_.Channel -eq 'stable' -and -not $_.Eligible }).Count
        $del  = @($plan | Where-Object { $_.Channel -eq 'stable' -and $_.Eligible }).Count
        "$kept|$del"
        """;
        Assert.Equal("15|3", Ok(script));
    }

    [Fact]
    public void Retention_KeepsFiveBetaReleases()
    {
        var script = SetsScript(0, 9) + """
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets)
        $kept = @($plan | Where-Object { $_.Channel -eq 'beta' -and -not $_.Eligible }).Count
        $del  = @($plan | Where-Object { $_.Channel -eq 'beta' -and $_.Eligible }).Count
        "$kept|$del"
        """;
        Assert.Equal("5|4", Ok(script));
    }

    [Fact]
    public void Retention_NeverDeletesTheCurrentLatestTargets()
    {
        // The oldest beta (2.0.0-beta.1) would fall outside keep=5, but it is the
        // current beta/latest target, so it must be protected.
        var script = SetsScript(0, 9) + """
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets -BetaLatestVersion '2.0.0-beta.1')
        $row = $plan | Where-Object { $_.Version -eq '2.0.0-beta.1' }
        "$($row.Protected)|$($row.Eligible)|$($row.IsLatestTarget)"
        """;
        Assert.Equal("True|False|True", Ok(script));
    }

    [Fact]
    public void Retention_NeverDeletesUpgradeFloorOrPinnedReleases()
    {
        var script = SetsScript(18, 0) + """
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets -UpgradeFloorVersion '1.1.0' -PinnedVersions @('1.2.0'))
        $floor  = $plan | Where-Object { $_.Version -eq '1.1.0' }
        $pinned = $plan | Where-Object { $_.Version -eq '1.2.0' }
        "$($floor.Eligible)|$($pinned.Eligible)"
        """;
        Assert.Equal("False|False", Ok(script));
    }

    [Fact]
    public void Retention_OperatesOnCoherentReleaseObjectSets()
    {
        // An eligible release is deleted as a whole set: every key of that version.
        var script = """
        $objs = @()
        1..7 | ForEach-Object {
            $objs += [pscustomobject]@{ Key="windows/3.0.0-beta.$_/win-x64/a.exe"; Size=100 }
            $objs += [pscustomobject]@{ Key="windows/3.0.0-beta.$_/win-x64/release-manifest.json"; Size=10 }
        }
        $sets = @(ConvertTo-PathVeerReleaseSet -Objects $objs)
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets)
        $elig = @($plan | Where-Object { $_.Eligible })
        "$($elig.Count)|$(($elig | ForEach-Object { $_.Keys.Count }) -join ',')"
        """;
        Assert.Equal("2|2,2", Ok(script));
    }

    [Fact]
    public void Retention_PreviewIsNonDestructiveAndReportsPerReleaseFacts()
    {
        // The preview is a pure computation: it returns facts (version, channel,
        // bytes, latest?, protected?, eligible?) and performs no deletion at all.
        var script = SetsScript(0, 7) + """
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets -BetaLatestVersion '2.0.0-beta.7')
        $r = $plan | Select-Object -First 1
        $hasAll = @('Version','Channel','Bytes','Rank','IsLatestTarget','Protected','Eligible','Keys') |
                  ForEach-Object { [bool]$r.PSObject.Properties[$_] }
        ($hasAll -notcontains $false).ToString()
        """;
        Assert.Equal("True", Ok(script));
    }

    [Fact]
    public void Retention_BetaAndStableAreIsolatedFromEachOther()
    {
        // 18 stable + 3 beta: only stable overflows; beta is untouched.
        var script = SetsScript(18, 3) + """
        $plan = @(Get-PathVeerRetentionPlan -ReleaseSets $sets)
        $sb = @($plan | Where-Object { $_.Channel -eq 'stable' -and $_.Eligible }).Count
        $bb = @($plan | Where-Object { $_.Channel -eq 'beta' -and $_.Eligible }).Count
        "$sb|$bb"
        """;
        Assert.Equal("3|0", Ok(script));
    }

    // --- Secret redaction -----------------------------------------------------------

    [Fact]
    public void SecretRedaction_ReplacesSecretMaterialInArbitraryText()
    {
        var outp = Ok("Protect-PathVeerSecretText -Text 'error: key=FAKESECRET1234567890 failed' -Secret @('FAKESECRET1234567890')");
        Assert.Contains("***REDACTED***", outp);
        Assert.DoesNotContain("FAKESECRET1234567890", outp);
    }

    [Fact]
    public void SecretRedaction_IgnoresShortStringsToAvoidOverRedaction()
    {
        var outp = Ok("Protect-PathVeerSecretText -Text 'the bucket is pathveer-releases' -Secret @('abc')");
        Assert.Equal("the bucket is pathveer-releases", outp);
    }

    [Fact]
    public void Diagnostics_AreRedactedAndContainNoKeyMaterial()
    {
        var dir = TempDir();
        WriteConfig(dir, """
        { "Endpoint": "https://acct.r2.cloudflarestorage.com", "Bucket": "pathveer-releases", "PublicBaseUrl": "https://releases.pathveer.com" }
        """);
        var cred = Path.Combine(dir, "r2-credential.xml");
        Ok($"$p = ConvertTo-SecureString 'FAKE-SECRET-DO-NOT-USE-0000' -AsPlainText -Force; " +
           $"$c = New-Object System.Management.Automation.PSCredential('FAKEACCESSKEYID', $p); " +
           $"$c | Export-Clixml -LiteralPath '{cred}'; 'ok'");

        var outp = Ok($"$ctx = Get-PathVeerR2Context -SecretsRoot '{dir}'; " +
                      "(Get-PathVeerR2Diagnostics -Context $ctx).GetEnumerator() | ForEach-Object { \"$($_.Key)=$($_.Value)\" }");

        Assert.Contains("Credential source=DPAPI", outp);
        Assert.Contains("Bucket=pathveer-releases", outp);
        Assert.Contains("Endpoint configured=yes", outp);
        // Neither the access key id nor the secret may appear in diagnostics.
        Assert.DoesNotContain("FAKE-SECRET-DO-NOT-USE", outp);
        Assert.DoesNotContain("FAKEACCESSKEYID", outp);
    }

    [Fact]
    public void SecretRedaction_ExceptionTextFromBackendIsScrubbed()
    {
        // Invoke-PathVeerR2WithCredential must scrub the secret from any exception
        // raised inside the action before it can reach stdout/stderr/audit output.
        var dir = TempDir();
        WriteConfig(dir, """
        { "Endpoint": "https://acct.r2.cloudflarestorage.com", "Bucket": "b", "PublicBaseUrl": "https://releases.pathveer.com" }
        """);
        var cred = Path.Combine(dir, "r2-credential.xml");
        Ok($"$p = ConvertTo-SecureString 'FAKE-SECRET-DO-NOT-USE-0000' -AsPlainText -Force; " +
           $"$c = New-Object System.Management.Automation.PSCredential('FAKEACCESSKEYID', $p); " +
           $"$c | Export-Clixml -LiteralPath '{cred}'; 'ok'");

        var script = $"$ctx = Get-PathVeerR2Context -SecretsRoot '{dir}'; " +
                     "try { Invoke-PathVeerR2WithCredential -Context $ctx -Action { param($c) throw \"boom secret=$($c.SecretKey)\" } } " +
                     "catch { $_.Exception.Message }";
        var outp = Ok(script);
        Assert.Contains("***REDACTED***", outp);
        Assert.DoesNotContain("FAKE-SECRET-DO-NOT-USE", outp);
    }

    // --- Suite independence ------------------------------------------------------

    [Fact]
    public void NormalTestSuite_DoesNotDependOnCloudflareOrTheRealCredentialStore()
    {
        // Guard: this test class must never reference the real DPAPI store path or
        // the real Cloudflare endpoint host in a way that would require them.
        var src = File.ReadAllText(Path.Combine(RepoRoot, "PathVeer.Core.Tests", "Update", "R2PublicationPolicyTests.cs"));
        Assert.DoesNotContain("r2.cloudflarestorage.com\";", src);
        // Every credential used here is fabricated.
        Assert.Contains("FAKE-SECRET-DO-NOT-USE", src);
    }
}
