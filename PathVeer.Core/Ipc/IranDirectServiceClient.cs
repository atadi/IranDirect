using System.Text.Json;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Ipc;

public sealed class IranDirectServiceClient :
    ICustomRouteCommandSender
{
    private static readonly TimeSpan DefaultConnectTimeout =
        TimeSpan.FromSeconds(5);

    private readonly INamedPipeClientFactory _factory;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public IranDirectServiceClient(
        INamedPipeClientFactory? factory = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        _factory = factory ?? new NamedPipeClientFactory();
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public async Task<ServiceResponse> SendAsync(
        IranDirectCommand command,
        string? value = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ServiceRequest request = new()
        {
            Command = command,
            Value = value,
            Description = description
        };

        string requestJson =
            JsonSerializer.Serialize(
                request,
                IranDirectJson.Options);

        using IpcRequestTelemetry.IpcRequestScope root =
            IpcRequestTelemetry.Start(command);

        try
        {
            if (ShouldFailAt(FaultInjectionPoint.NamedPipeSend))
            {
                throw new FaultInjectionException(
                    FaultInjectionPoint.NamedPipeSend);
            }

            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutSource.CancelAfter(DefaultConnectTimeout);

            INamedPipeClientConnection connection;

            using (IpcRequestTelemetry.IpcChildScope connect =
                root.StartConnect())
            {
                try
                {
                    connection =
                        await _factory.ConnectAsync(timeoutSource.Token);
                    connect.CompleteSuccess();
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    // Connect timeout (not caller cancellation) is remapped to
                    // TimeoutException; surface it on the child now.
                    connect.CompleteTimeout();
                    throw new TimeoutException(
                        "IranDirect Service is unavailable or did not " +
                        "accept the connection within 5 seconds.");
                }
                catch (OperationCanceledException)
                {
                    connect.CompleteCancelled();
                    throw;
                }
                catch (Exception ex)
                {
                    connect.CompleteFailure(ex);
                    throw;
                }
            }

            await using (connection)
            {
                using (IpcRequestTelemetry.IpcChildScope send =
                    root.StartSend())
                {
                    try
                    {
                        await connection.WriteRequestLineAsync(
                            requestJson,
                            cancellationToken);
                        send.CompleteSuccess();
                    }
                    catch (Exception ex)
                    {
                        send.CompleteFailure(ex);
                        throw;
                    }
                }

                string? responseJson;
                using (IpcRequestTelemetry.IpcChildScope receive =
                    root.StartReceive())
                {
                    try
                    {
                        responseJson =
                            await connection.ReadResponseLineAsync(
                                cancellationToken);
                        receive.CompleteSuccess();
                    }
                    catch (Exception ex)
                    {
                        receive.CompleteFailure(ex);
                        throw;
                    }
                }

                ServiceResponse? response =
                    JsonSerializer.Deserialize<ServiceResponse>(
                        responseJson ?? "",
                        IranDirectJson.Options);

                if (response is null)
                {
                    response = new ServiceResponse
                    {
                        Success = false,
                        ErrorCode = "INVALID_RESPONSE",
                        Message =
                            "The service returned an invalid response."
                    };
                }

                string? failureCategory =
                    (!response.Success &&
                     response.ErrorCode == "INVALID_RESPONSE")
                        ? IranDirectTagValues.FailureInvalidResponse
                        : null;

                root.Complete(
                    response.Success
                        ? TelemetryOutcome.Success
                        : TelemetryOutcome.Failure,
                    failureCategory);

                return response;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            root.CompleteCancelled();
            throw;
        }
        catch (TimeoutException)
        {
            root.CompleteTimeout();
            throw;
        }
        catch (FaultInjectionException ex)
        {
            root.CompleteFailure(ex);
            throw;
        }
        catch (Exception ex)
        {
            root.CompleteFailure(ex);
            throw;
        }
    }

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);
}
