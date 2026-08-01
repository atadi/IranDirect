using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace IranDirect.Core.Ipc;

public sealed class IranDirectServiceClient :
    ICustomRouteCommandSender
{
    private static readonly TimeSpan DefaultConnectTimeout =
        TimeSpan.FromSeconds(5);

    public async Task<ServiceResponse> SendAsync(
        IranDirectCommand command,
        string? value = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        await using NamedPipeClientStream pipe =
            new(
                ".",
                IranDirectPipeNames.Control,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(DefaultConnectTimeout);

        try
        {
            await pipe.ConnectAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "IranDirect Service is unavailable or did not " +
                "accept the connection within 5 seconds.");
        }

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

        await writer.WriteLineAsync(
            requestJson.AsMemory(),
            cancellationToken);

        string? responseJson =
            await reader.ReadLineAsync(
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