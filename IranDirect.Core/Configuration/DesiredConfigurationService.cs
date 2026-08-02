namespace IranDirect.Core.Configuration;

public sealed class DesiredConfigurationService :
    IDesiredConfigurationService
{
    private readonly DesiredConfigurationStore _store;

    public DesiredConfigurationService(
        DesiredConfigurationStore store)
    {
        _store = store;
    }

    public Task<DesiredConfiguration> GetAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync(cancellationToken);
    }

    public async Task<DesiredConfiguration> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        DesiredConfiguration current =
            await _store.LoadAsync(cancellationToken);

        DesiredConfiguration updated =
            current with
            {
                Enabled = enabled
            };

        await _store.SaveAsync(
            updated,
            cancellationToken);

        return updated;
    }

    public async Task<DesiredConfiguration> SetProfilePathAsync(
        string profilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            profilePath);

        DesiredConfiguration current =
            await _store.LoadAsync(cancellationToken);

        DesiredConfiguration updated =
            current with
            {
                VpnProfilePath = profilePath
            };

        await _store.SaveAsync(
            updated,
            cancellationToken);

        return updated;
    }
}