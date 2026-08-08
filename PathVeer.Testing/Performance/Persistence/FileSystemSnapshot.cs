namespace PathVeer.Testing.Performance.Persistence;

public sealed class FileSystemSnapshot
{
    private FileSystemSnapshot(IReadOnlyList<SnapshotEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<SnapshotEntry> Entries { get; }

    public static FileSystemSnapshot Capture(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new FileSystemSnapshot([]);
        }

        List<SnapshotEntry> entries = [];

        foreach (string file in Directory.EnumerateFiles(
                     directory,
                     "*",
                     SearchOption.AllDirectories))
        {
            FileInfo info = new(file);

            entries.Add(new SnapshotEntry(
                Path.GetRelativePath(directory, file),
                info.Length));
        }

        entries.Sort((a, b) =>
            string.CompareOrdinal(a.RelativePath, b.RelativePath));

        return new FileSystemSnapshot(entries);
    }

    public IEnumerable<string> OrphanTempFiles => Entries
        .Where(e => e.RelativePath.EndsWith(
            ".tmp",
            StringComparison.OrdinalIgnoreCase))
        .Select(e => e.RelativePath);

    public int FileCount => Entries.Count;

    public long TotalBytes => Entries.Sum(e => e.Length);

    public bool Contains(string relativePath) =>
        Entries.Any(e => e.RelativePath == relativePath);

    public sealed record SnapshotEntry(
        string RelativePath,
        long Length);
}
