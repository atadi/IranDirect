using IranDirect.Service.Observability;
using OpenTelemetry.Resources;
using Xunit;

namespace IranDirect.Service.Tests.Observability;

public sealed class ObservabilityResourceBuilderTests
{
    private static readonly HashSet<string> ForbiddenAttributes = new()
    {
        "machine.name", "host.name", "host.id", "host.arch",
        "service.instance.id", "process.command_line", "process.executable.path",
        "process.pid", "telemetry.distro.name", "cloud.account.id",
        "cloud.region", "cloud.availability_zone",
    };

    [Fact]
    public void Create_SetsServiceNameVersionAndEnvironment()
    {
        Resource resource = ObservabilityResourceBuilder.Create(
            "IranDirect.Service",
            "1.2.3.4",
            "Production");

        Assert.Equal(
            "IranDirect.Service",
            resource.Attributes
                .First(a => a.Key == "service.name").Value as string);
        Assert.Equal(
            "1.2.3.4",
            resource.Attributes
                .First(a => a.Key == "service.version").Value as string);
        Assert.Equal(
            "Production",
            resource.Attributes
                .First(a => a.Key == "deployment.environment.name")
                    .Value as string);
    }

    [Fact]
    public void Create_DoesNotGenerateInstanceId()
    {
        Resource resource = ObservabilityResourceBuilder.Create(
            "IranDirect.Service", "1.0.0", "Production");

        Assert.DoesNotContain(
            resource.Attributes,
            a => a.Key == "service.instance.id");
    }

    [Fact]
    public void Create_NoForbiddenSensitiveAttributes()
    {
        Resource resource = ObservabilityResourceBuilder.Create(
            "IranDirect.Service", "1.0.0", "Production");

        foreach (var attr in resource.Attributes)
        {
            Assert.DoesNotContain(
                ForbiddenAttributes,
                forbidden => attr.Key == forbidden);
        }
    }

    [Fact]
    public void Create_BlankInputs_FallBackToSafeValues()
    {
        Resource resource = ObservabilityResourceBuilder.Create(
            string.Empty, string.Empty, string.Empty);

        Assert.Equal(
            "IranDirect.Service",
            resource.Attributes
                .First(a => a.Key == "service.name").Value as string);
        Assert.Equal(
            "unknown",
            resource.Attributes
                .First(a => a.Key == "deployment.environment.name")
                    .Value as string);
    }
}
