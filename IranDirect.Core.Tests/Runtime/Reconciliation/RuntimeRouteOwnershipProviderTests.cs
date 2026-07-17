using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Tests.Runtime.Reconciliation;

public sealed class RuntimeRouteOwnershipProviderTests
{
    [Fact]
    public async Task LoadAsync_NormalizesAndDeduplicatesIdentities()
    {
        RuntimeRouteOwnershipProvider provider = new(
            new FakeSource
            {
                EndpointIdentities =
                [
                    "5.160.74.148/32|192.168.100.1|30",
                    " 5.160.74.148/32|192.168.100.1|30 ",
                    "",
                    " "
                ],
                PrefixIdentities =
                [
                    "203.0.113.0/24|192.168.100.1|30",
                    "203.0.113.0/24|192.168.100.1|30"
                ]
            });

        RuntimeRouteOwnership ownership =
            await provider.LoadAsync();

        Assert.Single(
            ownership.EndpointRouteIdentities);

        Assert.Single(
            ownership.PrefixRouteIdentities);

        Assert.Contains(
            "5.160.74.148/32|192.168.100.1|30",
            ownership.EndpointRouteIdentities);

        Assert.Contains(
            "203.0.113.0/24|192.168.100.1|30",
            ownership.PrefixRouteIdentities);
    }

    [Fact]
    public async Task LoadAsync_WhenInventoriesAreEmpty_ReturnsEmptyOwnership()
    {
        RuntimeRouteOwnershipProvider provider = new(
            new FakeSource());

        RuntimeRouteOwnership ownership =
            await provider.LoadAsync();

        Assert.Empty(
            ownership.EndpointRouteIdentities);

        Assert.Empty(
            ownership.PrefixRouteIdentities);
    }

    [Fact]
    public async Task LoadAsync_PreservesOwnershipCategories()
    {
        const string sameIdentity =
            "5.160.74.148/32|192.168.100.1|30";

        RuntimeRouteOwnershipProvider provider = new(
            new FakeSource
            {
                EndpointIdentities =
                [
                    sameIdentity
                ],
                PrefixIdentities =
                [
                    sameIdentity
                ]
            });

        RuntimeRouteOwnership ownership =
            await provider.LoadAsync();

        Assert.Contains(
            sameIdentity,
            ownership.EndpointRouteIdentities);

        Assert.Contains(
            sameIdentity,
            ownership.PrefixRouteIdentities);
    }

    private sealed class FakeSource :
        IRuntimeRouteOwnershipSource
    {
        public IReadOnlyCollection<string>
            EndpointIdentities { get; init; } = [];

        public IReadOnlyCollection<string>
            PrefixIdentities { get; init; } = [];

        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                EndpointIdentities);
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                PrefixIdentities);
        }
    }
}