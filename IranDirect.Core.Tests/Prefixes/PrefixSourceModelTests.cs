using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixSourceModelTests
{
    [Fact]
    public void Descriptor_Validate_ThrowsForEmptyId()
    {
        PrefixSourceDescriptor descriptor = CreateDescriptor()
            with { Id = "" };

        Assert.Throws<InvalidOperationException>(
            () => PrefixSourceDescriptor.Validate(descriptor));
    }

    [Fact]
    public void Descriptor_Validate_ThrowsForEmptyDisplayName()
    {
        PrefixSourceDescriptor descriptor = CreateDescriptor()
            with { DisplayName = "  " };

        Assert.Throws<InvalidOperationException>(
            () => PrefixSourceDescriptor.Validate(descriptor));
    }

    [Fact]
    public void Descriptor_Validate_ThrowsForEmptyFormat()
    {
        PrefixSourceDescriptor descriptor = CreateDescriptor()
            with { Format = "" };

        Assert.Throws<InvalidOperationException>(
            () => PrefixSourceDescriptor.Validate(descriptor));
    }

    [Fact]
    public void Descriptor_Validate_AcceptsValidDescriptor()
    {
        PrefixSourceDescriptor descriptor = CreateDescriptor();

        PrefixSourceDescriptor.Validate(descriptor);
    }

    [Fact]
    public void OfficialIranPrefixSource_Descriptor_IsStable()
    {
        PrefixSourceDescriptor first =
            OfficialIranPrefixSource.Descriptor;
        PrefixSourceDescriptor second =
            OfficialIranPrefixSource.Descriptor;

        Assert.Equal(first, second);
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.False(string.IsNullOrWhiteSpace(first.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(first.Format));
        Assert.False(string.IsNullOrWhiteSpace(first.ParserVersion));
        Assert.Contains("stat.ripe.net", first.Uri);
    }

    [Fact]
    public void OfficialIranPrefixSource_Descriptor_HasNoSecrets()
    {
        PrefixSourceDescriptor descriptor =
            OfficialIranPrefixSource.Descriptor;

        foreach (string secret in new[]
                 {
                     "token", "key", "secret",
                     "password", "apikey"
                 })
        {
            Assert.DoesNotContain(
                secret,
                descriptor.Uri,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FetchResult_ConstructsWithPopulatedValues()
    {
        DateTimeOffset startedAt =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset completedAt =
            new(2026, 1, 1, 0, 0, 5, TimeSpan.Zero);

        PrefixSourceFetchResult result = new()
        {
            Source = CreateDescriptor(),
            Prefixes = ["1.2.3.0/24", "10.0.0.0/8"],
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Duration = completedAt - startedAt,
            ETag = "\"abc123\"",
            LastModified = startedAt,
            ContentHash = new string('a', 64),
            ContentLength = 512,
            NotModified = false
        };

        Assert.Equal(
            CreateDescriptor(),
            result.Source);
        Assert.Equal(2, result.Prefixes.Count);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(completedAt, result.CompletedAt);
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            result.Duration);
        Assert.Equal("\"abc123\"", result.ETag);
        Assert.Equal(startedAt, result.LastModified);
        Assert.Equal(64, result.ContentHash.Length);
        Assert.Equal(512, result.ContentLength);
        Assert.False(result.NotModified);
    }

    [Fact]
    public void FetchResult_EmptyResult_HasSaneDefaults()
    {
        PrefixSourceFetchResult result = new();

        Assert.Empty(result.Prefixes);
        Assert.False(result.NotModified);
        Assert.Null(result.ETag);
        Assert.Null(result.LastModified);
        Assert.Null(result.ContentHash);
        Assert.Null(result.ContentLength);
        Assert.Equal(default, result.Duration);
    }

    private static PrefixSourceDescriptor CreateDescriptor() =>
        new()
        {
            Id = "test-source",
            DisplayName = "Test Source",
            Uri = "https://example.test/data.json",
            Format = "example-json",
            ParserVersion = "1"
        };
}
