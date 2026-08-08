using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Persistence;

public sealed class JsonStoreFaultInjectionTests
{
    [Fact]
    public async Task LoadAsync_JsonLoadFault_ThrowsExpectedException()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.JsonLoad]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => store.LoadAsync());

        Assert.Equal(
            FaultInjectionPoint.JsonLoad,
            exception.Point);
    }

    [Fact]
    public async Task LoadAsync_FileReadFault_ThrowsExpectedException()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = CreateStore(
            path,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => store.LoadAsync());

        Assert.Equal(
            FaultInjectionPoint.FileRead,
            exception.Point);
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

        await Assert.ThrowsAsync<FaultInjectionException>(
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

        await Assert.ThrowsAsync<FaultInjectionException>(
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

        FaultInjectionException loadFault =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => store.LoadAsync());

        Assert.Equal(
            FaultInjectionPoint.FileRead,
            loadFault.Point);

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

    [Fact]
    public async Task NestedFaultScopes_SelectCorrectFailurePoint()
    {
        string path = CreateTemporaryPath();

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
                FaultInjectionException innerFault =
                    await Assert.ThrowsAsync<FaultInjectionException>(
                        () => store.LoadAsync());

                Assert.Equal(
                    FaultInjectionPoint.FileRead,
                    innerFault.Point);
            }

            FaultInjectionException outerFault =
                await Assert.ThrowsAsync<FaultInjectionException>(
                    () => store.LoadAsync());

            Assert.Equal(
                FaultInjectionPoint.JsonLoad,
                outerFault.Point);
        }
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
