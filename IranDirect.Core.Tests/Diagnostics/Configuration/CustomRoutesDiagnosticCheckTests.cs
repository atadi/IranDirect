using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;

namespace IranDirect.Core.Tests.Diagnostics.Configuration;

public sealed class CustomRoutesDiagnosticCheckTests
{
    private static IReadOnlyList<CustomRouteEntry> ValidEntries() =>
        [
            new CustomRouteEntry
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.Domain,
                Value = "example.com",
                Enabled = true
            },
            new CustomRouteEntry
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.IpAddress,
                Value = "192.168.1.1",
                Enabled = true
            }
        ];

    private sealed class FakeRouteRepository :
        ICustomRouteRepository
    {
        private readonly IReadOnlyList<CustomRouteEntry> _entries;

        public FakeRouteRepository(
            IReadOnlyList<CustomRouteEntry> entries)
        {
            _entries = entries;
        }

        public Task<IReadOnlyList<CustomRouteEntry>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_entries);
        }

        public Task MutateAsync(
            Func<CustomRouteCollection, CustomRouteCollection> transform,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CheckAsync_ValidRoutes_ReturnsPassed()
    {
        var check = new CustomRoutesDiagnosticCheck(
            new FakeRouteRepository(ValidEntries()));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_DuplicateIds_ReturnsFailed()
    {
        var id = Guid.NewGuid();

        var entries = new List<CustomRouteEntry>
        {
            new()
            {
                Id = id,
                Type = CustomRouteEntryType.Domain,
                Value = "example.com",
                Enabled = true
            },
            new()
            {
                Id = id,
                Type = CustomRouteEntryType.IpAddress,
                Value = "192.168.1.1",
                Enabled = true
            }
        };

        var check = new CustomRoutesDiagnosticCheck(
            new FakeRouteRepository(entries));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_InvalidEntryEmptyValue_ReturnsFailed()
    {
        var entries = new List<CustomRouteEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.Domain,
                Value = "",
                Enabled = true
            }
        };

        var check = new CustomRoutesDiagnosticCheck(
            new FakeRouteRepository(entries));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_DuplicateEnabledValues_ReturnsWarning()
    {
        var entries = new List<CustomRouteEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.Domain,
                Value = "example.com",
                Enabled = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.IpAddress,
                Value = "example.com",
                Enabled = true
            }
        };

        var check = new CustomRoutesDiagnosticCheck(
            new FakeRouteRepository(entries));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_DuplicateValuesOneDisabled_IgnoresDisabled()
    {
        var entries = new List<CustomRouteEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.Domain,
                Value = "example.com",
                Enabled = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = CustomRouteEntryType.IpAddress,
                Value = "example.com",
                Enabled = false
            }
        };

        var check = new CustomRoutesDiagnosticCheck(
            new FakeRouteRepository(entries));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_RepoThrows_ReturnsFailed()
    {
        var check = new CustomRoutesDiagnosticCheck(
            new ThrowingRouteRepository());

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    private sealed class ThrowingRouteRepository :
        ICustomRouteRepository
    {
        public Task<IReadOnlyList<CustomRouteEntry>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }

        public Task MutateAsync(
            Func<CustomRouteCollection, CustomRouteCollection> transform,
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }
    }
}