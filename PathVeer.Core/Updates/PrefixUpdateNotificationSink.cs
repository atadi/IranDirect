namespace PathVeer.Core.Updates;

public interface IPrefixUpdateNotificationSink
{
    void NotifyBalloon(
        string title,
        string text);
}

public sealed class NullPrefixUpdateNotificationSink :
    IPrefixUpdateNotificationSink
{
    public static readonly IPrefixUpdateNotificationSink Instance =
        new NullPrefixUpdateNotificationSink();

    private NullPrefixUpdateNotificationSink() { }

    public void NotifyBalloon(string title, string text) { }
}
