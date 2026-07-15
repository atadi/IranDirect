using IranDirect.Core;
using IranDirect.Service.Ipc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IranDirect.Service;

public sealed class IranDirectWorker : BackgroundService
{
    private readonly IranDirectController _controller;
    private readonly NamedPipeCommandServer _pipeServer;
    private readonly ILogger<IranDirectWorker> _logger;

    public IranDirectWorker(
        IranDirectController controller,
        NamedPipeCommandServer pipeServer,
        ILogger<IranDirectWorker> logger)
    {
        _controller = controller;
        _pipeServer = pipeServer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IranDirect service started.");

        Task pipeTask =
            _pipeServer.RunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _controller.RepairAsync(
                    stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Automatic route repair failed.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(30),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        await pipeTask;
    }
}