using PathVeer.Core.Installer;
using Xunit;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase service-authority slice — friendly display version (DEFECT #3).
///
/// The assembly informational version carries full provenance
/// (e.g. 1.0.0-devsign.6+&lt;commit&gt;). Normal installer UI must show the
/// friendly version WITHOUT the Git SHA, while the full provenance remains
/// available for diagnostics. Prerelease SemVer tags must survive normalization.
/// </summary>
public class SetupVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta.1")]
    [InlineData("1.0.0-devsign.6+abcdef123456", "1.0.0-devsign.6")]
    [InlineData("1.0.0-devsign.5+67ca14c77dcfda3a33b708edb76f96f901e70f83",
        "1.0.0-devsign.5")]
    [InlineData("2.3.4-rc.2+deadbeef", "2.3.4-rc.2")]
    // Already-friendly strings are returned unchanged.
    [InlineData("1.0.0-devsign.6", "1.0.0-devsign.6")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void Friendly_StripsCommit_KeepsPrerelease(string? input, string expected)
    {
        Assert.Equal(expected, SetupVersion.Friendly(input));
    }

    [Fact]
    public void Full_PreservesProvenance()
    {
        const string provenance = "1.0.0-devsign.6+67ca14c77dcfda3a33b708edb76f96f901e70f83";
        Assert.Equal(provenance, SetupVersion.Full(provenance));
    }
}
