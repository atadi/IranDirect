using PathVeer.Core.Persistence;
using PathVeer.Core.State;

namespace PathVeer.Core.Tests.State;

/// <summary>
/// Store-specific resilience coverage for <see cref="StateRepository"/>
/// (state.json), reproducing the historical devsign.10 incident where a
/// runtime-state document became all-NUL bytes and every subsequent repair
/// cycle failed to parse it.
///
/// StateRepository uses BackupRollback: a recoverable corrupt runtime-state
/// document is restored from the last known-good ".bak" instead of bricking
/// every future cycle.
/// </summary>
public sealed class StateRepositoryResilienceTests
{
    private const int HistoricalNulSize = 271; // matches the real C:\ProgramData\PathVeer\state.json

    [Fact]
    public async Task LoadAsync_AllNulPrimaryPlusValidBackup_RecoversLastKnownGood()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        // Two successful cycles establish a known-good .bak chain.
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 3 });
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.2", PrefixCount = 4 });
        // primary = .2/4, .bak = .1/3.

        // Simulate the historical corruption: an all-NUL primary of the same
        // size observed on the real machine.
        await File.WriteAllBytesAsync(path, new byte[HistoricalNulSize]);

        PathVeerState loaded = await repository.LoadAsync();

        Assert.True(loaded.Enabled);
        Assert.Equal("10.0.0.1", loaded.Gateway);
        Assert.Equal(3, loaded.PrefixCount);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public async Task LoadAsync_AllNulPrimaryPlusCorruptBackup_FailsClearly()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 1 });
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.2", PrefixCount = 2 });

        await File.WriteAllBytesAsync(path, new byte[HistoricalNulSize]);
        await File.WriteAllBytesAsync(path + ".bak", new byte[40]);

        PersistenceCorruptException ex =
            await Assert.ThrowsAsync<PersistenceCorruptException>(
                () => repository.LoadAsync());
        Assert.Equal(path, ex.StorePath);
    }

    [Fact]
    public async Task LoadAsync_AllNulPrimaryWithNoBackup_FailsClearly()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        // First save: no prior primary, so no .bak is created.
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 1 });

        await File.WriteAllBytesAsync(path, new byte[HistoricalNulSize]);

        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => repository.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_AfterCorruptionRecovery_ContinuesCycles()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 1 });
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.2", PrefixCount = 2 });

        await File.WriteAllBytesAsync(path, new byte[HistoricalNulSize]);

        // A subsequent cycle must recover and then persist new state (i.e. the
        // corruption must not permanently brick future cycles).
        PathVeerState recovered = await repository.LoadAsync();
        Assert.Equal("10.0.0.1", recovered.Gateway);

        await repository.SaveAsync(
            new PathVeerState { Enabled = false, Gateway = "10.0.0.9", PrefixCount = 7 });

        PathVeerState after = await repository.LoadAsync();
        Assert.Equal("10.0.0.9", after.Gateway);
        Assert.Equal(7, after.PrefixCount);
        Assert.False(after.Enabled);
    }

    [Fact]
    public async Task LoadAsync_ValidPrimaryPlusStaleTmp_PrimaryWins()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.5", PrefixCount = 9 });

        // Leftover .tmp from an interrupted save must never become authoritative.
        await File.WriteAllTextAsync(path + ".tmp", "{\"garbage\":true");

        PathVeerState loaded = await repository.LoadAsync();
        Assert.Equal("10.0.0.5", loaded.Gateway);
        Assert.Equal(9, loaded.PrefixCount);
    }

    private static string CreateTemporaryPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "state.json");
}
