using PathVeer.Core.Prefixes;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class PrefixUpdateCheckResultFormatterTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildResultText_Current_ShowsConciseResult()
    {
        PrefixUpdateMonitorSnapshot snapshot = new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Current,
                CheckedAt = BaseTime
            },
            LastCheckedAt = BaseTime,
            LastSuccessfulCheckAt = BaseTime
        };

        string text = PrefixUpdateCheckResultFormatter
            .BuildResultText(snapshot);

        Assert.Contains("Status: Current", text);
        Assert.Contains("Checked:", text);
        Assert.Contains("Consecutive failures: 0", text);
        Assert.DoesNotContain("Reason:", text);
    }

    [Fact]
    public void BuildResultText_Failed_IncludesReason()
    {
        PrefixUpdateMonitorSnapshot snapshot = new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CheckedAt = BaseTime,
                Reason = "Remote check failed: network down"
            },
            LastCheckedAt = BaseTime,
            ConsecutiveFailures = 2
        };

        string text = PrefixUpdateCheckResultFormatter
            .BuildResultText(snapshot);

        Assert.Contains("Status: Failed", text);
        Assert.Contains("Consecutive failures: 2", text);
        Assert.Contains(
            "Reason: Remote check failed: network down",
            text);
    }

    [Fact]
    public void BuildResultText_NullResult_ShowsUnknown()
    {
        string text = PrefixUpdateCheckResultFormatter
            .BuildResultText(
                new PrefixUpdateMonitorSnapshot());

        Assert.Contains("Status: Unknown", text);
        Assert.Contains("Checked: -", text);
    }

    [Theory]
    [InlineData(PrefixUpdateCheckStatus.Current, false)]
    [InlineData(PrefixUpdateCheckStatus.UpdateAvailable, false)]
    [InlineData(PrefixUpdateCheckStatus.Unknown, false)]
    [InlineData(PrefixUpdateCheckStatus.Failed, true)]
    public void IsFailure_OnlyFailedIsFailure(
        PrefixUpdateCheckStatus status,
        bool expected)
    {
        PrefixUpdateMonitorSnapshot snapshot = new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = status,
                CheckedAt = BaseTime
            }
        };

        Assert.Equal(
            expected,
            PrefixUpdateCheckResultFormatter.IsFailure(
                snapshot));
    }

    [Fact]
    public void IsFailure_NullResult_IsNotFailure()
    {
        Assert.False(
            PrefixUpdateCheckResultFormatter.IsFailure(
                new PrefixUpdateMonitorSnapshot()));
    }
}
