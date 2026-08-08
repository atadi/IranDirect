using PathVeer.Core.Configuration;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Scripted <see cref="ICountryPrefixSource"/> used by the lifecycle
/// harness. Never performs a real download: it replays enqueued
/// outcomes (with content hashes computed through the production
/// <see cref="PrefixContentHasher"/>) and repeats the last outcome
/// when the queue is exhausted. The production
/// <see cref="FaultInjectionPoint.HttpRequest"/> fault is honoured.
/// </summary>
public sealed class ScriptedPrefixSource : ICountryPrefixSource
{
    private readonly IFaultInjectionPolicy _faultPolicy;
    private readonly object _gate = new();
    private readonly List<Func<CancellationToken, PrefixSourceFetchResult>> _script = [];
    private Func<CancellationToken, PrefixSourceFetchResult>? _last;

    public ScriptedPrefixSource(
        IFaultInjectionPolicy? faultPolicy = null)
    {
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public DirectCountryCode? LastRequestedCountry { get; private set; }

    public PrefixSourceDescriptor Descriptor { get; } =
        new()
        {
            Id = "simulation-prefix-source",
            DisplayName = "Simulation Prefix Source",
            Uri = "https://example.invalid/prefixes.txt",
            Format = "cidr",
            ParserVersion = "1.0"
        };

    public void EnqueueFetch(
        IReadOnlyList<string> prefixes,
        bool notModified = false,
        string? etag = null,
        DateTimeOffset? lastModified = null)
    {
        string[] distinct = prefixes
            .Select(prefix => prefix.Trim())
            .Where(prefix => prefix.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(prefix => prefix, StringComparer.Ordinal)
            .ToArray();

        _script.Add(_ => CreateResult(distinct, notModified, etag, lastModified));
    }

    public void EnqueueFailure(string message) =>
        _script.Add(_ => throw new InvalidOperationException(message));

    public PrefixSourceDescriptor GetDescriptor(
        DirectCountryCode country) => Descriptor;

    public Task<PrefixSourceFetchResult> FetchAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);

        LastRequestedCountry = country;

        cancellationToken.ThrowIfCancellationRequested();

        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy,
                FaultInjectionPoint.HttpRequest))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.HttpRequest);
        }

        Func<CancellationToken, PrefixSourceFetchResult> factory;

        lock (_gate)
        {
            if (_script.Count > 0)
            {
                factory = _script[0];
                _script.RemoveAt(0);
                _last = factory;
            }
            else if (_last is not null)
            {
                factory = _last;
            }
            else
            {
                factory = _ => CreateResult(
                    [],
                    notModified: true,
                    etag: null,
                    lastModified: null);
            }
        }

        return Task.FromResult(factory(cancellationToken));
    }

    private PrefixSourceFetchResult CreateResult(
        IReadOnlyList<string> prefixes,
        bool notModified,
        string? etag,
        DateTimeOffset? lastModified)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;

        return new PrefixSourceFetchResult
        {
            Source = Descriptor,
            Prefixes = prefixes,
            StartedAt = started,
            CompletedAt = started + TimeSpan.FromMilliseconds(1),
            Duration = TimeSpan.FromMilliseconds(1),
            ETag = etag,
            LastModified = lastModified,
            ContentHash = PrefixContentHasher.ComputeHash(prefixes),
            ContentLength = prefixes.Count,
            NotModified = notModified
        };
    }
}
