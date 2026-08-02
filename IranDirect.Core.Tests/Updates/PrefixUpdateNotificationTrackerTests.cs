using IranDirect.Core.Prefixes;
using IranDirect.Core.Updates;

namespace IranDirect.Core.Tests.Updates;

public sealed class PrefixUpdateNotificationTrackerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PrefixUpdateMonitorSnapshot CreateSnapshot(
        PrefixUpdateCheckStatus status,
        string? eTag = null,
        DateTimeOffset? lastModified = null,
        long? contentLength = null)
    {
        return new PrefixUpdateMonitorSnapshot
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = status,
                RemoteMetadata =
                    eTag is not null ||
                    lastModified is not null ||
                    contentLength is not null
                        ? new PrefixUpdateCheckRemoteMetadata
                        {
                            ETag = eTag,
                            LastModified = lastModified,
                            ContentLength = contentLength
                        }
                        : null,
                CheckedAt = BaseTime
            }
        };
    }

    [Fact]
    public void FirstSnapshot_UpdateAvailable_ShouldNotNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();
        var snapshot = CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable);

        tracker.ProcessSnapshot(snapshot, serviceAvailable: true, manualCheckSinceLastReset: false);

        Assert.False(tracker.ShouldNotify);
    }

    [Fact]
    public void CurrentToUpdateAvailable_ShouldNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
    }

    [Fact]
    public void UnknownToUpdateAvailable_ShouldNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Unknown),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
    }

    [Fact]
    public void FailedToUpdateAvailable_ShouldNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Failed),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
    }

    [Fact]
    public void RepeatedUpdateAvailableSameIdentity_ShouldNotNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
        tracker.MarkNotified();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.False(tracker.ShouldNotify);
    }

    [Fact]
    public void UpdateAvailableNewETag_ShouldNotifyOnce()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v3"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
    }

    [Fact]
    public void UpdateAvailableChangedLastModified_NoETag_ShouldNotifyOnce()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(
                PrefixUpdateCheckStatus.UpdateAvailable,
                lastModified: BaseTime),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);

        tracker.ProcessSnapshot(
            CreateSnapshot(
                PrefixUpdateCheckStatus.UpdateAvailable,
                lastModified: BaseTime.AddHours(1)),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
    }

    [Fact]
    public void UpdateAvailableChangedContentLength_NoETagOrLastModified_ShouldNotifyOnce()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(
                PrefixUpdateCheckStatus.UpdateAvailable,
                contentLength: 1000),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);

        tracker.ProcessSnapshot(
            CreateSnapshot(
                PrefixUpdateCheckStatus.UpdateAvailable,
                contentLength: 2000),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
    }

    [Fact]
    public void CurrentToCurrent_ShouldNotNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.False(tracker.ShouldNotify);
    }

    [Fact]
    public void ServiceUnavailable_DoesNotResetTracker()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: false,
            manualCheckSinceLastReset: false);

        Assert.False(tracker.ShouldNotify);
    }

    [Fact]
    public void ManualCheck_MarksIdentitySeen_SuppressesDuplicate()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: true);

        Assert.False(tracker.ShouldNotify);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.False(tracker.ShouldNotify);
    }

    [Fact]
    public void NoRemoteMetadata_FallbackToStatusIdentity()
    {
        var tracker = new PrefixUpdateNotificationTracker();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.True(tracker.ShouldNotify);
        tracker.MarkNotified();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        Assert.False(tracker.ShouldNotify);
    }
}