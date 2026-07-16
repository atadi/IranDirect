namespace IranDirect.Core.Vpn;

public sealed class OpenVpnEndpointProvider
{
    private readonly string _profilePath;
    private readonly OpenVpnProfileParser _parser;
    private readonly VpnEndpointResolver _resolver;

    public OpenVpnEndpointProvider(
        string profilePath,
        OpenVpnProfileParser parser,
        VpnEndpointResolver resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            profilePath);

        _profilePath = profilePath;
        _parser = parser;
        _resolver = resolver;
    }

    public async Task<IReadOnlyList<ResolvedVpnEndpoint>>
        GetEndpointsAsync(
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VpnEndpointDefinition> definitions =
            await _parser.ParseAsync(
                _profilePath,
                cancellationToken);

        return await _resolver.ResolveAsync(
            definitions,
            cancellationToken);
    }
}