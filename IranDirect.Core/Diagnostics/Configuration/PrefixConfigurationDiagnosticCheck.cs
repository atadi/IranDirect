using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Diagnostics.Configuration;

public sealed class PrefixConfigurationDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly CountryPrefixStore _prefixStore;
    private readonly Func<DirectCountryCode> _countryResolver;

    public PrefixConfigurationDiagnosticCheck(
        CountryPrefixStore prefixStore,
        Func<DirectCountryCode> countryResolver)
    {
        ArgumentNullException.ThrowIfNull(prefixStore);
        ArgumentNullException.ThrowIfNull(countryResolver);

        _prefixStore = prefixStore;
        _countryResolver = countryResolver;
    }

    public string Id => "prefix-configuration";
    public string Title => "Prefix configuration";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> prefixes =
                await _prefixStore.LoadPrefixesAsync(
                    _countryResolver(),
                    cancellationToken);

            if (prefixes.Count == 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        "No IPv4 prefixes are available.",
                    SuggestedAction:
                        "Ensure the prefix file exists and " +
                        "contains valid CIDR entries.");
            }

            List<string> invalidLines = [];
            HashSet<string> seen =
                new(StringComparer.OrdinalIgnoreCase);
            List<string> duplicates = [];

            foreach (string prefix in prefixes)
            {
                if (!TryParseCidr(prefix))
                {
                    invalidLines.Add(prefix);
                    continue;
                }

                if (!seen.Add(prefix))
                {
                    duplicates.Add(prefix);
                }
            }

            if (invalidLines.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Invalid CIDR entries: " +
                        $"{string.Join(", ", invalidLines)}.",
                    SuggestedAction:
                        "Ensure all prefix entries are valid " +
                        "CIDR notation (e.g. 192.168.0.0/16).");
            }

            if (duplicates.Count > 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message:
                        $"Duplicate prefixes found: " +
                        $"{string.Join(", ", duplicates)}.",
                    SuggestedAction:
                        "Remove duplicate prefix entries.");
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message:
                    $"{prefixes.Count} prefix(es) loaded " +
                    "successfully.",
                SuggestedAction: null);
        }
        catch (Exception exception)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    $"Could not read prefix file: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the prefix file exists and is " +
                    "readable.");
        }
    }

    private static bool TryParseCidr(string cidr)
    {
        int slashIndex = cidr.IndexOf('/');

        if (slashIndex < 0)
        {
            return false;
        }

        string addressPart = cidr[..slashIndex];
        string prefixPart = cidr[(slashIndex + 1)..];

        if (!System.Net.IPAddress.TryParse(
            addressPart,
            out System.Net.IPAddress? address))
        {
            return false;
        }

        if (!int.TryParse(prefixPart, out int prefixLength))
        {
            return false;
        }

        if (address.AddressFamily ==
            System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return prefixLength >= 0 && prefixLength <= 32;
        }

        if (address.AddressFamily ==
            System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return prefixLength >= 0 && prefixLength <= 128;
        }

        return false;
    }
}