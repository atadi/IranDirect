using System.Net.NetworkInformation;
using System.Net.Sockets;
using IranDirect.Core.Models;

namespace IranDirect.Core.Networking;

public sealed class GatewayDetector
{
    private static readonly string[] ExcludedTerms =
    [
        "openvpn",
        "tap-windows",
        "wintun",
        "wireguard",
        "vpn"
    ];

    public DirectGateway Detect()
    {
        List<DirectGateway> candidates = [];

        foreach (NetworkInterface networkInterface
                 in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus !=
                OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is
                NetworkInterfaceType.Loopback or
                NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            string searchableName =
                $"{networkInterface.Name} {networkInterface.Description}";

            if (ExcludedTerms.Any(term =>
                searchableName.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            IPInterfaceProperties properties =
                networkInterface.GetIPProperties();

            GatewayIPAddressInformation? gateway =
                properties.GatewayAddresses.FirstOrDefault(item =>
                    item.Address.AddressFamily ==
                    AddressFamily.InterNetwork
                    && !item.Address.Equals(
                        System.Net.IPAddress.Any));

            if (gateway is null)
            {
                continue;
            }

            IPv4InterfaceProperties ipv4 =
                properties.GetIPv4Properties();

            candidates.Add(new DirectGateway
            {
                Address = gateway.Address,
                InterfaceIndex = checked((uint)ipv4.Index),
                InterfaceName = networkInterface.Name,
                InterfaceMetric = ipv4.Index
            });
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No suitable non-VPN IPv4 gateway was detected.");
        }

        // Improve this selection later using GetBestRoute2.
        return candidates[0];
    }
}