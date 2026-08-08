using System.Net;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Deterministic DNS resolver used as the injected lookup function for
/// the production <see cref="CustomRouteResolver"/>. Returns scripted
/// IPv4 addresses and never touches the network. Resolution failures
/// are simulated either by a temporary failing domain (this resolver
/// throws) or by the <see cref="FaultInjectionPoint.DnsLookup"/> fault
/// applied by the production resolver itself.
/// </summary>
public sealed class ScriptedDnsResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<IPAddress>> _addresses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failingDomains =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _resolutionCounts =
        new(StringComparer.OrdinalIgnoreCase);

    public int TotalResolutionCount
    {
        get
        {
            lock (_gate)
            {
                return _resolutionCounts.Values.Sum();
            }
        }
    }

    public void SetAddresses(
        string domain,
        params IPAddress[] addresses)
    {
        lock (_gate)
        {
            _addresses[domain] = addresses;
        }
    }

    public void SetFailing(string domain, bool failing)
    {
        lock (_gate)
        {
            if (failing)
            {
                _failingDomains.Add(domain);
            }
            else
            {
                _failingDomains.Remove(domain);
            }
        }
    }

    public int GetResolutionCount(string domain)
    {
        lock (_gate)
        {
            return _resolutionCounts.GetValueOrDefault(domain);
        }
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _resolutionCounts[host] =
                _resolutionCounts.GetValueOrDefault(host) + 1;

            if (_failingDomains.Contains(host))
            {
                throw new InvalidOperationException(
                    $"Scripted DNS failure for {host}.");
            }

            if (!_addresses.TryGetValue(host, out IReadOnlyList<IPAddress>? addresses))
            {
                throw new InvalidOperationException(
                    $"No scripted addresses for {host}.");
            }

            return Task.FromResult(addresses);
        }
    }
}
