namespace PathVeer.Core.Prefixes;

public sealed class PrefixSourceHistoryOptions
{
    public int RetentionCount { get; init; } = 100;

    public static void Validate(
        PrefixSourceHistoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RetentionCount < 1)
        {
            throw new InvalidOperationException(
                "Prefix source history RetentionCount must be " +
                "at least 1.");
        }

        if (options.RetentionCount > 10_000)
        {
            throw new InvalidOperationException(
                "Prefix source history RetentionCount must not " +
                "exceed 10000.");
        }
    }
}
