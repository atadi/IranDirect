using System.Net;
using System.Net.Sockets;

namespace IranDirect.Core.Vpn;

public sealed class VpnEndpointResolver
{
    public async Task<IReadOnlyList<ResolvedVpnEndpoint>>
        ResolveAsync(
            IReadOnlyCollection<VpnEndpointDefinition> endpoints,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        List<ResolvedVpnEndpoint> resolved = [];

        foreach (VpnEndpointDefinition endpoint in endpoints)
        {
            IPAddress[] addresses;

            if (IPAddress.TryParse(
                    endpoint.Host,
                    out IPAddress? literalAddress))
            {
                addresses = [literalAddress];
            }
            else
            {
                addresses =
                    await Dns.GetHostAddressesAsync(
                        endpoint.Host,
                        cancellationToken);
            }

            foreach (IPAddress address in addresses
                         .Where(item =>
                             item.AddressFamily ==
                             AddressFamily.InterNetwork)
                         .Distinct())
            {
                resolved.Add(
                    new ResolvedVpnEndpoint
                    {
                        Host = endpoint.Host,
                        Address = address.ToString(),
                        Port = endpoint.Port,
                        Protocol = endpoint.Protocol
                    });
            }
        }

        ResolvedVpnEndpoint[] distinct = resolved
            .GroupBy(
                endpoint =>
                    $"{endpoint.Address}|" +
                    $"{endpoint.Port}|" +
                    $"{endpoint.Protocol}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (distinct.Length == 0)
        {
            throw new InvalidOperationException(
                "No IPv4 VPN endpoint addresses could be resolved.");
        }

        return distinct;
    }
}