using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Persistence;

public sealed class JsonStoreFaultInjectionTests
{
    // Audit finding 4: a JsonLoad fault (content/parse failure) is CORRUPTION,
    // so a fail-closed store surfaces it as JsonException — preserving the
    // native caller contract, not as a FaultInjectionException.
    [Fact]
    public async Task LoadAsync_JsonLoadFault_ThrowsJsonException()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.JsonLoad]));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadAsync());
    }

    // Audit finding 4: a FileRead fault is a transient I/O failure, NOT
    // corruption. It is retried (bounded) and, once exhausted, surfaces as an
    // InvalidOperationException — not a FaultInjectionException, and never as a
    // corruption classification.
    [Fact]
    public async Task LoadAsync_FileReadFault_RetriedThenThrowsAccessFailure()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.LoadAsync());

        Assert.Contains("could not be read", exception.Message);
    }

    // A ONE-SHOT FileRead fault must be retried and then succeed, proving it is
    // classified as transient rather than corruption.
    [Fact]
    public async Task LoadAsync_OneShotFileReadFault_Recovers()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);
        await store.SaveAsync(new TestDocument { Name = "ok", Count = 3 });

        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            new OneShotFaultInjectionPolicy(FaultInjectionPoint.FileRead));

        TestDocument loaded = await faultedStore.LoadAsync();

        Assert.Equal("ok", loaded.Name);
        Assert.Equal(3, loaded.Count);
    }

    [Fact]
    public async Task LoadAsync_FailedLoad_DoesNotAlterFile()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);
        TestDocument original = new() { Name = "original", Count = 7 };

        await store.SaveAsync(original);
        string before = await File.ReadAllTextAsync(path);

        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [
                    FaultInjectionPoint.JsonLoad,
                    FaultInjectionPoint.FileRead
                ]));

        await Assert.ThrowsAnyAsync<Exception>(
            () => faultedStore.LoadAsync());

        string after = await File.ReadAllTextAsync(path);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task LoadAsync_Succeeds_AfterInjectedFailure()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        // A FileRead fault is now transient-retried; once exhausted it throws
        // an access failure rather than FaultInjectionException.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => faultedStore.LoadAsync());

        JsonStore<TestDocument> store = new(path);

        TestDocument loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("", loaded.Name);
    }

    [Fact]
    public async Task SaveAsync_JsonSaveFault_NoTempFileCreated()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.JsonSave]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => store.SaveAsync(new TestDocument()));

        Assert.Equal(
            FaultInjectionPoint.JsonSave,
            exception.Point);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_FileWriteFault_DestinationUnchanged()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);
        TestDocument original = new() { Name = "original", Count = 1 };

        await store.SaveAsync(original);
        string before = await File.ReadAllTextAsync(path);

        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileWrite]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => faultedStore.SaveAsync(
                    new TestDocument { Name = "replacement" }));

        Assert.Equal(
            FaultInjectionPoint.FileWrite,
            exception.Point);

        string after = await File.ReadAllTextAsync(path);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SaveAsync_FileWriteFault_NoTempFileRemains()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileWrite]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => store.SaveAsync(new TestDocument()));

        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_FileMoveFault_DestinationUnchanged()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);
        TestDocument original = new() { Name = "original", Count = 2 };

        await store.SaveAsync(original);
        string before = await File.ReadAllTextAsync(path);

        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileMove]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => faultedStore.SaveAsync(
                    new TestDocument { Name = "replacement" }));

        Assert.Equal(
            FaultInjectionPoint.FileMove,
            exception.Point);

        string after = await File.ReadAllTextAsync(path);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SaveAsync_FileMoveFault_TempFileCleanedUp()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileMove]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => store.SaveAsync(new TestDocument()));

        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_Succeeds_AfterInjectedFailure()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [
                    FaultInjectionPoint.FileWrite,
                    FaultInjectionPoint.FileMove
                ]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faultedStore.SaveAsync(new TestDocument()));

        JsonStore<TestDocument> store = new(path);

        await store.SaveAsync(new TestDocument { Name = "ok", Count = 9 });

        TestDocument loaded = await store.LoadAsync();

        Assert.Equal("ok", loaded.Name);
        Assert.Equal(9, loaded.Count);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task InjectedFault_IsNotRetried()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [
                    FaultInjectionPoint.FileRead,
                    FaultInjectionPoint.FileWrite
                ]));

        // FileRead is now transient-retried and surfaces as an access failure
        // (not FaultInjectionException); FileWrite still faults immediately.
        InvalidOperationException loadFault =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.LoadAsync());

        Assert.Contains("could not be read", loadFault.Message);

        FaultInjectionException saveFault =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => store.SaveAsync(new TestDocument()));

        Assert.Equal(
            FaultInjectionPoint.FileWrite,
            saveFault.Point);
    }

    [Fact]
    public async Task FileLock_IsReleased_AfterInjectedFailure()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> faultedStore = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileWrite]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faultedStore.SaveAsync(new TestDocument()));

        JsonStore<TestDocument> store = new(path);

        TestDocument loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task ConcurrentUnrelatedStore_IsUnaffected()
    {
        string faultedPath = CreateTemporaryPath();
        string healthyPath = CreateTemporaryPath();
        JsonStore<TestDocument> healthy = new(healthyPath);
        JsonStore<TestDocument> faulted = CreateStore(
            faultedPath,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileWrite]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.SaveAsync(new TestDocument()));

        await healthy.SaveAsync(
            new TestDocument { Name = "healthy", Count = 3 });

        TestDocument loaded = await healthy.LoadAsync();

        Assert.Equal("healthy", loaded.Name);
        Assert.Equal(3, loaded.Count);
    }

    // Audit finding 4: the two load-side fault points now have distinct,
    // correct classifications — FileRead is a transient access failure
    // (retried, then InvalidOperationException) while JsonLoad is corruption
    // (fail-closed surfaces JsonException). The ambient scope + injected policy
    // both contribute failures.
    [Fact]
    public async Task NestedFaultScopes_DistinctClassifications()
    {
        string path = CreateTemporaryPath();

        // Establish a valid primary so read faults can actually apply.
        JsonStore<TestDocument> seed = new(path);
        await seed.SaveAsync(new TestDocument { Name = "seed", Count = 1 });

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(FaultInjectionPoint.JsonLoad))
        {
            JsonStore<TestDocument> store = CreateStore(
                path,
                FaultInjectionPolicy.For(
                    [
                        FaultInjectionPoint.FileRead,
                        FaultInjectionPoint.JsonLoad
                    ]));

            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(FaultInjectionPoint.FileRead))
            {
                // Inner scope forces FileRead: transient I/O -> retried ->
                // access failure (NOT FaultInjectionException, NOT corruption).
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => store.LoadAsync());
            }

            // After the inner scope, we are still inside the outer JsonLoad
            // ambient scope, which takes precedence over the injected policy,
            // so a parse failure is classified as corruption -> JsonException.
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(
                () => store.LoadAsync());
        }

        // With the ambient scope cleared and only the injected JsonLoad
        // fault remaining, a parse failure is corruption -> JsonException.
        string p2 = CreateTemporaryPath();
        JsonStore<TestDocument> seed2 = new(p2);
        await seed2.SaveAsync(new TestDocument { Name = "seed2", Count = 2 });

        JsonStore<TestDocument> store2 = CreateStore(
            p2,
            FaultInjectionPolicy.For([FaultInjectionPoint.JsonLoad]));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store2.LoadAsync());
    }

    [Fact]
    public async Task NoScopeLeakage_AfterDisposal()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);
        FaultInjectionScope scope =
            FaultInjectionScope.Fail(FaultInjectionPoint.JsonSave);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => store.SaveAsync(new TestDocument()));

        scope.Dispose();

        await store.SaveAsync(new TestDocument { Name = "after", Count = 5 });

        TestDocument loaded = await store.LoadAsync();

        Assert.Equal(5, loaded.Count);
    }

    [Fact]
    public async Task MissingFileBehavior_IsUnchanged()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);

        TestDocument document = await store.LoadAsync();

        Assert.NotNull(document);
        Assert.Equal("", document.Name);
    }

    [Fact]
    public async Task CorruptJsonBehavior_IsUnchanged()
    {
        string path = CreateTemporaryPath();
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json !");

        JsonStore<TestDocument> store = new(path);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task NormalSaveLoadOutput_IsUnchanged()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);

        await store.SaveAsync(
            new TestDocument { Name = "normal", Count = 12 });

        string content = await File.ReadAllTextAsync(path);

        Assert.Contains("\"Name\": \"normal\"", content);
        Assert.Contains("\"Count\": 12", content);

        TestDocument loaded = await store.LoadAsync();

        Assert.Equal("normal", loaded.Name);
        Assert.Equal(12, loaded.Count);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private static JsonStore<TestDocument> CreateStore(
        string path,
        IFaultInjectionPolicy faultPolicy)
    {
        return new JsonStore<TestDocument>(
            path,
            faultPolicy: faultPolicy);
    }

    private static string CreateTemporaryPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "document.json");
    }

    public sealed record TestDocument
    {
        public string Name { get; init; } = "";

        public int Count { get; init; }
    }
}
