using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using IranDirect.Core;
using IranDirect.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace IranDirect.Service.Ipc;

public sealed class NamedPipeCommandServer
{
    public const string PipeName =
        "IranDirect.Control.v1";

    private readonly IranDirectController _controller;
    private readonly ILogger<NamedPipeCommandServer> _logger;

    public NamedPipeCommandServer(
        IranDirectController controller,
        ILogger<NamedPipeCommandServer> logger)
    {
        _controller = controller;
        _logger = logger;
    }

    public async Task RunAsync(
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
                    requestJson ?? "");

            if (request is null)
            {
                throw new InvalidOperationException(
                    "Invalid request.");
            }

            response = await ExecuteAsync(
                request,
                cancellationToken);
        }
        catch (Exception exception)
        {
            response = new ServiceResponse
            {
                Success = false,
                Message = exception.Message
            };
        }

        string responseJson =
            JsonSerializer.Serialize(response);

        await writer.WriteLineAsync(
            responseJson.AsMemory(),
            cancellationToken);
    }

    private async Task<ServiceResponse> ExecuteAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Command.Trim().ToLowerInvariant())
        {
            case "status":
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

            case "update":
            {
                int count =
                    await _controller.UpdatePrefixesAsync(
                        cancellationToken);

                return new ServiceResponse
                {
                    Success = true,
                    Message =
                        $"Updated {count} prefixes."
                };
            }

            case "enable":
            {
                await _controller.EnableAsync(
                    cancellationToken);

                IranDirectStatus status =
                    await _controller.GetStatusAsync(
                        cancellationToken);

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Iran Direct enabled.",
                    Status = status
                };
            }

            case "disable":
            {
                await _controller.DisableAsync(
                    cancellationToken);

                IranDirectStatus status =
                    await _controller.GetStatusAsync(
                        cancellationToken);

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Iran Direct disabled.",
                    Status = status
                };
            }

            case "repair":
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

            default:
                return new ServiceResponse
                {
                    Success = false,
                    Message =
                        $"Unsupported command: {request.Command}"
                };
        }
    }
}