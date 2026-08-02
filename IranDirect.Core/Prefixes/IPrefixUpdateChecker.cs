namespace IranDirect.Core.Prefixes;

public interface IPrefixUpdateChecker
{
    Task<PrefixUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken);
}
