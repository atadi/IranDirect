using System.Diagnostics;
using OpenTelemetry.Resources;

namespace PathVeer.Service.Observability;

/// <summary>
/// Builds the bounded OpenTelemetry resource (identity metadata) for the
/// PathVeer Service. Only non-sensitive, bounded attributes are attached:
/// <c>service.name</c>, <c>service.version</c>, and
/// <c>deployment.environment.name</c>. No per-process instance identifier is
/// generated (the privacy policy requires explicit approval for such
/// identifiers, and none was granted). Host, user, network, installation-path,
/// and organization identifiers are never attached.
/// </summary>
public static class ObservabilityResourceBuilder
{
    /// <summary>
    /// Creates the resource from bounded inputs. <paramref name="serviceName"/>
    /// and <paramref name="serviceVersion"/> are non-empty (serviceName
    /// defaults to <c>PathVeer.Service</c>; serviceVersion is the Core
    /// assembly version shared with <c>PathVeerTelemetry.Version</c>).
    /// </summary>
    public static Resource Create(
        string serviceName,
        string serviceVersion,
        string environmentName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            serviceName = "PathVeer.Service";
        }

        if (string.IsNullOrWhiteSpace(serviceVersion))
        {
            serviceVersion = "1.0.0";
        }

        if (string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = "unknown";
        }

        return ResourceBuilder
            .CreateEmpty()
            // autoGenerateServiceInstanceId: false -> no per-instance id is
            // emitted as a resource attribute (privacy policy: no machine or
            // instance identifiers in telemetry).
            .AddService(
                serviceName,
                serviceVersion,
                autoGenerateServiceInstanceId: false)
            // AddService maps its version argument to service.namespace, not
            // service.version; set service.version explicitly so the
            // shipping Core assembly version is reported.
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment.name", environmentName),
                new("service.version", serviceVersion),
            })
            .Build();
    }
}
