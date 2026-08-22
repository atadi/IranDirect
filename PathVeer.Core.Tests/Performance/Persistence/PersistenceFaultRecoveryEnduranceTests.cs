namespace PathVeer.Core.Tests.Performance.Persistence;

using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;
using PathVeer.Testing.Performance.Persistence;

public sealed class PersistenceFaultRecoveryEnduranceTests
{
    public static IEnumerable<object[]> FaultPoints() => new[]
    {
        new object[] { FaultInjectionPoint.JsonLoad },
        new object[] { FaultInjectionPoint.JsonSave },
        new object[] { FaultInjectionPoint.FileRead },
        new object[] { FaultInjectionPoint.FileWrite },
        new object[] { FaultInjectionPoint.FileMove }
    };

    [Theory]
    [MemberData(nameof(FaultPoints))]
    public async Task InjectedFault_CyclesThrough_FailThenRecover(
        FaultInjectionPoint point)
    {
        using TemporaryPersistenceWorkspace workspace =
            new($"fault-recovery-{point}");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        const int cycles = 100;

        PersistenceEnduranceDocument committed =
            PersistenceEnduranceFixtures.Document(0);
        await store.SaveAsync(committed);
        string committedJson = await File.ReadAllTextAsync(path);

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            JsonStore<PersistenceEnduranceDocument> faulted = new(
                path,
                faultPolicy: FaultInjectionPolicy.For([point]));

            // Audit finding 4: FileRead is a TRANSIENT I/O failure (retried,
            // then InvalidOperationException); JsonLoad is CORRUPTION
            // (fail-closed surfaces JsonException). The write-side points still
            // fault immediately as FaultInjectionException.
            if (point is FaultInjectionPoint.FileRead)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => FaultAsync(faulted, point));
            }
            else if (point is FaultInjectionPoint.JsonLoad)
            {
                await Assert.ThrowsAsync<System.Text.Json.JsonException>(
                    () => FaultAsync(faulted, point));
            }
            else
            {
                FaultInjectionException fault =
                    await Assert.ThrowsAsync<FaultInjectionException>(
                        () => FaultAsync(faulted, point));

                Assert.Equal(point, fault.Point);
                Assert.False(File.Exists(path + ".tmp"));
            }

            if (point is FaultInjectionPoint.FileWrite
                or FaultInjectionPoint.FileMove)
            {
                Assert.Equal(
                    committedJson,
                    await File.ReadAllTextAsync(path));
            }

            PersistenceEnduranceDocument next =
                PersistenceEnduranceFixtures.Document(cycle + 1);

            await store.SaveAsync(next);

            PersistenceEnduranceDocument loaded =
                await store.LoadAsync();

            Assert.Equal(next, loaded);

            committed = next;
            committedJson = await File.ReadAllTextAsync(path);
        }

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
        PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(path);
        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(path);
    }

    [Theory]
    [MemberData(nameof(FaultPoints))]
    public async Task InjectedFault_DoesNotLeakIntoHealthyStore(
        FaultInjectionPoint point)
    {
        using TemporaryPersistenceWorkspace workspace =
            new($"fault-isolation-{point}");
        string healthyPath = workspace.CreatePath("healthy.json");
        string faultedPath = workspace.CreatePath("faulted.json");

        JsonStore<PersistenceEnduranceDocument> healthy = new(healthyPath);
        JsonStore<PersistenceEnduranceDocument> faulted = new(
            faultedPath,
            faultPolicy: FaultInjectionPolicy.For([point]));

        const int cycles = 25;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            PersistenceEnduranceDocument healthyDocument =
                PersistenceEnduranceFixtures.Document(cycle * 2);

            await healthy.SaveAsync(healthyDocument);

            // Audit finding 4: read-side points now have corrected
            // classifications (FileRead -> InvalidOperationException,
            // JsonLoad -> JsonException); write-side -> FaultInjectionException.
            if (point is FaultInjectionPoint.FileRead)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => FaultAsync(faulted, point));
            }
            else if (point is FaultInjectionPoint.JsonLoad)
            {
                await Assert.ThrowsAsync<System.Text.Json.JsonException>(
                    () => FaultAsync(faulted, point));
            }
            else
            {
                await Assert.ThrowsAsync<FaultInjectionException>(
                    () => FaultAsync(faulted, point));
            }

            PersistenceEnduranceDocument loaded =
                await healthy.LoadAsync();

            Assert.Equal(healthyDocument, loaded);
        }

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        Assert.False(File.Exists(faultedPath));
        Assert.True(File.Exists(healthyPath));
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
    }

    private static async Task FaultAsync(
        JsonStore<PersistenceEnduranceDocument> store,
        FaultInjectionPoint point)
    {
        if (point is FaultInjectionPoint.JsonLoad
            or FaultInjectionPoint.FileRead)
        {
            await store.LoadAsync();
        }
        else
        {
            await store.SaveAsync(
                PersistenceEnduranceFixtures.Document(999_999));
        }
    }
}
