using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using IranDirect.Core.Ipc;

namespace IranDirect.Tray.Ipc;

public sealed class IranDirectServiceClient
{
    private const string PipeName =
        "IranDirect.Control.v1";

    public async Task<ServiceResponse> SendAsync(
        string command,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        await using NamedPipeClientStream pipe =
            new(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

        await pipe.ConnectAsync(
            timeout: 5000,
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

        ServiceRequest request = new()
        {
            Command = command,
            Value = value
        };

        string requestJson =
            JsonSerializer.Serialize(request);

        await writer.WriteLineAsync(
            requestJson.AsMemory(),
            cancellationToken);

        string? responseJson =
            await reader.ReadLineAsync(
                cancellationToken);

        ServiceResponse? response =
            JsonSerializer.Deserialize<ServiceResponse>(
                responseJson ?? "");

        return response
            ?? new ServiceResponse
            {
                Success = false,
                Message =
                    "The service returned an invalid response."
            };
    }
}
