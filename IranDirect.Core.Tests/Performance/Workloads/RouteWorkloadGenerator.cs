using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Tests.Performance.Workloads;

public sealed class RouteWorkloadGenerator
{
    public DesiredPrefixRoute DesiredPrefix(string prefix, int index, int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            Gateway = DesiredGateway(index, seed),
            InterfaceIndex = DesiredInterface(index),
            Metric = 5
        };

    public ObservedRoute ObservedPrefix(string prefix, int index, int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            NextHop = DesiredGateway(index, seed),
            InterfaceIndex = DesiredInterface(index),
            Metric = 5
        };

    public RouteInventoryItem PrefixInventoryItem(string prefix, int index, int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            Gateway = DesiredGateway(index, seed),
            InterfaceIndex = DesiredInterface(index),
            Metric = 5
        };

    public ObservedRoute ObservedPrefixMismatchedGateway(
        string prefix,
        int index,
        int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            NextHop = MismatchedGateway(index, seed),
            InterfaceIndex = DesiredInterface(index),
            Metric = 5
        };

    public RouteInventoryItem PrefixInventoryItemMismatchedGateway(
        string prefix,
        int index,
        int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            Gateway = MismatchedGateway(index, seed),
            InterfaceIndex = DesiredInterface(index),
            Metric = 5
        };

    public ObservedRoute ObservedPrefixMismatchedInterface(
        string prefix,
        int index,
        int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            NextHop = DesiredGateway(index, seed),
            InterfaceIndex = MismatchedInterface(index),
            Metric = 5
        };

    public RouteInventoryItem PrefixInventoryItemMismatchedInterface(
        string prefix,
        int index,
        int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            Gateway = DesiredGateway(index, seed),
            InterfaceIndex = MismatchedInterface(index),
            Metric = 5
        };

    public DesiredEndpointRoute DesiredEndpoint(string prefix, int index, int seed) =>
        new()
        {
            Host = $"vpn-{index}.endpoint.example.invalid",
            Address = StripPrefixLength(prefix),
            Port = 1194 + (index % 16),
            Protocol = "udp",
            DestinationPrefix = prefix,
            Gateway = EndpointGateway(index, seed),
            InterfaceIndex = EndpointInterface(index),
            Metric = 1
        };

    public ObservedRoute ObservedEndpointRoute(string prefix, int index, int seed) =>
        new()
        {
            DestinationPrefix = prefix,
            NextHop = EndpointGateway(index, seed),
            InterfaceIndex = EndpointInterface(index),
            Metric = 1
        };

    public ObservedVpnEndpoint VpnEndpoint(string prefix, int index, int seed) =>
        new()
        {
            Host = $"vpn-{index}.endpoint.example.invalid",
            Address = StripPrefixLength(prefix),
            Port = 1194 + (index % 16),
            Protocol = "udp"
        };

    public VpnEndpointInventoryItem EndpointInventoryItem(
        string prefix,
        int index,
        int seed,
        bool addedByIranDirect) =>
        new()
        {
            Host = $"vpn-{index}.endpoint.example.invalid",
            Address = StripPrefixLength(prefix),
            Port = 1194 + (index % 16),
            Protocol = "udp",
            DestinationPrefix = prefix,
            Gateway = EndpointGateway(index, seed),
            InterfaceIndex = EndpointInterface(index),
            Metric = 1,
            AddedByIranDirect = addedByIranDirect,
            IsCurrent = true
        };

    public string DesiredGateway(int index, int seed) =>
        DrawGateway(DeterministicRandom.Mix(seed, index, 1));

    public string MismatchedGateway(int index, int seed)
    {
        string desired = DesiredGateway(index, seed);
        string candidate = DrawGateway(DeterministicRandom.Mix(seed, index, 2));
        return string.Equals(
            candidate,
            desired,
            StringComparison.Ordinal)
            ? FlipLastOctet(candidate)
            : candidate;
    }

    public uint DesiredInterface(int index) =>
        1 + (uint)(index % 24);

    public uint MismatchedInterface(int index) =>
        DesiredInterface(index) + 25;

    public string EndpointGateway(int index, int seed) =>
        DrawGateway(DeterministicRandom.Mix(seed, index, 3));

    public uint EndpointInterface(int index) =>
        1 + (uint)(index % 24);

    private static string DrawGateway(ulong seed)
    {
        var random = new SeededRandomStream(seed);
        int first = random.Next(1, 224);
        if (first == 127)
        {
            first = 126;
        }

        int second = random.Next(1, 255);
        int third = random.Next(1, 255);
        int fourth = random.Next(1, 254);
        return $"{first}.{second}.{third}.{fourth}";
    }

    private static string FlipLastOctet(string address)
    {
        int separator = address.LastIndexOf('.');
        int fourth = int.Parse(address.AsSpan(separator + 1));
        int flipped = fourth == 253 ? 1 : fourth + 1;
        return $"{address[..separator]}.{flipped}";
    }

    private static string StripPrefixLength(string prefix) =>
        prefix.Remove(prefix.Length - 3);
}
