using PathVeer.Core.Configuration;
using PathVeer.Core.Prefixes;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Counting decorator over an <see cref="IPrefixUpdateChecker"/>.
/// Tracks the peak number of concurrent in-flight checks so the
/// harness can assert that scheduled and forced monitor checks never
/// overlap (production guarantee is one check at a time).
/// </summary>
public sealed class CountingPrefixUpdateChecker :
    IPrefixUpdateChecker,
    ICountryPrefixUpdateChecker
{
    private readonly IPrefixUpdateChecker _inner;
    private int _active;
    private int _peak;
    private long _total;

    public CountingPrefixUpdateChecker(IPrefixUpdateChecker inner)
    {
        _inner = inner;
    }

    public int ActiveChecks => Volatile.Read(ref _active);

    public int PeakActiveChecks => Volatile.Read(ref _peak);

    public long TotalChecks => Interlocked.Read(ref _total);

    public Task<PrefixUpdateCheckResult> CheckAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default) =>
        CheckAsync(cancellationToken);

    public async Task<PrefixUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        int active = Interlocked.Increment(ref _active);
        int peak;
        while (active > (peak = Volatile.Read(ref _peak)))
        {
            if (Interlocked.CompareExchange(
                    ref _peak,
                    active,
                    peak) == peak)
            {
                break;
            }
        }

        Interlocked.Increment(ref _total);

        try
        {
            return await _inner.CheckAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
