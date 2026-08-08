using PathVeer.Core.Ipc;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class SupportBundleDefaultFileNameTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 14, 22, 1, TimeSpan.Zero);

    [Fact]
    public void Build_ProducesDefaultFilename()
    {
        string fileName = SupportBundleDefaultFileName.Build(
            new FixedTimeProvider(FixedTime),
            FixedTime);

        Assert.StartsWith(
            "IranDirect-Support-",
            fileName);
        Assert.EndsWith(".zip", fileName);
    }

    [Fact]
    public void Build_UsesCurrentTimeWhenNowIsNull()
    {
        // Passing time on the local-time zone guarantees both
        // paths produce the same stamp regardless of the
        // runner's timezone, because the function uses
        // ToLocalTime for both call sites.
        DateTimeOffset localStamp =
            FixedTime.ToLocalTime();

        string fileName = SupportBundleDefaultFileName.Build(
            new FixedTimeProvider(localStamp));

        string expectedTime =
            $"IranDirect-Support-{localStamp:yyyyMMdd-HHmmss}.zip";

        Assert.Equal(expectedTime, fileName);
    }

    [Fact]
    public void Build_DefaultExtensionIsZip()
    {
        string fileName = SupportBundleDefaultFileName.Build(
            new FixedTimeProvider(FixedTime),
            FixedTime);

        Assert.EndsWith(".zip", fileName);
    }

    [Fact]
    public void Build_HasExpectedPrefix()
    {
        string fileName = SupportBundleDefaultFileName.Build(
            new FixedTimeProvider(FixedTime),
            FixedTime);

        Assert.StartsWith(
            "IranDirect-Support-",
            fileName);
    }
}

file sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}
