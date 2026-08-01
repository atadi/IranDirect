namespace IranDirect.Core.CustomRoutes;

public interface ICustomRouteResolver
{
    Task<CustomRouteResolutionResult> ResolveAsync(
        CancellationToken cancellationToken = default);
}
