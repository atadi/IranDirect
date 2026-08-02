namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteDnsCacheOptions
{
    public TimeSpan DnsCacheDuration { get; init; } =
        TimeSpan.FromMinutes(15);

    public TimeSpan DnsMaxStaleDuration { get; init; } =
        TimeSpan.FromDays(1);

    public TimeSpan DnsTimeout { get; init; } =
        TimeSpan.FromSeconds(5);

    public static void Validate(CustomRouteDnsCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DnsCacheDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DnsCacheDuration must be greater than zero.");
        }

        if (options.DnsMaxStaleDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DnsMaxStaleDuration must be greater than zero.");
        }

        if (options.DnsTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DnsTimeout must be greater than zero.");
        }

        if (options.DnsMaxStaleDuration < options.DnsCacheDuration)
        {
            throw new ArgumentException(
                "DnsMaxStaleDuration must not be earlier than " +
                "DnsCacheDuration.",
                nameof(options));
        }
    }
}
