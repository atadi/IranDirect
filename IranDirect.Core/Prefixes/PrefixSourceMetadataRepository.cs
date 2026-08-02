namespace IranDirect.Core.Prefixes;

public sealed class PrefixSourceMetadataRepository :
    IPrefixSourceMetadataRepository
{
    private readonly PrefixSourceMetadataStore _store;
    private readonly PrefixSourceMetadataValidator _validator;

    public PrefixSourceMetadataRepository(
        PrefixSourceMetadataStore store,
        PrefixSourceMetadataValidator validator)
    {
        _store = store;
        _validator = validator;
    }

    public async Task<PrefixSourceMetadataDocument> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        PrefixSourceMetadataDocument document =
            await _store.LoadAsync(cancellationToken);

        _validator.ValidateAndThrow(document);

        return document;
    }

    public async Task SaveAsync(
        PrefixSourceMetadataDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        _validator.ValidateAndThrow(document);

        await _store.SaveAsync(
            document,
            cancellationToken);
    }
}
