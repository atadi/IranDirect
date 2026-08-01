using IranDirect.Cli;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Ipc;

namespace IranDirect.Core.Tests.Cli;

public sealed class CustomRouteCliRunnerTests
{
    [Fact]
    public async Task RunAsync_UnknownSubcommand_ReturnsUsageExitCode()
    {
        (int exitCode, string stdout, string stderr) =
            await RunAsync(["frobnicate"]);

        Assert.Equal(6, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task RunAsync_MissingValue_ReturnsUsageExitCode()
    {
        (int exitCode, string stdout, string stderr) =
            await RunAsync(["add-domain"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("value is required", stderr);
    }

    [Fact]
    public async Task RunAsync_InvalidId_ReturnsUsageExitCode()
    {
        (int exitCode, string stdout, string stderr) =
            await RunAsync(["enable", "not-a-guid"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("GUID", stderr);
    }

    [Fact]
    public async Task RunAsync_ValidationError_ReturnsFailureExitCode()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = false,
                ErrorCode = "INVALID_CUSTOM_ROUTE",
                Message = "Invalid IPv4 address."
            });

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["add-ip", "not-an-ip"], sender);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains(
            "[INVALID_CUSTOM_ROUTE] Invalid IPv4 address.",
            stderr);
    }

    [Fact]
    public async Task RunAsync_UnknownId_ReturnsFailureExitCode()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = false,
                ErrorCode = "CUSTOM_ROUTE_NOT_FOUND",
                Message = "Custom route was not found."
            });

        (int exitCode, _, string stderr) =
            await RunAsync(
                ["remove", Guid.NewGuid().ToString()],
                sender);

        Assert.Equal(1, exitCode);
        Assert.Contains("was not found", stderr);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsTimeoutExitCode()
    {
        FakeSender sender = new(
            (_, _, _) =>
                throw new TimeoutException(
                    "IranDirect Service is unavailable or did " +
                    "not accept the connection within 5 seconds."));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["list"], sender);

        Assert.Equal(3, exitCode);
        Assert.Contains("unavailable", stderr);
    }

    [Fact]
    public async Task RunAsync_List_RendersRows()
    {
        Guid id = Guid.NewGuid();
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = true,
                Message = "Retrieved 1 custom route(s).",
                CustomRoutes =
                [
                    new CustomRouteEntry
                    {
                        Id = id,
                        Type = CustomRouteEntryType.Cidr,
                        Value = "10.0.0.0/24",
                        Enabled = false,
                        Description = "office"
                    }
                ]
            });

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["list"], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains(id.ToString(), stdout);
        Assert.Contains("10.0.0.0/24", stdout);
        Assert.DoesNotContain("Error", stderr);
    }

    [Fact]
    public async Task RunAsync_Resolve_WithFailures_RendersFailures()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = true,
                Message = "Resolved 1 prefix(es); 1 failure(s).",
                CustomRouteResolution =
                    new CustomRouteResolutionResult
                    {
                        Prefixes = ["8.8.8.8/32"],
                        Failures =
                        [
                            new CustomRouteResolutionFailure
                            {
                                Type = CustomRouteEntryType.Domain,
                                Value = "broken.example",
                                Reason = "NXDOMAIN"
                            }
                        ]
                    }
            });

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["resolve"], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains("Resolved prefixes: 1", stdout);
        Assert.Contains("8.8.8.8/32", stdout);
        Assert.Contains("- [Domain] broken.example: NXDOMAIN", stdout);
    }

    [Fact]
    public async Task RunAsync_Add_DispatchesCommandAndPrintsMessage()
    {
        FakeSender sender = new(
            (command, value, description) =>
            {
                Assert.Equal(
                    IranDirectCommand.CustomRoutesAddDomain,
                    command);
                Assert.Equal("example.com", value);
                Assert.Equal("my site", description);

                return new ServiceResponse
                {
                    Success = true,
                    Message =
                        "Added Domain custom route " +
                        "'example.com'.",
                    CustomRoutes = []
                };
            });

        (int exitCode, string stdout, string stderr) =
            await RunAsync(
                ["add-domain", "example.com", "my site"],
                sender);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "Added Domain custom route 'example.com'.",
            stdout.Trim());
        Assert.Empty(stderr);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)>
        RunAsync(
            string[] args,
            FakeSender? sender = null)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await CustomRouteCliRunner.RunAsync(
            args,
            sender ?? new FakeSender(
                (_, _, _) => new ServiceResponse
                {
                    Success = true,
                    Message = "ok."
                }),
            stdout,
            stderr);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeSender : ICustomRouteCommandSender
    {
        private readonly Func<
            IranDirectCommand,
            string?,
            string?,
            ServiceResponse> _handler;

        public FakeSender(
            Func<
                IranDirectCommand,
                string?,
                string?,
                ServiceResponse> handler)
        {
            _handler = handler;
        }

        public Task<ServiceResponse> SendAsync(
            IranDirectCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _handler(command, value, description));
        }
    }
}
