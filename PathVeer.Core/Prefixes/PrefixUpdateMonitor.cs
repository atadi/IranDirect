using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PathVeer.Core.Prefixes;

public sealed class PrefixUpdateMonitor :
    IPrefixUpdateMonitor,
    IDisposable
{
    private const int MaxReasonLength = 300;

    private readonly IPrefixUpdateChecker _checker;
    private readonly PrefixUpdateMonitorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrefixUpdateMonitor> _logger;

    private readonly object _gate = new();

    private volatile PrefixUpdateMonitorSnapshot _snapshot =
        new();

    private CancellationTokenSource? _stoppingSource;
    private Task? _loopTask;
    private Task? _activeCheck;

    public PrefixUpdateMonitor(
        IPrefixUpdateChecker checker,
        PrefixUpdateMonitorOptions options,
        TimeProvider? timeProvider = null,
        ILogger<PrefixUpdateMonitor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(options);

        _checker = checker;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ??
            NullLogger<PrefixUpdateMonitor>.Instance;
    }

    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_snapshot.Running)
            {
                return Task.CompletedTask;
            }

            _stoppingSource = new CancellationTokenSource();
            _snapshot = _snapshot with { Running = true };
            _loopTask = RunLoopAsync(_stoppingSource.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task? loopTask;
        Task? activeCheck;

        lock (_gate)
        {
            if (!_snapshot.Running)
            {
                return;
            }

            _snapshot = _snapshot with { Running = false };
            _stoppingSource?.Cancel();
            loopTask = _loopTask;
            activeCheck = _activeCheck;
        }

        try
        {
            if (loopTask is not null)
            {
                await loopTask.WaitAsync(cancellationToken);
            }

            if (activeCheck is not null)
            {
                await activeCheck.WaitAsync(
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Expected: monitor shutdown cancels the loop.
        }
    }

    public Task ForceCheckAsync(
        CancellationToken cancellationToken = default) =>
        TriggerCheck(cancellationToken);

    public PrefixUpdateMonitorSnapshot GetSnapshot() =>
        _snapshot;

    public void Dispose()
    {
        lock (_gate)
        {
            _stoppingSource?.Cancel();
            _stoppingSource?.Dispose();
            _stoppingSource = null;
        }
    }

    private async Task RunLoopAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan interval = _options.Interval;

            try
            {
                await Task.Delay(
                    interval,
                    _timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await TriggerCheck(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private Task TriggerCheck(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_activeCheck is { IsCompleted: false } active)
            {
                return active;
            }

            Task check = RunCheckAsync(cancellationToken);
            _activeCheck = check;
            return check;
        }
    }

    private async Task RunCheckAsync(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { Checking = true };
        }

        try
        {
            PrefixUpdateCheckResult result =
                await _checker.CheckAsync(cancellationToken);

            RecordResult(result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            RecordCancellation();

            throw;
        }
        catch (Exception ex)
        {
            RecordResult(new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CheckedAt = _timeProvider.GetUtcNow(),
                Reason = Truncate(
                    $"Monitor check failed: {ex.Message}")
            });
        }
        finally
        {
            lock (_gate)
            {
                _snapshot =
                    _snapshot with { Checking = false };
            }
        }
    }

    private void RecordResult(
        PrefixUpdateCheckResult result)
    {
        bool success =
            result.Status != PrefixUpdateCheckStatus.Failed;

        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                CurrentResult = result,
                LastCheckedAt = result.CheckedAt,
                LastSuccessfulCheckAt = success
                    ? result.CheckedAt
                    : _snapshot.LastSuccessfulCheckAt,
                ConsecutiveFailures = success
                    ? 0
                    : _snapshot.ConsecutiveFailures + 1
            };
        }

        if (success)
        {
            _logger.LogInformation(
                "Prefix update check completed. " +
                "Status={Status}",
                result.Status);
        }
        else
        {
            _logger.LogWarning(
                "Prefix update check failed. " +
                "ConsecutiveFailures={ConsecutiveFailures} " +
                "Reason={Reason}",
                _snapshot.ConsecutiveFailures,
                result.Reason ?? "");
        }
    }

    private void RecordCancellation()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                CurrentResult = new PrefixUpdateCheckResult
                {
                    Status = PrefixUpdateCheckStatus.Unknown,
                    CheckedAt = _timeProvider.GetUtcNow(),
                    Reason = "Monitor check cancelled."
                }
            };
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxReasonLength
            ? value
            : value[..MaxReasonLength];
}
