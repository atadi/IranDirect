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
            await LoadOrDefaultAsync(cancellationToken);

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
            await LoadOrDefaultAsync(cancellationToken);

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

    // A write command (enable/disable/set-profile) is how the authoritative
    // configuration is first created, so a MISSING file is treated as "start
    // from a valid default and apply the change". A CORRUPT file must still
    // fail closed and preserve the original (it is never silently reset).
    private async Task<DesiredConfiguration> LoadOrDefaultAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _store.LoadAsync(cancellationToken);
        }
        catch (DesiredConfigurationMissingException)
        {
            return new DesiredConfiguration();
        }
    }
}