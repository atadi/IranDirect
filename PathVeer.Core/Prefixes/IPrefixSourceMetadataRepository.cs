namespace PathVeer.Core.Prefixes;

public interface IPrefixSourceMetadataRepository
{
    Task<PrefixSourceMetadataDocument> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PrefixSourceMetadataDocument document,
        CancellationToken cancellationToken = default);
}
