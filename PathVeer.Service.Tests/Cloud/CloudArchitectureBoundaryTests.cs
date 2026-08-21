using System.Reflection;
using PathVeer.Core.Cloud;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Architecture boundary test (D2 §13): the Cloud integration code must NOT
/// depend on routing mutation / reconciliation internals. The Service remains
/// the sole machine authority; Cloud code only: sends enrollment + heartbeat
/// over HTTP, encrypts + persists the device credential, and exposes status.
///
/// This test statically verifies the dependency direction by asserting that no
/// type in the Cloud integration namespaces references any routing / route
/// mutation / desired-configuration type.
/// </summary>
public sealed class CloudArchitectureBoundaryTests
{
    // Forbidden routing / reconcile / network-mutation types. If a Cloud type
    // ever references one of these, the Service authority boundary is broken.
    private static readonly HashSet<string> ForbiddenTypeNames = new(
        StringComparer.Ordinal)
    {
        "PathVeerController",
        "RuntimeReconciler",
        "DesiredConfigurationService",
        "RouteMutationEngine",
        "WireGuardService",
        "WireGuardTunnel",
        "RouteApplier",
        "IDesiredConfigurationSink",
        "IReconciliationSink",
        "NetworkAdapterManager",
        "IRouteMutator",
        "DesiredConfiguration"
    };

    private static readonly string[] CloudNamespaces =
    {
        "PathVeer.Core.Cloud",
        "PathVeer.Service.Cloud"
    };

    [Fact]
    public void CloudTypes_DoNotReferenceRoutingMutationInternals()
    {
        List<Type> cloudTypes = CollectCloudTypes().ToList();

        Assert.NotEmpty(cloudTypes);

        List<string> violations = new();

        foreach (Type type in cloudTypes)
        {
            foreach (Type referenced in ReferencedTypes(type))
            {
                if (ForbiddenTypeNames.Contains(referenced.Name) ||
                    ForbiddenTypeNames.Contains(referenced.FullName ?? ""))
                {
                    violations.Add(
                        $"{type.FullName} -> {referenced.FullName}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void CloudNamespaces_AreIsolatedFromRouting()
    {
        foreach (Type type in CollectCloudTypes())
        {
            Assert.False(
                type.Namespace?.Contains("Routing") == true ||
                type.Namespace?.Contains("Reconcile") == true,
                $"Cloud type {type.FullName} is under a routing namespace.");
        }
    }

    private static IEnumerable<Type> CollectCloudTypes()
    {
        foreach (Assembly assembly in new[]
                 {
                     typeof(PathVeerCloudClient).Assembly,
                     typeof(CloudHeartbeatService).Assembly
                 })
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.Namespace is null)
                {
                    continue;
                }

                if (CloudNamespaces.Any(ns =>
                        type.Namespace == ns ||
                        type.Namespace.StartsWith(ns + ".",
                            StringComparison.Ordinal)))
                {
                    yield return type;
                }
            }
        }
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (Type i in type.GetInterfaces())
        {
            yield return i;
        }

        foreach (FieldInfo f in type.GetFields(
                     BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.Instance |
                     BindingFlags.Static))
        {
            yield return f.FieldType;
        }

        foreach (PropertyInfo p in type.GetProperties(
                     BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.Instance |
                     BindingFlags.Static))
        {
            yield return p.PropertyType;
        }

        foreach (MethodBase m in type.GetMethods(
                     BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.Instance |
                     BindingFlags.Static |
                     BindingFlags.DeclaredOnly)
                 .Concat<MethodBase>(
                     type.GetConstructors(
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Instance |
                         BindingFlags.Static)))
        {
            if (m is MethodInfo mi)
            {
                yield return mi.ReturnType;
            }

            foreach (ParameterInfo p in m.GetParameters())
            {
                yield return p.ParameterType;
            }
        }

        foreach (Type t in CollectGenericArgs(type))
        {
            yield return t;
        }
    }

    private static IEnumerable<Type> CollectGenericArgs(Type type)
    {
        if (type.IsGenericType)
        {
            foreach (Type a in type.GetGenericArguments())
            {
                yield return a;
                foreach (Type g in CollectGenericArgs(a))
                {
                    yield return g;
                }
            }
        }
    }
}
