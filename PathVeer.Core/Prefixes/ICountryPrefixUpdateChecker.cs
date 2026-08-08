using PathVeer.Core.Configuration;

namespace PathVeer.Core.Prefixes;

public interface ICountryPrefixUpdateChecker
{
    Task<PrefixUpdateCheckResult> CheckAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default);
}
