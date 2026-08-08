using PathVeer.Core.Cli;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Tests.Cli;

public sealed class PrefixUpdateCheckCliRendererTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Render_AllFields_Present()
    {
        PrefixUpdateMonitorSnapshot snapshot = new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.UpdateAvailable,
                CheckedAt = BaseTime,
                RemoteMetadata =
                    new PrefixUpdateCheckRemoteMetadata
                    {
                        ETag = "\"etag9\"",
                        LastModified = BaseTime.AddHours(2),
                        ContentLength = 4096
                    }
            },
            LastCheckedAt = BaseTime,
            LastSuccessfulCheckAt = BaseTime,
            ConsecutiveFailures = 0,
            Running = true,
            Checking = false
        };

        string[] lines = PrefixUpdateCheckCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains(
            "=== Prefix Update Check ===",
            lines);
        Assert.Contains("Status: Update Available", lines);
        Assert.Contains(
            "Checked at: 2026-01-01 00:00:00 UTC",
            lines);
        Assert.Contains(
            "Last successful check: 2026-01-01 00:00:00 UTC",
            lines);
        Assert.Contains("Consecutive failures: 0", lines);
        Assert.Contains("Remote ETag: \"etag9\"", lines);
        Assert.Contains(
            "Remote Last Modified: 2026-01-01 02:00:00 UTC",
            lines);
        Assert.Contains("Remote Content Length: 4096", lines);
        Assert.Contains("Reason: -", lines);
    }

    [Fact]
    public void Render_NullCurrentResult_ShowsUnknownAndDashes()
    {
        PrefixUpdateMonitorSnapshot snapshot = new();

        string[] lines = PrefixUpdateCheckCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status: Unknown", lines);
        Assert.Contains("Checked at: -", lines);
        Assert.Contains("Last successful check: -", lines);
        Assert.Contains("Consecutive failures: 0", lines);
        Assert.Contains("Remote ETag: -", lines);
        Assert.Contains("Remote Last Modified: -", lines);
        Assert.Contains("Remote Content Length: -", lines);
        Assert.Contains("Reason: -", lines);
    }

    [Fact]
    public void Render_Failed_IncludesReasonAndFailures()
    {
        PrefixUpdateMonitorSnapshot snapshot = new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CheckedAt = BaseTime,
                Reason = "Remote check timed out after 5 seconds."
            },
            LastCheckedAt = BaseTime,
            ConsecutiveFailures = 3
        };

        string[] lines = PrefixUpdateCheckCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status: Failed", lines);
        Assert.Contains("Consecutive failures: 3", lines);
        Assert.Contains(
            "Reason: Remote check timed out after 5 seconds.",
            lines);
        Assert.Contains("Last successful check: -", lines);
    }

    [Fact]
    public void Render_UnknownStatus_IncludesReason()
    {
        PrefixUpdateMonitorSnapshot snapshot = new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Unknown,
                CheckedAt = BaseTime,
                Reason = "No local metadata available."
            },
            LastCheckedAt = BaseTime,
            LastSuccessfulCheckAt = BaseTime
        };

        string[] lines = PrefixUpdateCheckCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status: Unknown", lines);
        Assert.Contains(
            "Reason: No local metadata available.",
            lines);
    }
}
