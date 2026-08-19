using PathVeer.Core.Installer;
using Xunit;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// PathVeer Tray single-instance primitive (per-interactive-user-session).
///
/// The Tray itself must be the authority: a duplicate launch must not create a
/// second Tray process. These tests exercise the primitive directly (acquire /
/// release / duplicate / re-acquire). The real 10x-process-count proof lives in
/// tools/Test-TraySingleInstance.ps1 (runs the actual published EXE).
/// </summary>
public class TraySingleInstanceTests
{
    [Fact]
    public void TryAcquire_ReturnsOwner_FirstCall()
    {
        using var guard = TraySingleInstance.TryAcquire();
        Assert.NotNull(guard);
        Assert.True(guard!.IsOwned);
    }

    [Fact]
    public void TryAcquire_DuplicateInSameProcess_ReturnsNull()
    {
        using var owner = TraySingleInstance.TryAcquire();
        Assert.NotNull(owner);

        // A second acquire (same process, same session name) sees the live
        // owner and must return null — no second instance.
        using var duplicate = TraySingleInstance.TryAcquire();
        Assert.Null(duplicate);
    }

    [Fact]
    public void TryAcquire_AfterRelease_Reacquires()
    {
        var first = TraySingleInstance.TryAcquire();
        Assert.NotNull(first);
        first!.Dispose();

        // Released: the next acquire should succeed.
        using var second = TraySingleInstance.TryAcquire();
        Assert.NotNull(second);
        Assert.True(second!.IsOwned);
    }

    [Fact]
    public void Scope_IsSessionLocal_NotGlobal()
    {
        // The contract requires per-user-session scope, not machine-global.
        Assert.StartsWith(@"Local\", TraySingleInstance.DefaultMutexName);
        Assert.False(TraySingleInstance.DefaultMutexName.StartsWith(@"Global\"));
    }

    [Fact]
    public void SignalExisting_WhenNoPrimary_IsNoOp()
    {
        // Must not throw when no primary has created the show-event yet.
        TraySingleInstance.SignalExisting();
    }
}
