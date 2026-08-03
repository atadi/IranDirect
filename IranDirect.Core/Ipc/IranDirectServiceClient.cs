using System.Text.Json;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Ipc;

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

        try
        {
            connection =
                await _factory.ConnectAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "IranDirect Service is unavailable or did not " +
                "accept the connection within 5 seconds.");
        }

        await using (connection)
        {
            await connection.WriteRequestLineAsync(
                requestJson,
                cancellationToken);

            string? responseJson =
                await connection.ReadResponseLineAsync(
                    cancellationToken);

            ServiceResponse? response =
                JsonSerializer.Deserialize<ServiceResponse>(
                    responseJson ?? "",
                    IranDirectJson.Options);

            return response
                ?? new ServiceResponse
                {
                    Success = false,
                    ErrorCode = "INVALID_RESPONSE",
                    Message =
                        "The service returned an invalid response."
                };
        }
    }

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);
}
