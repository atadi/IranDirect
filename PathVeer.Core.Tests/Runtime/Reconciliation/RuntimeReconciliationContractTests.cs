using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Runtime.Reconciliation;

public sealed class RuntimeReconciliationContractTests
{
    [Fact]
    public void NoChanges_IsSuccessfulAndDoesNotMutate()
    {
        RuntimeReconciliationResult result =
            RuntimeReconciliationResult.NoChanges(
                "Runtime already matches the desired state.");

        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            result.Status);
        Assert.True(result.ChangeSet.IsEmpty);
    }

    [Fact]
    public void Blocked_IsNotSuccessfulAndDoesNotMutate()
    {
        RuntimeReconciliationResult result =
            RuntimeReconciliationResult.Blocked(
            [
                "VPN profile is unavailable."
            ]);

        Assert.False(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Status);
        Assert.Single(result.Messages);
    }

    [Fact]
    public void Applied_RequiresAtLeastOneChange()
    {
        Assert.Throws<ArgumentException>(
            () =>
                RuntimeReconciliationResult.Applied(
                    new RuntimeChangeSet()));
    }

    [Fact]
    public void Applied_WithChanges_IsSuccessfulAndMutates()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind =
                        RuntimeChangeKind.AddEndpointRoute,
                    Identity =
                        "5.160.74.148/32|192.168.100.1|30",
                    DestinationPrefix =
                        "5.160.74.148/32",
                    Gateway =
                        "192.168.100.1",
                    InterfaceIndex = 30,
                    Metric = 1,
                    Description =
                        "Protect the VPN endpoint through " +
                        "the direct gateway."
                }
            ]
        };

        RuntimeReconciliationResult result =
            RuntimeReconciliationResult.Applied(
                changeSet,
                "One route was added.");

        Assert.True(result.Succeeded);
        Assert.True(result.MutatedInfrastructure);
        Assert.Equal(
            RuntimeReconciliationStatus.ChangesApplied,
            result.Status);
        Assert.Single(result.ChangeSet.Changes);
    }
}