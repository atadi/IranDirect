namespace IranDirect.Core.Prefixes;

/// <summary>
/// Country-agnostic update checker used by the standalone
/// <see cref="PrefixUpdateMonitor"/>. The country is resolved at call time via
/// the configured provider; explicit per-country checks use
/// <see cref="ICountryPrefixUpdateChecker"/>.
/// </summary>
public interface IPrefixUpdateChecker
{
    Task<PrefixUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken);
}
