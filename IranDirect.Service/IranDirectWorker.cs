using System.Diagnostics;
using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Service.Ipc;
using IranDirect.Service.Operations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IranDirect.Service;

public sealed class IranDirectWorker : BackgroundService
{
    // While the authoritative configuration is missing or corrupt the runtime
    // must not reconcile, so it polls at a calm interval (rather than the
    // configured RepairInterval, which is unavailable) until a valid
    // configuration appears. This keeps the host alive without a crash-loop.
    private static readonly TimeSpan UnconfiguredRetryInterval =
        TimeSpan.FromSeconds(30);
    private readonly IranDirectController _controller;
    private readonly NamedPipeCommandServer _pipeServer;
    private readonly OperationCoordinator _operations;
    private readonly DesiredConfigurationService _configurationService;
    private readonly RouteMutationRecovery _recovery;
    private readonly ILogger<IranDirectWorker> _logger;

    public IranDirectWorker(
        IranDirectController controller,
        NamedPipeCommandServer pipeServer,
        OperationCoordinator operations,
        DesiredConfigurationService configurationService,
        RouteMutationRecovery recovery,
        ILogger<IranDirectWorker> logger)
    {
        _controller = controller;
        _pipeServer = pipeServer;
        _operations = operations;
        _configurationService = configurationService;
        _recovery = recovery;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IranDirect service started.");

        Task pipeTask =
            _pipeServer.RunAsync(stoppingToken);

        // Recover any route mutation interrupted by a previous process crash
        // BEFORE normal planning reads ownership state. Runs through the
        // OperationCoordinator gate so it cannot race a startup cycle or an
        // early IPC command.
        await _operations.ExecuteAsync(
            _recovery.RecoverAsync, stoppingToken);

        // Load the authoritative configuration. A missing or corrupt file must
        // NOT be substituted with a fabricated disabled configuration; it fails
        // closed. While the configuration is unavailable the runtime must not
        // reconcile (no route mutation), but the host stays alive so a valid
        // configuration can be supplied (e.g. via a command) and picked up on
        // the next poll without a restart.
        DesiredConfiguration? desired =
            await TryLoadConfigurationAsync(stoppingToken);

        // The generic prefix source (Phase 35.3) resolves any valid ISO 3166-1
        // alpha-2 country via DesiredConfiguration.DirectCountryCode. The source
        // contract (a valid dataset must be obtainable for the selected country)
        // is enforced at acquisition time, not here. A recognized country that
        // cannot currently be acquired fails safely inside the cycle (the
        // dataset is not replaced, existing routes are preserved), so
        // reconciliation is always attempted when the configuration is enabled.
        bool shouldReconcile = desired is { Enabled: true };

        if (shouldReconcile)
        {
            _logger.LogInformation(
                "Desired configuration is enabled. " +
                "Running startup cycle.");
        }
        else if (desired is not null)
        {
            _logger.LogInformation(
                "Desired configuration is disabled. " +
                "Skipping startup cycle.");
        }

        if (shouldReconcile)
        {
            await RunServiceCycleAsync(
                "startup", stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested
               && (desired is null || desired.AutoRepair))
        {
            TimeSpan wait = desired is null
                ? UnconfiguredRetryInterval
                : desired.RepairInterval;

            try
            {
                await Task.Delay(
                    wait,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                desired =
                    await _configurationService.GetAsync(
                        stoppingToken);
            }
            catch (DesiredConfigurationException exception)
            {
                _logger.LogError(
                    exception,
                    "Desired configuration is unavailable; reconciliation is " +
                    "skipped until a valid configuration is present.");

                continue;
            }

            if (desired.Enabled)
            {
                await RunServiceCycleAsync(
                    "periodic", stoppingToken);
            }
        }

        await pipeTask;
    }

    private async Task<DesiredConfiguration?> TryLoadConfigurationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _configurationService.GetAsync(
                cancellationToken);
        }
        catch (DesiredConfigurationException exception)
        {
            _logger.LogError(
                exception,
                "Desired configuration is unavailable; reconciliation is " +
                "skipped until a valid configuration is present.");

            return null;
        }
    }

    private async Task RunServiceCycleAsync(
        string trigger,
        CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();

        try
        {
            RuntimeCycleExecutionResult result =
                await _operations.ExecuteAsync(
                    _controller.RunCycleAsync,
                    cancellationToken);

            TimeSpan duration =
                Stopwatch.GetElapsedTime(start);

            RuntimeDecision decision = result.Decision;
            RuntimeExecutionResult execution =
                result.Execution;

            int succeededSteps = execution.StepResults
                .Count(sr =>
                    sr.Status ==
                    RuntimeExecutionStepStatus
                        .Succeeded);

            _logger.LogInformation(
                "Cycle completed. " +
                "Trigger={Trigger} " +
                "ReconciliationStatus={RecStatus} " +
                "ExecutionStatus={ExecStatus} " +
                "PlannedSteps={PlannedSteps} " +
                "SucceededSteps={SucceededSteps} " +
                "ErrorMessage={ErrorMsg} " +
                "DurationMs={DurationMs}",
                trigger,
                decision.Reconciliation.Status,
                execution.Status,
                decision.ExecutionPlan.Count,
                succeededSteps,
                execution.ErrorMessage ?? "",
                duration.TotalMilliseconds);
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            _logger.LogInformation(
                "Cycle cancelled. " +
                "Trigger={Trigger}",
                trigger);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cycle failed. " +
                "Trigger={Trigger}",
                trigger);
        }
    }
}
