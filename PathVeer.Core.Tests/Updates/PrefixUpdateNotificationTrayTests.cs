using PathVeer.Core.Prefixes;
using PathVeer.Core.Updates;

namespace PathVeer.Core.Tests.Updates;

public sealed class PrefixUpdateNotificationTrayTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeNotificationSink :
        IPrefixUpdateNotificationSink
    {
        public List<(string Title, string Text)> Notifications { get; } = new();

        public void NotifyBalloon(string title, string text)
        {
            Notifications.Add((title, text));
        }
    }

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
    public void Poll_UpdateAvailableTransition_TriggersNotification()
    {
        var tracker = new PrefixUpdateNotificationTracker();
        var sink = new FakeNotificationSink();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        if (tracker.ShouldNotify)
        {
            sink.NotifyBalloon(
                "IranDirect",
                "A newer Iran prefix dataset is available.");

            tracker.MarkNotified();
        }

        Assert.Single(sink.Notifications);
        Assert.Equal("IranDirect", sink.Notifications[0].Title);
        Assert.Equal(
            "A newer Iran prefix dataset is available.",
            sink.Notifications[0].Text);
    }

    [Fact]
    public void Poll_RepeatedUpdateAvailable_SuppressesDuplicate()
    {
        var tracker = new PrefixUpdateNotificationTracker();
        var sink = new FakeNotificationSink();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        if (tracker.ShouldNotify)
        {
            sink.NotifyBalloon(
                "IranDirect",
                "A newer Iran prefix dataset is available.");

            tracker.MarkNotified();
        }

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        if (tracker.ShouldNotify)
        {
            sink.NotifyBalloon(
                "IranDirect",
                "A newer Iran prefix dataset is available.");

            tracker.MarkNotified();
        }

        Assert.Single(sink.Notifications);
    }

    [Fact]
    public void Poll_ManualCheckSuppressesDuplicate()
    {
        var tracker = new PrefixUpdateNotificationTracker();
        var sink = new FakeNotificationSink();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: true);

        if (tracker.ShouldNotify)
        {
            sink.NotifyBalloon(
                "IranDirect",
                "A newer Iran prefix dataset is available.");

            tracker.MarkNotified();
        }

        Assert.Empty(sink.Notifications);
    }

    [Fact]
    public void Poll_ServiceUnavailable_DoesNotResetTracker()
    {
        var tracker = new PrefixUpdateNotificationTracker();
        var sink = new FakeNotificationSink();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.Current),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: false,
            manualCheckSinceLastReset: false);

        if (tracker.ShouldNotify)
        {
            sink.NotifyBalloon(
                "IranDirect",
                "A newer Iran prefix dataset is available.");

            tracker.MarkNotified();
        }

        Assert.Empty(sink.Notifications);
    }

    [Fact]
    public void Startup_ExistingUpdateAvailable_DoesNotNotify()
    {
        var tracker = new PrefixUpdateNotificationTracker();
        var sink = new FakeNotificationSink();

        tracker.ProcessSnapshot(
            CreateSnapshot(PrefixUpdateCheckStatus.UpdateAvailable, eTag: "v2"),
            serviceAvailable: true,
            manualCheckSinceLastReset: false);

        if (tracker.ShouldNotify)
        {
            sink.NotifyBalloon(
                "IranDirect",
                "A newer Iran prefix dataset is available.");

            tracker.MarkNotified();
        }

        Assert.Empty(sink.Notifications);
    }
}