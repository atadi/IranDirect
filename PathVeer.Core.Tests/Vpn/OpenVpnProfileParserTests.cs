using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Vpn;

public sealed class OpenVpnProfileParserTests
{
    [Fact]
    public async Task ParseAsync_UsesProfileProtocolAndRemotePort()
    {
        string path = await CreateProfileAsync(
            """
            client
            proto tcp-client
            remote 5.160.74.148 1409
            """);

        OpenVpnProfileParser parser = new();

        IReadOnlyList<VpnEndpointDefinition> endpoints =
            await parser.ParseAsync(path);

        VpnEndpointDefinition endpoint =
            Assert.Single(endpoints);

        Assert.Equal("5.160.74.148", endpoint.Host);
        Assert.Equal(1409, endpoint.Port);
        Assert.Equal("tcp", endpoint.Protocol);
    }

    [Fact]
    public async Task ParseAsync_SupportsMultipleRemotesAndComments()
    {
        string path = await CreateProfileAsync(
            """
            # primary
            proto udp
            remote first.example 1194
            ; fallback
            remote second.example 443 tcp-client
            """);

        OpenVpnProfileParser parser = new();

        IReadOnlyList<VpnEndpointDefinition> endpoints =
            await parser.ParseAsync(path);

        Assert.Equal(2, endpoints.Count);
        Assert.Equal("udp", endpoints[0].Protocol);
        Assert.Equal("tcp", endpoints[1].Protocol);
    }

    [Fact]
    public async Task ParseAsync_WhenNoRemoteExists_Throws()
    {
        string path = await CreateProfileAsync(
            """
            client
            proto udp
            """);

        OpenVpnProfileParser parser = new();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => parser.ParseAsync(path));
    }

    private static async Task<string> CreateProfileAsync(
        string content)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string path = Path.Combine(
            directory,
            "profile.ovpn");

        await File.WriteAllTextAsync(path, content);

        return path;
    }
}