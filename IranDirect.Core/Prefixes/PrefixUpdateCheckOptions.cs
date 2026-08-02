namespace IranDirect.Core.Prefixes;

public sealed class PrefixUpdateCheckOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public static void Validate(
        PrefixUpdateCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Prefix update check Timeout must be positive.");
        }
    }
}
