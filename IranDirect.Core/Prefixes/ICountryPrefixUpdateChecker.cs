using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

public interface ICountryPrefixUpdateChecker
{
    Task<PrefixUpdateCheckResult> CheckAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default);
}
