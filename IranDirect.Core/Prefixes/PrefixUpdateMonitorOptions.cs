namespace IranDirect.Core.Prefixes;

public sealed class PrefixUpdateMonitorOptions
{
    public static readonly TimeSpan MinimumInterval =
        TimeSpan.FromMinutes(15);

    public static readonly TimeSpan MaximumInterval =
        TimeSpan.FromDays(30);

    public TimeSpan Interval { get; init; } =
        TimeSpan.FromHours(12);

    public static void Validate(
        PrefixUpdateMonitorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Interval < MinimumInterval)
        {
            throw new InvalidOperationException(
                "Prefix update monitor Interval must be " +
                "at least 15 minutes.");
        }

        if (options.Interval > MaximumInterval)
        {
            throw new InvalidOperationException(
                "Prefix update monitor Interval must not " +
                "exceed 30 days.");
        }
    }
}
