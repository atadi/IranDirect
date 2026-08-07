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

        DesiredConfiguration desired =
            await _configurationService.GetAsync(
                stoppingToken);

        if (desired.Enabled)
        {
            _logger.LogInformation(
                "Desired configuration is enabled. " +
                "Running startup cycle.");

            await RunServiceCycleAsync(
                "startup", stoppingToken);
        }
        else
        {
            _logger.LogInformation(
                "Desired configuration is disabled. " +
                "Skipping startup cycle.");
        }

        while (!stoppingToken.IsCancellationRequested
               && desired.AutoRepair)
        {
            try
            {
                await Task.Delay(
                    desired.RepairInterval,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunServiceCycleAsync(
                "periodic", stoppingToken);

            desired =
                await _configurationService.GetAsync(
                    stoppingToken);
        }

        await pipeTask;
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
