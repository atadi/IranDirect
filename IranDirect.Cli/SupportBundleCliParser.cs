namespace IranDirect.Cli;

public sealed record SupportBundleCliParseResult(
    bool IsValid,
    string? OutputPath,
    string? Error);

public static class SupportBundleCliParser
{
    public static SupportBundleCliParseResult Parse(
        string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return new SupportBundleCliParseResult(
                IsValid: true,
                OutputPath: null,
                Error: null);
        }

        if (args.Length > 1)
        {
            return new SupportBundleCliParseResult(
                IsValid: false,
                OutputPath: null,
                Error: "Only one optional path argument is accepted.");
        }

        string path = args[0];

        if (string.IsNullOrWhiteSpace(path))
        {
            return new SupportBundleCliParseResult(
                IsValid: false,
                OutputPath: null,
                Error: "Path must not be empty.");
        }

        return new SupportBundleCliParseResult(
            IsValid: true,
            OutputPath: path,
            Error: null);
    }
}
