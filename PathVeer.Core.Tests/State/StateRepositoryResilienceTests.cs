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
        Assert.True(
            File.Exists(path + ".corrupt")
            || Directory.EnumerateFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path + ".corrupt") + ".*").Any());
        // The validated .bak must survive the recovery (it is never consumed).
        Assert.True(File.Exists(path + ".bak"));
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

    // Required regression (audit finding 3): a stale backup from a previous
    // generation — whose primary vanished — must NOT become eligible for
    // automatic recovery once a new generation is established while no prior
    // primary exists. Recovery must never resurrect pre-missing-primary state.
    [Fact]
    public async Task MissingPrimaryThenNewGeneration_DoesNotRecoverStaleBackup()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 1 });
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.2", PrefixCount = 2 });
        // primary = .2/2, .bak = .1/1.

        // Primary disappears (without restoring from .bak).
        File.Delete(path);

        // Truly missing primary: not automatically restored from .bak.
        PathVeerState missing = await repository.LoadAsync();
        Assert.False(missing.Enabled);
        Assert.Equal(0, missing.PrefixCount);

        // New generation established while no prior primary exists.
        await repository.SaveAsync(
            new PathVeerState { Enabled = false, Gateway = "10.0.0.9", PrefixCount = 5 });

        // Now corrupt the new primary. Recovery must NOT resurrect v1 (.1/1).
        await File.WriteAllBytesAsync(path, new byte[HistoricalNulSize]);

        PersistenceCorruptException ex =
            await Assert.ThrowsAsync<PersistenceCorruptException>(
                () => repository.LoadAsync());
        Assert.Equal(path, ex.StorePath);
        Assert.False(File.Exists(path + ".bak"));
    }

    // Required regression (audit finding 6): a 0-byte / whitespace-only primary
    // that EXISTS is corruption for BackupRollback stores and must enter the
    // recovery policy, not silently become defaults. A truly missing file
    // remains distinguishable.
    [Fact]
    public async Task ZeroBytePrimaryWithValidBackup_RecoversDoNotDefault()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 1 });
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.2", PrefixCount = 2 });

        // 0-byte existing primary + valid backup -> recovers, not defaults.
        File.WriteAllBytes(path, Array.Empty<byte>());

        PathVeerState loaded = await repository.LoadAsync();
        Assert.Equal("10.0.0.1", loaded.Gateway);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task WhitespacePrimaryWithValidBackup_RecoversDoNotDefault()
    {
        string path = CreateTemporaryPath();
        StateRepository repository = new(path);

        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.1", PrefixCount = 1 });
        await repository.SaveAsync(
            new PathVeerState { Enabled = true, Gateway = "10.0.0.2", PrefixCount = 2 });

        await File.WriteAllTextAsync(path, "   \t  ");

        PathVeerState loaded = await repository.LoadAsync();
        Assert.Equal("10.0.0.1", loaded.Gateway);
        Assert.True(File.Exists(path + ".bak"));
    }
}
