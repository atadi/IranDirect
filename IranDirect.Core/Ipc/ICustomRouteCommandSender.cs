namespace IranDirect.Core.Ipc;

public interface ICustomRouteCommandSender
{
    Task<ServiceResponse> SendAsync(
        IranDirectCommand command,
        string? value = null,
        string? description = null,
        CancellationToken cancellationToken = default);
}
