using PathVeer.Service.Observability;
using Xunit;

namespace PathVeer.Service.Tests.Observability;

public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void Defaults_AreDisabledAndSafe()
    {
        ObservabilityOptions options = new();

        Assert.False(options.Enabled);
        Assert.True(options.TracingEnabled);
        Assert.True(options.MetricsEnabled);
        Assert.Equal(1.0, options.SamplingRatio);
        Assert.Equal(5, options.ExportTimeoutSeconds);
        Assert.Equal(5, options.ShutdownFlushTimeoutSeconds);
        Assert.Equal("PathVeer.Service", options.ServiceName);
        Assert.False(options.Otlp.Enabled);
        Assert.Equal(string.Empty, options.Otlp.Endpoint);
        Assert.Equal("grpc", options.Otlp.Protocol);
        Assert.Equal(string.Empty, options.Otlp.HeadersEnvironmentVariable);
        Assert.False(options.Console.Enabled);
    }

    [Fact]
    public void FromConfiguration_BindsSection()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:Otlp:Enabled"] = "true",
                ["Observability:Otlp:Endpoint"] = "http://localhost:4317",
                ["Observability:SamplingRatio"] = "0.25",
            })
            .Build();

        ObservabilityOptions options =
            ObservabilityOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.True(options.Otlp.Enabled);
        Assert.Equal("http://localhost:4317", options.Otlp.Endpoint);
        Assert.Equal(0.25, options.SamplingRatio);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public void Validate_RejectsSamplingRatioOutOfRange(string ratio)
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            SamplingRatio = double.Parse(ratio),
        };

        Assert.Throws<ArgumentException>(() =>
            ObservabilityOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.5")]
    public void Validate_AcceptsSamplingRatioInRange(string ratio)
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            SamplingRatio = double.Parse(ratio),
        };

        ObservabilityOptionsValidator.Validate(options);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("-1")]
    public void Validate_RejectsInvalidExportTimeout(string timeout)
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            ExportTimeoutSeconds = int.Parse(timeout),
        };

        Assert.Throws<ArgumentException>(() =>
            ObservabilityOptionsValidator.Validate(options));
    }

    [Fact]
    public void Validate_RejectsInvalidOtlpEndpoint()
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            Otlp = new OtlpExporterOptions
            {
                Enabled = true,
                Endpoint = "not-a-uri",
            },
        };

        Assert.Throws<ArgumentException>(() =>
            ObservabilityOptionsValidator.Validate(options));
    }

    [Fact]
    public void Validate_RejectsOtlpEndpointWhenEnabledButBlank()
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            Otlp = new OtlpExporterOptions { Enabled = true, Endpoint = "" },
        };

        Assert.Throws<ArgumentException>(() =>
            ObservabilityOptionsValidator.Validate(options));
    }

    [Fact]
    public void Validate_RejectsConsoleExporterOutsideDevelopment()
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            Console = new ConsoleExporterOptions { Enabled = true },
        };

        Assert.Throws<ArgumentException>(() =>
            ObservabilityOptionsValidator.Validate(options, isDevelopment: false));
    }

    [Fact]
    public void Validate_AllowsConsoleExporterInDevelopment()
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            Console = new ConsoleExporterOptions { Enabled = true },
        };

        ObservabilityOptionsValidator.Validate(options, isDevelopment: true);
    }

    [Fact]
    public void Validate_MissingHeaderEnvironmentVariable_FailsWhenRequired()
    {
        ObservabilityOptions options = new()
        {
            Enabled = true,
            Otlp = new OtlpExporterOptions
            {
                Enabled = true,
                Endpoint = "http://localhost:4317",
                HeadersEnvironmentVariable = "IRANDIRECT_OTLP_HEADERS",
            },
        };

        // The named variable is not set in the environment.
        Assert.Throws<ArgumentException>(() =>
            ObservabilityOptionsValidator.Validate(options));
    }

    [Fact]
    public void Validate_SecretValueNotInExceptionMessage()
    {
        const string secret = "Authorization=Bearer super-secret-token";
        Environment.SetEnvironmentVariable(
            "IRANDIRECT_TEST_HEADER_VAR", secret);
        try
        {
            // Force a failure on a different field to capture the message;
            // the value must never leak even if it were referenced.
            ObservabilityOptions options = new()
            {
                Enabled = true,
                SamplingRatio = 2.0, // out of range
                Otlp = new OtlpExporterOptions
                {
                    Enabled = true,
                    Endpoint = "http://localhost:4317",
                    HeadersEnvironmentVariable = "IRANDIRECT_TEST_HEADER_VAR",
                },
            };

            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                ObservabilityOptionsValidator.Validate(options));

            Assert.DoesNotContain(secret, ex.Message);
            Assert.DoesNotContain("super-secret-token", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRANDIRECT_TEST_HEADER_VAR", null);
        }
    }
}
