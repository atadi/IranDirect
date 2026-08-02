using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixUpdateMonitorTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetSnapshot_Initial_IsIdleAndEmpty()
    {
        PrefixUpdateMonitor monitor =
            CreateMonitor(new StubPrefixUpdateChecker());

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.False(snapshot.Running);
        Assert.False(snapshot.Checking);
        Assert.Null(snapshot.CurrentResult);
        Assert.Null(snapshot.LastCheckedAt);
        Assert.Null(snapshot.LastSuccessfulCheckAt);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task StartAsync_SetsRunning()
    {
        PrefixUpdateMonitor monitor =
            CreateMonitor(new StubPrefixUpdateChecker());

        await monitor.StartAsync();

        try
        {
            Assert.True(monitor.GetSnapshot().Running);
        }
        finally
        {
            await monitor.StopAsync();
        }

        Assert.False(monitor.GetSnapshot().Running);
    }

    [Fact]
    public async Task StartAsync_Twice_IsIdempotent()
    {
        StubPrefixUpdateChecker checker = new();
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMilliseconds(50));

        await monitor.StartAsync();
        await monitor.StartAsync();

        try
        {
            Assert.True(monitor.GetSnapshot().Running);

            await WaitForAsync(
                () => checker.CallCount >= 1);

            int calls = checker.CallCount;
            Assert.True(calls >= 1);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_IsNoOp()
    {
        PrefixUpdateMonitor monitor =
            CreateMonitor(new StubPrefixUpdateChecker());

        await monitor.StopAsync();

        Assert.False(monitor.GetSnapshot().Running);
    }

    [Fact]
    public async Task StopAsync_DuringDelay_CompletesQuickly()
    {
        StubPrefixUpdateChecker checker = new();
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMinutes(15));

        await monitor.StartAsync();

        Task stop = monitor.StopAsync();

        Task completed =
            await Task.WhenAny(
                stop,
                Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(stop, completed);
        Assert.False(monitor.GetSnapshot().Running);
        Assert.Equal(0, checker.CallCount);
    }

    [Fact]
    public async Task ForceCheck_Current_UpdatesSnapshot()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            snapshot.CurrentResult!.Status);
        Assert.Equal(BaseTime, snapshot.LastCheckedAt);
        Assert.Equal(
            BaseTime,
            snapshot.LastSuccessfulCheckAt);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.False(snapshot.Checking);
        Assert.Equal(1, checker.CallCount);
    }

    [Fact]
    public async Task ForceCheck_UpdateAvailable_UpdatesSnapshot()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.UpdateAvailable)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable,
            snapshot.CurrentResult!.Status);
        Assert.Equal(
            BaseTime,
            snapshot.LastSuccessfulCheckAt);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task ForceCheck_Unknown_UpdatesSnapshot()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Unknown,
                reason: "No local metadata available.")
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.Unknown,
            snapshot.CurrentResult!.Status);
        Assert.Equal(
            "No local metadata available.",
            snapshot.CurrentResult.Reason);
        Assert.Equal(
            BaseTime,
            snapshot.LastSuccessfulCheckAt);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task ForceCheck_Failed_UpdatesSnapshot()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Failed,
                reason: "Remote check timed out.")
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            snapshot.CurrentResult!.Status);
        Assert.Equal(BaseTime, snapshot.LastCheckedAt);
        Assert.Null(snapshot.LastSuccessfulCheckAt);
        Assert.Equal(1, snapshot.ConsecutiveFailures);
        Assert.False(snapshot.Checking);
    }

    [Fact]
    public async Task ForceCheck_CheckerThrows_SynthesizesFailedResult()
    {
        StubPrefixUpdateChecker checker = new()
        {
            ExceptionToThrow = new IOException("disk error")
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            snapshot.CurrentResult!.Status);
        Assert.Contains(
            "disk error",
            snapshot.CurrentResult.Reason);
        Assert.Equal(1, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task ForceCheck_ConsecutiveFailures_Accumulate()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Failed)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();
        await monitor.ForceCheckAsync();
        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(3, snapshot.ConsecutiveFailures);
        Assert.Equal(3, checker.CallCount);
        Assert.Null(snapshot.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task ForceCheck_SuccessAfterFailure_ResetsFailures()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Failed)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();
        await monitor.ForceCheckAsync();

        Assert.Equal(
            2,
            monitor.GetSnapshot().ConsecutiveFailures);

        checker.Result = CreateResult(
            PrefixUpdateCheckStatus.Current);

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            snapshot.CurrentResult!.Status);
        Assert.Equal(
            BaseTime,
            snapshot.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task ForceCheck_WhileCheckInFlight_ReturnsSameTask()
    {
        TaskCompletionSource gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StubPrefixUpdateChecker checker = new()
        {
            Gate = gate.Task,
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        Task first = monitor.ForceCheckAsync();
        Task second = monitor.ForceCheckAsync();

        Assert.Same(first, second);
        Assert.True(monitor.GetSnapshot().Checking);

        gate.SetResult();

        await Task.WhenAll(first, second);

        Assert.Equal(1, checker.CallCount);
        Assert.False(monitor.GetSnapshot().Checking);
    }

    [Fact]
    public async Task ForceCheck_AfterCheckCompletes_StartsNewCheck()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        await monitor.ForceCheckAsync();
        await monitor.ForceCheckAsync();

        Assert.Equal(2, checker.CallCount);
    }

    [Fact]
    public async Task Timer_ExecutesScheduledChecks()
    {
        StubPrefixUpdateChecker checker = new();
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMilliseconds(50));

        await monitor.StartAsync();

        try
        {
            await WaitForAsync(
                () => checker.CallCount >= 2);

            Assert.True(checker.CallCount >= 2);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task Timer_NoCheckBeforeIntervalElapses()
    {
        StubPrefixUpdateChecker checker = new();
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMilliseconds(500));

        await monitor.StartAsync();

        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(150));

            Assert.Equal(0, checker.CallCount);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task ForceCheck_DoesNotWaitForTimer()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMinutes(15));

        await monitor.StartAsync();

        try
        {
            await monitor.ForceCheckAsync();

            Assert.Equal(1, checker.CallCount);
            Assert.NotNull(
                monitor.GetSnapshot().CurrentResult);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task Timer_ContinuesAfterFailure()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Failed)
        };
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMilliseconds(50));

        await monitor.StartAsync();

        try
        {
            await WaitForAsync(
                () => checker.CallCount >= 3);

            PrefixUpdateMonitorSnapshot snapshot =
                monitor.GetSnapshot();

            Assert.True(snapshot.Running);
            Assert.True(
                snapshot.ConsecutiveFailures >= 3);
            Assert.Equal(
                PrefixUpdateCheckStatus.Failed,
                snapshot.CurrentResult!.Status);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task Timer_OverlappingForceCheck_RunsOneProbe()
    {
        TaskCompletionSource gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StubPrefixUpdateChecker checker = new()
        {
            Gate = gate.Task,
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMilliseconds(50));

        await monitor.StartAsync();

        try
        {
            await WaitForAsync(
                () => checker.CallCount >= 1);

            Task forced = monitor.ForceCheckAsync();

            gate.SetResult();

            await forced;

            // The timer probe and the forced probe shared
            // the same in-flight check.
            Assert.Equal(1, checker.CallCount);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task Snapshot_Immutability_OldSnapshotUnchanged()
    {
        StubPrefixUpdateChecker checker = new()
        {
            Result = CreateResult(
                PrefixUpdateCheckStatus.Failed)
        };
        PrefixUpdateMonitor monitor =
            CreateMonitor(checker);

        PrefixUpdateMonitorSnapshot before =
            monitor.GetSnapshot();

        await monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot after =
            monitor.GetSnapshot();

        Assert.NotSame(before, after);
        Assert.Null(before.CurrentResult);
        Assert.Equal(0, before.ConsecutiveFailures);
        Assert.False(before.Checking);
        Assert.Equal(1, after.ConsecutiveFailures);
        Assert.NotNull(after.CurrentResult);
    }

    [Fact]
    public async Task StopAsync_WaitsForInFlightCheck()
    {
        TaskCompletionSource gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StubPrefixUpdateChecker checker = new()
        {
            Gate = gate.Task,
            Result = CreateResult(
                PrefixUpdateCheckStatus.Current)
        };
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMinutes(15));

        await monitor.StartAsync();
        Task check = monitor.ForceCheckAsync();

        Task stop = monitor.StopAsync();
        Assert.False(stop.IsCompleted);

        gate.SetResult();

        await check;
        await stop;

        Assert.False(monitor.GetSnapshot().Running);
        Assert.False(monitor.GetSnapshot().Checking);
        Assert.NotNull(
            monitor.GetSnapshot().CurrentResult);
    }

    [Fact]
    public async Task StopAsync_CancelledCheck_DoesNotCountAsFailure()
    {
        StubPrefixUpdateChecker checker = new()
        {
            ThrowOnCancellation = true
        };
        PrefixUpdateMonitor monitor = CreateMonitor(
            checker,
            interval: TimeSpan.FromMilliseconds(50));

        await monitor.StartAsync();

        await WaitForAsync(
            () => checker.CallCount >= 1);

        await monitor.StopAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            monitor.GetSnapshot();

        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public void Validate_DefaultInterval_IsTwelveHours()
    {
        PrefixUpdateMonitorOptions options = new();

        Assert.Equal(
            TimeSpan.FromHours(12),
            options.Interval);

        PrefixUpdateMonitorOptions.Validate(options);
    }

    [Fact]
    public void Validate_IntervalBelowMinimum_Throws()
    {
        PrefixUpdateMonitorOptions options = new()
        {
            Interval = TimeSpan.FromMinutes(14)
        };

        Assert.Throws<InvalidOperationException>(
            () => PrefixUpdateMonitorOptions.Validate(
                options));
    }

    [Fact]
    public void Validate_IntervalAboveMaximum_Throws()
    {
        PrefixUpdateMonitorOptions options = new()
        {
            Interval = TimeSpan.FromDays(31)
        };

        Assert.Throws<InvalidOperationException>(
            () => PrefixUpdateMonitorOptions.Validate(
                options));
    }

    [Fact]
    public void Validate_BoundaryIntervals_AreAccepted()
    {
        PrefixUpdateMonitorOptions.Validate(
            new PrefixUpdateMonitorOptions
            {
                Interval = TimeSpan.FromMinutes(15)
            });

        PrefixUpdateMonitorOptions.Validate(
            new PrefixUpdateMonitorOptions
            {
                Interval = TimeSpan.FromDays(30)
            });
    }

    private static PrefixUpdateMonitor CreateMonitor(
        StubPrefixUpdateChecker checker,
        TimeSpan? interval = null) =>
        new(
            checker,
            new PrefixUpdateMonitorOptions
            {
                Interval = interval ??
                    TimeSpan.FromHours(12)
            });

    private static PrefixUpdateCheckResult CreateResult(
        PrefixUpdateCheckStatus status,
        string? reason = null) =>
        new()
        {
            Status = status,
            CheckedAt = BaseTime,
            Reason = reason
        };

    private static async Task WaitForAsync(
        Func<bool> condition)
    {
        TimeSpan deadline =
            TimeSpan.FromSeconds(10);
        TimeSpan poll = TimeSpan.FromMilliseconds(10);

        using CancellationTokenSource timeout = new(
            deadline);

        while (!condition())
        {
            await Task.Delay(
                poll,
                timeout.Token);
        }
    }

    private sealed class StubPrefixUpdateChecker :
        IPrefixUpdateChecker
    {
        public int CallCount;
        public PrefixUpdateCheckResult Result =
            new()
            {
                Status = PrefixUpdateCheckStatus.Current,
                CheckedAt = BaseTime
            };
        public Exception? ExceptionToThrow { get; set; }
        public Task? Gate { get; set; }
        public bool ThrowOnCancellation { get; set; }

        public async Task<PrefixUpdateCheckResult>
            CheckAsync(
                CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);

            if (Gate is not null)
            {
                await Gate.WaitAsync(cancellationToken);
            }

            if (ThrowOnCancellation)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Result;
        }
    }
}
