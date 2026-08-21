namespace PathVeer.Core.Ipc;

public interface ICustomRouteCommandSender
{
    Task<ServiceResponse> SendAsync(
        PathVeerCommand command,
        string? value = null,
        string? description = null,
        bool force = false,
        CancellationToken cancellationToken = default);
}
