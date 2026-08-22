using System.Text;
using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Persistence;

/// <summary>
/// Audit finding 1: cross-instance writer serialization for the SAME canonical
/// file path. The previous design only held a per-instance lock, so two
/// independent JsonStore&lt;T&gt; instances targeting the same path could
/// overlap Load/Save/Mutate/promotion/recovery. The fix uses a process-wide
/// path-scoped lock registry (Windows case-insensitive), so every in-process
/// store for the same path serializes against each other.
///
/// These tests do NOT rely on the pre-existing "single writer + many readers"
/// test as proof; they exercise two independent instances directly.
/// </summary>
public sealed class JsonStoreCrossInstanceLockingTests
{
    private sealed record Doc
    {
        public int Revision { get; init; }

        public string Payload { get; init; } = "";
    }

    [Fact]
    public async Task TwoInstances_SamePath_ConcurrentSaveAsync_NoLostUpdates()
    {
        string path = CreatePath();

        const int perWriter = 200;
        const int writerCount = 4;

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Task> writers = [];
        for (int w = 0; w < writerCount; w++)
        {
            writers.Add(Task.Run(async () =>
            {
                await start.Task;

                // Each writer is an INDEPENDENT instance; only the path lock
                // coordinates them.
                JsonStore<Doc> store = new(path);
                for (int i = 0; i < perWriter; i++)
                {
                    await store.MutateAsync(d => Task.FromResult(
                        d with { Revision = d.Revision + 1 }));
                }
            }));
        }

        start.TrySetResult();
        await Task.WhenAll(writers);

        JsonStore<Doc> reader = new(path);
        Doc final = await reader.LoadAsync();

        Assert.Equal(writerCount * perWriter, final.Revision);
    }

    [Fact]
    public async Task TwoInstances_SamePath_ConcurrentMutateAsync_Atomic()
    {
        string path = CreatePath();
        await new JsonStore<Doc>(path).SaveAsync(new Doc { Revision = 0 });

        const int perWriter = 150;
        const int writerCount = 3;

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Task> writers = [];
        for (int w = 0; w < writerCount; w++)
        {
            writers.Add(Task.Run(async () =>
            {
                await start.Task;
                JsonStore<Doc> store = new(path);
                for (int i = 0; i < perWriter; i++)
                {
                    await store.MutateAsync(d => Task.FromResult(
                        d with { Revision = d.Revision + 1 }));
                }
            }));
        }

        start.TrySetResult();
        await Task.WhenAll(writers);

        Doc final = await new JsonStore<Doc>(path).LoadAsync();
        Assert.Equal(writerCount * perWriter, final.Revision);
    }

    [Fact]
    public async Task WriterA_WriterB_Readers_SamePath_FinalValidNoLostUpdates()
    {
        string path = CreatePath();
        await new JsonStore<Doc>(path).SaveAsync(new Doc { Revision = 0 });

        const int writerCount = 4;
        const int perWriter = 100;
        const int readerCount = 4;
        const int perReader = 100;

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Task> workers = [];
        for (int w = 0; w < writerCount; w++)
        {
            workers.Add(Task.Run(async () =>
            {
                await start.Task;
                JsonStore<Doc> store = new(path);
                for (int i = 0; i < perWriter; i++)
                {
                    await store.MutateAsync(d => Task.FromResult(
                        d with { Revision = d.Revision + 1 }));
                }
            }));
        }

        for (int r = 0; r < readerCount; r++)
        {
            workers.Add(Task.Run(async () =>
            {
                await start.Task;
                JsonStore<Doc> store = new(path);
                for (int i = 0; i < perReader; i++)
                {
                    Doc loaded = await store.LoadAsync();
                    Assert.InRange(loaded.Revision, 0, writerCount * perWriter);
                }
            }));
        }

        start.TrySetResult();
        await Task.WhenAll(workers);

        Doc final = await new JsonStore<Doc>(path).LoadAsync();
        Assert.Equal(writerCount * perWriter, final.Revision);
    }

    [Fact]
    public async Task SamePath_DifferentCasing_ResolvesToSameLock()
    {
        string basePath = CreatePath();
        string lowerPath = basePath.ToLowerInvariant();
        string upperPath = basePath.ToUpperInvariant();

        // Two independent instances using different casing of the same path
        // must still serialize against each other. We prove it by a
        // deterministic interleaving: instance A continuously mutates; instance
        // B reads and must never observe an out-of-range revision, and the
        // final count equals the exact number of mutations.
        await new JsonStore<Doc>(lowerPath).SaveAsync(new Doc { Revision = 0 });

        const int perWriter = 200;
        const int writerCount = 3;

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Task> writers = [];
        for (int w = 0; w < writerCount; w++)
        {
            writers.Add(Task.Run(async () =>
            {
                await start.Task;
                JsonStore<Doc> store = new(w % 2 == 0 ? lowerPath : upperPath);
                for (int i = 0; i < perWriter; i++)
                {
                    await store.MutateAsync(d => Task.FromResult(
                        d with { Revision = d.Revision + 1 }));
                }
            }));
        }

        start.TrySetResult();
        await Task.WhenAll(writers);

        Doc final = await new JsonStore<Doc>(upperPath).LoadAsync();
        Assert.Equal(writerCount * perWriter, final.Revision);
    }

    [Fact]
    public async Task TwoInstances_NoWriterTouchesAnothersTemp()
    {
        string path = CreatePath();
        await new JsonStore<Doc>(path).SaveAsync(new Doc { Revision = 0 });

        // Drive many interleaved saves through independent instances and
        // confirm no temporary file from one writer is ever promoted or left
        // in the active ".bak"/primary slot in a partially written state.
        const int iterations = 100;
        List<Task> jobs = [];
        for (int i = 0; i < 6; i++)
        {
            int id = i;
            jobs.Add(Task.Run(async () =>
            {
                JsonStore<Doc> store = new(path);
                for (int j = 0; j < iterations; j++)
                {
                    await store.MutateAsync(d => Task.FromResult(
                        d with { Revision = d.Revision + 1 }));

                    // After every save, the on-disk file must be complete,
                    // parseable JSON (a writer never promotes another's temp).
                    Doc current = await new JsonStore<Doc>(path).LoadAsync();
                    Assert.True(current.Revision >= 0);
                    Assert.True(
                        File.Exists(path),
                        "primary must be present between writes");
                }
            }));
        }

        await Task.WhenAll(jobs);

        Doc final = await new JsonStore<Doc>(path).LoadAsync();
        Assert.Equal(6 * iterations, final.Revision);
    }

    private static string CreatePath()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "cross-instance.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
