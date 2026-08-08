using PathVeer.Core;
using PathVeer.Core.Cli;
using PathVeer.Core.Configuration;
using PathVeer.Core.Ipc;

namespace PathVeer.Cli;

/// <summary>
/// CLI surface for the direct-country routing selection.
///
/// Commands:
///   country get            -> prints the requested ISO country code
///   country set &lt;ISO2&gt;        -> persists the requested country and
///                              triggers a prefix refresh for it
///   country list           -> lists all recognized ISO alpha-2 codes
///
/// Country identity is always the ISO alpha-2 code; display names are
/// presentation-only. Validation is delegated entirely to
/// <see cref="DirectCountryCode"/> so no raw string logic is duplicated here.
/// </summary>
public static class CountryCliRunner
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitTimeout = 3;
    private const int ExitCanceled = 4;
    private const int ExitUsage = 6;

    public static async Task<int> RunAsync(
        string[] args,
        ICustomRouteCommandSender sender,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        string subcommand =
            args.FirstOrDefault()?.ToLowerInvariant() ?? "";

        try
        {
            return subcommand switch
            {
                "get" => await GetAsync(sender, stdout, stderr),
                "set" => await SetAsync(args, sender, stdout, stderr),
                "list" => List(stdout),
                _ => Usage(stderr)
            };
        }
        catch (TimeoutException exception)
        {
            stderr.WriteLine(exception.Message);
            return ExitTimeout;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("The operation was canceled.");
            return ExitCanceled;
        }
        catch (Exception exception)
        {
            stderr.WriteLine(
                $"PathVeer command failed: {exception.Message}");
            return ExitFailure;
        }
    }

    private static async Task<int> GetAsync(
        ICustomRouteCommandSender sender,
        TextWriter stdout,
        TextWriter stderr)
    {
        ServiceResponse response =
            await sender.SendAsync(
                PathVeerCommand.GetConfiguration);

        if (!response.Success || response.Configuration is null)
        {
            string prefix =
                string.IsNullOrWhiteSpace(response.ErrorCode)
                    ? ""
                    : $"[{response.ErrorCode}] ";
            stderr.WriteLine(prefix + response.Message);
            return ExitFailure;
        }

        DirectCountryCode requested =
            response.Configuration.DirectCountryCode ?? DirectCountryCode.IR;

        stdout.WriteLine(requested.Code);
        return ExitSuccess;
    }

    private static async Task<int> SetAsync(
        string[] args,
        ICustomRouteCommandSender sender,
        TextWriter stdout,
        TextWriter stderr)
    {
        string? raw = args.ElementAtOrDefault(1);

        if (string.IsNullOrWhiteSpace(raw) ||
            !DirectCountryCode.TryParse(
                raw,
                out DirectCountryCode? country) ||
            country is null)
        {
            stderr.WriteLine(
                "Usage: PathVeer.Cli country set <ISO2>  " +
                "(e.g. IR, IQ, RO)");
            return ExitUsage;
        }

        ServiceResponse response =
            await sender.SendAsync(
                PathVeerCommand.SetConfigurationDirectCountry,
                country.Code);

        if (!response.Success)
        {
            string prefix =
                string.IsNullOrWhiteSpace(response.ErrorCode)
                    ? ""
                    : $"[{response.ErrorCode}] ";
            stderr.WriteLine(prefix + response.Message);
            return ExitFailure;
        }

        stdout.WriteLine(response.Message);

        // Partial set: config accepted but the dataset could not be fetched
        // (source unavailable / no cache). Keep exit non-zero so callers and
        // users can detect the requested policy is not yet effective, while the
        // config remains set (fail-closed, retryable).
        if (response.PrefixRefreshed == false)
        {
            return ExitFailure;
        }

        return ExitSuccess;
    }

    private static int List(TextWriter stdout)
    {
        stdout.WriteLine("Code  Country");
        stdout.WriteLine("----  -------");

        foreach (DirectCountryCode code in
                 DirectCountryCode.AllSupported)
        {
            stdout.WriteLine(
                $"{code.Code,-5}{code.DisplayName}");
        }

        return ExitSuccess;
    }

    private static int Usage(TextWriter stderr)
    {
        stderr.WriteLine(
            "Usage: PathVeer.Cli country <get|set|list>");
        stderr.WriteLine("  country get");
        stderr.WriteLine("  country set <ISO2>   (e.g. IR, IQ, RO)");
        stderr.WriteLine("  country list");
        return ExitUsage;
    }
}
