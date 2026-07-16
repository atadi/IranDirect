using IranDirect.Core.Runtime;

namespace IranDirect.Core.Tests.Runtime;

public sealed class RuntimeObserverTests
{
    [Fact]
    public async Task ObserveAsync_ProducesSuccessfulSnapshot()
    {
        FakeObservationSource source = new();
        RuntimeObserver observer = new(source);

        ObservedRuntime runtime =
            await observer.ObserveAsync();

        Assert.True(runtime.VpnProfileExists);
        Assert.True(runtime.VpnProfileValid);
        Assert.NotNull(runtime.DirectGateway);
        Assert.Single(runtime.VpnEndpoints);
        Assert.Single(runtime.Prefixes);
        Assert.Single(runtime.Routes);
        Assert.NotEqual(default, runtime.ObservedAt);
    }

    [Fact]
    public async Task ObserveAsync_WhenProfileParsingFails_MarksProfileInvalid()
    {
        FakeObservationSource source = new()
        {
            EndpointResult =
                RuntimeObservationSourceResult<
                    IReadOnlyList<ObservedVpnEndpoint>>
                    .Failure("Invalid profile.")
        };

        RuntimeObserver observer = new(source);

        ObservedRuntime runtime =
            await observer.ObserveAsync();

        Assert.True(runtime.VpnProfileExists);
        Assert.False(runtime.VpnProfileValid);
        Assert.Empty(runtime.VpnEndpoints);
    }

    [Fact]
    public async Task ObserveAsync_WhenGatewayFails_LeavesGatewayNull()
    {
        FakeObservationSource source = new()
        {
            GatewayResult =
                RuntimeObservationSourceResult<
                    ObservedDirectGateway>
                    .Failure("No gateway.")
        };

        RuntimeObserver observer = new(source);

        ObservedRuntime runtime =
            await observer.ObserveAsync();

        Assert.Null(runtime.DirectGateway);
    }

    private sealed class FakeObservationSource :
        IRuntimeObservationSource
    {
        public bool VpnProfileExists { get; init; } = true;

        public RuntimeObservationSourceResult<
            IReadOnlyList<ObservedVpnEndpoint>>
            EndpointResult { get; init; } =
                RuntimeObservationSourceResult<
                    IReadOnlyList<ObservedVpnEndpoint>>
                    .Success(
                    [
                        new ObservedVpnEndpoint
                        {
                            Host = "vpn.example",
                            Address = "5.160.74.148",
                            Port = 1409,
                            Protocol = "tcp"
                        }
                    ]);

        public RuntimeObservationSourceResult<
            ObservedDirectGateway>
            GatewayResult { get; init; } =
                RuntimeObservationSourceResult<
                    ObservedDirectGateway>
                    .Success(
                        new ObservedDirectGateway
                        {
                            Address = "192.168.100.1",
                            InterfaceIndex = 30,
                            InterfaceName = "Ethernet",
                            InterfaceMetric = 10
                        });

        public Task<RuntimeObservationSourceResult<
            IReadOnlyList<ObservedVpnEndpoint>>>
            ObserveVpnEndpointsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EndpointResult);
        }

        public RuntimeObservationSourceResult<
            ObservedDirectGateway>
            ObserveDirectGateway()
        {
            return GatewayResult;
        }

        public Task<IReadOnlyList<string>>
            ObservePrefixesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<string>>(
                ["203.0.113.0/24"]);
        }

        public Task<IReadOnlyList<ObservedRoute>>
            ObserveRoutesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<ObservedRoute>>(
                [
                    new ObservedRoute
                    {
                        DestinationPrefix =
                            "5.160.74.148/32",
                        NextHop =
                            "192.168.100.1",
                        InterfaceIndex = 30,
                        Metric = 1
                    }
                ]);
        }
    }
}