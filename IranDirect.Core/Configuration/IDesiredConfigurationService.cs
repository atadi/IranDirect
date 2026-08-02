namespace IranDirect.Core.Configuration;

public interface IDesiredConfigurationService
{
    Task<DesiredConfiguration> GetAsync(
        CancellationToken cancellationToken = default);
}
