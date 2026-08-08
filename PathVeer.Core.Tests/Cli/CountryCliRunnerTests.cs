using PathVeer.Cli;
using PathVeer.Core.Configuration;
using PathVeer.Core.Ipc;

namespace PathVeer.Core.Tests.Cli;

public sealed class CountryCliRunnerTests
{
    private static readonly DirectCountryCode Ir =
        DirectCountryCode.Parse("IR");

    [Fact]
    public async Task Get_NoSubcommand_ReturnsUsage()
    {
        var (exit, stdout, stderr) = await RunAsync([]);

        Assert.Equal(6, exit);
        Assert.Empty(stdout);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task Get_UnknownSubcommand_ReturnsUsage()
    {
        var (exit, _, stderr) = await RunAsync(["frobnicate"]);

        Assert.Equal(6, exit);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task Get_PrintsRequestedCountryCode()
    {
        FakeSender sender = new((command, _, _) =>
        {
            Assert.Equal(IranDirectCommand.GetConfiguration, command);
            return new ServiceResponse
            {
                Success = true,
                Configuration = new DesiredConfiguration
                {
                    Enabled = true,
                    DirectCountryCode = DirectCountryCode.Parse("IQ")
                }
            };
        });

        var (exit, stdout, _) = await RunAsync(["get"], sender);

        Assert.Equal(0, exit);
        Assert.Equal("IQ", stdout.Trim());
    }

    [Fact]
    public async Task Get_MissingConfig_DefaultsToIr()
    {
        FakeSender sender = new((command, _, _) =>
        {
            Assert.Equal(IranDirectCommand.GetConfiguration, command);
            return new ServiceResponse
            {
                Success = true,
                Configuration = new DesiredConfiguration()
            };
        });

        var (exit, stdout, _) = await RunAsync(["get"], sender);

        Assert.Equal(0, exit);
        Assert.Equal("IR", stdout.Trim());
    }

    [Theory]
    [InlineData("IR")]
    [InlineData("IQ")]
    [InlineData("RO")]
    [InlineData("iq")] // lowercase normalized
    [InlineData("ro")] // lowercase normalized
    public async Task Set_SendsSetConfigurationDirectCountry(
        string input)
    {
        IranDirectCommand? sentCommand = null;
        string? sentValue = null;

        FakeSender sender = new((command, value, _) =>
        {
            sentCommand = command;
            sentValue = value;
            return new ServiceResponse
            {
                Success = true,
                Message = "Direct country set to Iraq (IQ).",
                Configuration = new DesiredConfiguration
                {
                    Enabled = true,
                    DirectCountryCode =
                        DirectCountryCode.Parse("IQ")
                },
                PrefixRefreshed = true
            };
        });

        var (exit, stdout, _) = await RunAsync(
            ["set", input], sender);

        Assert.Equal(0, exit);
        Assert.Equal(
            IranDirectCommand.SetConfigurationDirectCountry,
            sentCommand);
        Assert.Equal(input.ToUpperInvariant(), sentValue);
        Assert.Contains("Iraq (IQ)", stdout);
    }

    [Fact]
    public async Task Set_PreservesEnabledAndProfilePath()
    {
        DesiredConfiguration? persisted = null;

        FakeSender sender = new((command, value, _) =>
        {
            if (command ==
                IranDirectCommand.SetConfigurationDirectCountry)
            {
                persisted = new DesiredConfiguration
                {
                    Enabled = true,
                    VpnProfilePath = "my-profile.ovpn",
                    DirectCountryCode =
                        DirectCountryCode.Parse(value!)
                };

                return new ServiceResponse
                {
                    Success = true,
                    Message = "ok",
                    Configuration = persisted,
                    PrefixRefreshed = true
                };
            }

            return new ServiceResponse { Success = true };
        });

        var (exit, _, _) = await RunAsync(["set", "RO"], sender);

        Assert.Equal(0, exit);
        Assert.NotNull(persisted);
        Assert.True(persisted!.Enabled);
        Assert.Equal("my-profile.ovpn", persisted.VpnProfilePath);
        Assert.Equal(
            DirectCountryCode.Parse("RO"),
            persisted.DirectCountryCode);
    }

    [Fact]
    public async Task Set_InvalidCode_ReturnsUsage()
    {
        FakeSender sender = new((_, _, _) =>
            new ServiceResponse { Success = true });

        var (exit, _, stderr) =
            await RunAsync(["set", "XX"], sender);

        Assert.Equal(6, exit);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task Set_NoArgument_ReturnsUsage()
    {
        FakeSender sender = new((_, _, _) =>
            new ServiceResponse { Success = true });

        var (exit, _, stderr) = await RunAsync(["set"], sender);

        Assert.Equal(6, exit);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task Set_SourceUnavailable_PartialSetReturnsFailure()
    {
        FakeSender sender = new((command, value, _) =>
        {
            Assert.Equal(
                IranDirectCommand.SetConfigurationDirectCountry,
                command);

            // Config accepted (requested policy), but dataset unavailable.
            return new ServiceResponse
            {
                Success = true,
                Message =
                    "Direct country set to Iraq (IQ). " +
                    "Prefix dataset update failed: source down.",
                Configuration = new DesiredConfiguration
                {
                    DirectCountryCode =
                        DirectCountryCode.Parse("IQ")
                },
                PrefixRefreshed = false
            };
        });

        var (exit, stdout, _) =
            await RunAsync(["set", "IQ"], sender);

        // Partial set must surface a non-zero exit while config stays set.
        Assert.Equal(1, exit);
        Assert.Contains("Iraq (IQ)", stdout);
    }

    [Fact]
    public async Task Set_ServiceFailure_ReturnsFailure()
    {
        FakeSender sender = new((_, _, _) =>
            new ServiceResponse
            {
                Success = false,
                ErrorCode = "INVALID_COUNTRY_CODE",
                Message = "bad"
            });

        var (exit, _, stderr) =
            await RunAsync(["set", "IQ"], sender);

        Assert.Equal(1, exit);
        Assert.Contains("[INVALID_COUNTRY_CODE] bad", stderr);
    }

    [Fact]
    public async Task List_PrintsCatalogWithNames()
    {
        FakeSender sender = new((_, _, _) =>
            new ServiceResponse { Success = true });

        var (exit, stdout, _) = await RunAsync(["list"], sender);

        Assert.Equal(0, exit);
        // All supported codes are listed (deterministic, not just IR/IQ/RO).
        Assert.Contains("IR", stdout);
        Assert.Contains("IQ", stdout);
        Assert.Contains("RO", stdout);
        Assert.Contains("Iran", stdout);
        Assert.Contains("Iraq", stdout);
        Assert.Contains("Romania", stdout);
        // At least 200 ISO codes present (catalog is not a 3-entry allow-list).
        int lines = stdout.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(lines > 200,
            $"expected many country rows, got {lines}");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)>
        RunAsync(
            string[] args,
            FakeSender? sender = null)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await CountryCliRunner.RunAsync(
            args,
            sender ?? new FakeSender(
                (_, _, _) => new ServiceResponse { Success = true }),
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
