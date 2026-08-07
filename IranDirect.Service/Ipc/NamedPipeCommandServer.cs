using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Vpn;
using IranDirect.Service.Operations;
using Microsoft.Extensions.Logging;
using IranDirect.Core.Observability.Telemetry;

namespace IranDirect.Service.Ipc;

public class NamedPipeCommandServer
{
    private readonly IranDirectController _controller;
    private readonly OperationCoordinator _operations;
    private readonly OpenVpnEndpointProvider _vpnEndpointProvider;
    private readonly IranDirectDiagnosticsService _diagnosticsService;
    private readonly DesiredConfigurationService _configurationService;
    private readonly RuntimeCoordinator _runtimeCoordinator;
    private readonly CustomRouteCommandHandler _customRoutes;
    private readonly RuntimeSnapshotCommandHandler _runtimeSnapshot;
    private readonly PrefixUpdateCheckCommandHandler
        _prefixUpdateCheck;
    private readonly DiagnosticCommandHandler _diagnosticsHandler;
    private readonly ExecutionPreviewCommandHandler
        _executionPreviewHandler;
    private readonly SupportBundleCommandHandler
        _supportBundleHandler;
    private readonly ILogger<NamedPipeCommandServer> _logger;

    public NamedPipeCommandServer(
        IranDirectController controller,
        OperationCoordinator operations,
        OpenVpnEndpointProvider vpnEndpointProvider,
        IranDirectDiagnosticsService diagnosticsService,
        DesiredConfigurationService configurationService,
        RuntimeCoordinator runtimeCoordinator,
        CustomRouteCommandHandler customRoutes,
        RuntimeSnapshotCommandHandler runtimeSnapshot,
        PrefixUpdateCheckCommandHandler prefixUpdateCheck,
        DiagnosticCommandHandler diagnosticsHandler,
        ExecutionPreviewCommandHandler executionPreviewHandler,
        SupportBundleCommandHandler supportBundleHandler,
        ILogger<NamedPipeCommandServer> logger)
    {
        _controller = controller;
        _operations = operations;
        _vpnEndpointProvider = vpnEndpointProvider;
        _diagnosticsService = diagnosticsService;
        _configurationService = configurationService;
        _runtimeCoordinator = runtimeCoordinator;
        _customRoutes = customRoutes;
        _runtimeSnapshot = runtimeSnapshot;
        _prefixUpdateCheck = prefixUpdateCheck;
        _diagnosticsHandler = diagnosticsHandler;
        _executionPreviewHandler = executionPreviewHandler;
        _supportBundleHandler = supportBundleHandler;
        _logger = logger;
    }

    public virtual async Task RunAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await HandleOneClientAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Named-pipe request failed.");

                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    cancellationToken);
            }
        }
    }

    private async Task HandleOneClientAsync(
        CancellationToken cancellationToken)
    {
        await using NamedPipeServerStream pipe =
            new(
                IranDirectPipeNames.Control,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync(
            cancellationToken);

        using StreamReader reader =
            new(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

        using StreamWriter writer =
            new(
                pipe,
                new UTF8Encoding(false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true
            };

        string? requestJson =
            await reader.ReadLineAsync(
                cancellationToken);

        ServiceResponse response;

        try
        {
            ServiceRequest? request =
                JsonSerializer.Deserialize<ServiceRequest>(
                    requestJson ?? "",
                    IranDirectJson.Options);

            if (request is null)
            {
                response = Failure(
                    "INVALID_REQUEST",
                    "The request was invalid.");
            }
            else if (request.ProtocolVersion !=
                     IpcProtocol.CurrentVersion)
            {
                response = Failure(
                    "UNSUPPORTED_PROTOCOL",
                    $"Protocol version " +
                    $"{request.ProtocolVersion} is not supported.");
            }
            else
            {
                response = await ExecuteWithTelemetryAsync(
                    request,
                    cancellationToken);
            }
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Invalid named-pipe JSON request.");

            response = Failure(
                "INVALID_JSON",
                "The request could not be parsed.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "IranDirect command failed.");

            response = Failure(
                "COMMAND_FAILED",
                exception.Message);
        }

        string responseJson =
            JsonSerializer.Serialize(
                response,
                IranDirectJson.Options);

        await writer.WriteLineAsync(
            responseJson.AsMemory(),
            cancellationToken);
    }

    private async Task<ServiceResponse> ExecuteWithTelemetryAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        // Independent server-side dispatch span. The named-pipe server runs in
        // a separate process from the client, so this cannot be parented to the
        // client IranDirect.IpcRequest root without changing the wire protocol.
        using IpcDispatchTelemetry.IpcDispatchScope dispatch =
            IpcDispatchTelemetry.Start(request.Command);

        try
        {
            ServiceResponse response = await ExecuteAsync(
                request,
                cancellationToken);
            dispatch.CompleteSuccess();
            return response;
        }
        catch (Exception exception)
        {
            dispatch.CompleteFailure(exception);
            throw;
        }
    }

    private Task<ServiceResponse> ExecuteAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            IranDirectCommand.Status =>
                GetStatusAsync(cancellationToken),

            IranDirectCommand.UpdatePrefixes =>
                _operations.ExecuteAsync(
                    UpdatePrefixesAsync,
                    cancellationToken),

            IranDirectCommand.Enable =>
                _operations.ExecuteAsync(
                    EnableAsync,
                    cancellationToken),

            IranDirectCommand.Disable =>
                _operations.ExecuteAsync(
                    DisableAsync,
                    cancellationToken),

            IranDirectCommand.Repair =>
                _operations.ExecuteAsync(
                    RepairAsync,
                    cancellationToken),

            IranDirectCommand.VpnEndpoints =>
                GetVpnEndpointsAsync(cancellationToken),

            IranDirectCommand.Diagnostics =>
                GetDiagnosticsAsync(cancellationToken),

            IranDirectCommand.GetConfiguration =>
                GetConfigurationAsync(cancellationToken),

            IranDirectCommand.SetConfigurationEnabled =>
                SetConfigurationEnabledAsync(
                    request,
                    cancellationToken),

            IranDirectCommand.SetConfigurationProfilePath =>
                SetConfigurationProfilePathAsync(
                    request,
                    cancellationToken),

            IranDirectCommand.RuntimePlan =>
                GetRuntimePlanAsync(cancellationToken),

            IranDirectCommand.CustomRoutesList =>
                _customRoutes.ListAsync(cancellationToken),

            IranDirectCommand.CustomRoutesAddDomain =>
                _customRoutes.AddAsync(
                    CustomRouteEntryType.Domain,
                    request.Value,
                    request.Description,
                    cancellationToken),

            IranDirectCommand.CustomRoutesAddIp =>
                _customRoutes.AddAsync(
                    CustomRouteEntryType.IpAddress,
                    request.Value,
                    request.Description,
                    cancellationToken),

            IranDirectCommand.CustomRoutesAddCidr =>
                _customRoutes.AddAsync(
                    CustomRouteEntryType.Cidr,
                    request.Value,
                    request.Description,
                    cancellationToken),

            IranDirectCommand.CustomRoutesEnable =>
                _customRoutes.SetEnabledAsync(
                    request.Value,
                    enabled: true,
                    cancellationToken),

            IranDirectCommand.CustomRoutesDisable =>
                _customRoutes.SetEnabledAsync(
                    request.Value,
                    enabled: false,
                    cancellationToken),

            IranDirectCommand.CustomRoutesRemove =>
                _customRoutes.RemoveAsync(
                    request.Value,
                    cancellationToken),

            IranDirectCommand.CustomRoutesResolve =>
                _customRoutes.ResolveAsync(cancellationToken),

            IranDirectCommand.CustomRoutesCacheStatus =>
                _customRoutes.CacheStatusAsync(cancellationToken),

            IranDirectCommand.CustomRoutesInvalidateCache =>
                _customRoutes.InvalidateCacheAsync(
                    request.Value,
                    cancellationToken),

            IranDirectCommand.CustomRoutesInvalidateAllCaches =>
                _customRoutes.InvalidateAllCachesAsync(
                    cancellationToken),

            IranDirectCommand.RuntimeSnapshot =>
                _runtimeSnapshot.GetAsync(cancellationToken),

            IranDirectCommand.PrefixUpdateCheckNow =>
                _prefixUpdateCheck.CheckNowAsync(
                    cancellationToken),

            IranDirectCommand.ExecutionPreview =>
                _executionPreviewHandler.GetAsync(
                    cancellationToken),

            IranDirectCommand.SupportBundleExport =>
                _supportBundleHandler.ExportAsync(
                    request.Value ?? string.Empty,
                    cancellationToken),

            _ => Task.FromResult(
                Failure(
                    "UNSUPPORTED_COMMAND",
                    $"Unsupported command: {request.Command}"))
        };
    }

    private async Task<ServiceResponse> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        IranDirectStatus status =
            await _controller.GetStatusAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "Status retrieved.",
            Status = status
        };
    }

    private async Task<ServiceResponse> UpdatePrefixesAsync(
        CancellationToken cancellationToken)
    {
        int count =
            await _controller.UpdatePrefixesAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = $"Updated {count} prefixes.",
            PrefixCount = count
        };
    }

    private async Task<ServiceResponse> EnableAsync(
        CancellationToken cancellationToken)
    {
        RuntimeCycleExecutionResult cycleResult =
            await _controller.EnableAsync(
                cancellationToken);

        IranDirectStatus status =
            await _controller.GetStatusAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = cycleResult.IsSuccess,
            Message = cycleResult.IsSuccess
                ? "Iran Direct enabled."
                : "Enable failed: "
                  + cycleResult.Execution.ErrorMessage,
            Status = status,
            Decision = cycleResult.Decision,
            Execution = cycleResult.Execution
        };
    }

    private async Task<ServiceResponse> DisableAsync(
        CancellationToken cancellationToken)
    {
        RuntimeCycleExecutionResult cycleResult =
            await _controller.DisableAsync(
                cancellationToken);

        IranDirectStatus status =
            await _controller.GetStatusAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = cycleResult.IsSuccess,
            Message = cycleResult.IsSuccess
                ? "Iran Direct disabled."
                : "Disable failed: "
                  + cycleResult.Execution.ErrorMessage,
            Status = status,
            Decision = cycleResult.Decision,
            Execution = cycleResult.Execution
        };
    }

    private async Task<ServiceResponse> RepairAsync(
        CancellationToken cancellationToken)
    {
        await _controller.RepairAsync(
            cancellationToken);

        IranDirectStatus status =
            await _controller.GetStatusAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "Repair completed.",
            Status = status
        };
    }

    private async Task<ServiceResponse>
        GetVpnEndpointsAsync(
            CancellationToken cancellationToken)
    {
        IReadOnlyList<ResolvedVpnEndpoint> endpoints =
            await _vpnEndpointProvider.GetEndpointsAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                $"Resolved {endpoints.Count} VPN endpoint(s).",
            VpnEndpoints = endpoints
        };
    }

    private async Task<ServiceResponse> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        IranDirectDiagnostics diagnostics =
            await _diagnosticsService.RunAsync(
                cancellationToken);

        ServiceResponse legacyResponse =
            new ServiceResponse
            {
                Success = true,
                Message =
                    $"Diagnostics completed: " +
                    $"{diagnostics.OverallSeverity}.",
                Diagnostics = diagnostics
            };

        ServiceResponse reportResponse =
            await _diagnosticsHandler.RunAsync(
                cancellationToken);

        return legacyResponse with
        {
            Report = reportResponse.Report
        };
    }
    private async Task<ServiceResponse> GetConfigurationAsync(
        CancellationToken cancellationToken)
    {
        DesiredConfiguration configuration;

        try
        {
            configuration =
                await _configurationService.GetAsync(
                    cancellationToken);
        }
        catch (DesiredConfigurationException exception)
        {
            return Failure(
                "CONFIGURATION_UNAVAILABLE",
                exception.Message);
        }

        return new ServiceResponse
        {
            Success = true,
            Message = "Desired configuration retrieved.",
            Configuration = configuration
        };
    }

    private async Task<ServiceResponse>
        SetConfigurationEnabledAsync(
            ServiceRequest request,
            CancellationToken cancellationToken)
    {
        if (!bool.TryParse(
                request.Value,
                out bool enabled))
        {
            return Failure(
                "INVALID_CONFIGURATION_VALUE",
                "Enabled must be true or false.");
        }

        DesiredConfiguration configuration =
            await _configurationService.SetEnabledAsync(
                enabled,
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                $"Desired configuration enabled set to " +
                $"{enabled}.",
            Configuration = configuration
        };
    }

    private async Task<ServiceResponse>
        SetConfigurationProfilePathAsync(
            ServiceRequest request,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Failure(
                "INVALID_CONFIGURATION_VALUE",
                "VPN profile path is required.");
        }

        DesiredConfiguration configuration =
            await _configurationService.SetProfilePathAsync(
                request.Value,
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                "Desired configuration profile path updated.",
            Configuration = configuration
        };
    }
    private async Task<ServiceResponse> GetRuntimePlanAsync(
        CancellationToken cancellationToken)
    {
        RuntimePlanSnapshot snapshot =
            await _runtimeCoordinator.BuildPlanAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                snapshot.Desired.CanReconcile
                    ? "Runtime plan is safe to reconcile."
                    : "Runtime plan contains blockers.",
            RuntimePlan = snapshot
        };
    }
    private static ServiceResponse Failure(
        string errorCode,
        string message)
    {
        return new ServiceResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message
        };
    }
}