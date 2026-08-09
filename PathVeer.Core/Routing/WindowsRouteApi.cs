using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PathVeer.Core.SystemTools;

namespace PathVeer.Core.Routing;

public sealed class WindowsRouteApi : IWindowsRouteApi
{
    private readonly CommandRunner _commandRunner;

    public WindowsRouteApi(CommandRunner commandRunner)
    {
        ArgumentNullException.ThrowIfNull(commandRunner);

        _commandRunner = commandRunner;
    }

    public async Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
        CancellationToken cancellationToken = default)
    {
        const string script =
            "$ErrorActionPreference='Stop';" +
            "$routes=Get-NetRoute -AddressFamily IPv4 | " +
            "Select-Object DestinationPrefix,NextHop," +
            "InterfaceIndex,RouteMetric;" +
            "$routes | ConvertTo-Json -Compress";

        string encodedCommand =
            Convert.ToBase64String(
                Encoding.Unicode.GetBytes(script));

        CommandResult result = await _commandRunner.RunAsync(
            "powershell.exe",
            $"-NoLogo -NoProfile -NonInteractive " +
            $"-ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not enumerate Windows routes. " +
                $"{result.StandardError}");
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<SystemRoute>();
        }

        return ParseRoutes(result.StandardOutput);
    }

    public async Task AddAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            return;
        }

        string scriptPath = CreateNetshScript(
            routes,
            operation: "add");

        try
        {
            await RunNetshScriptAsync(
                scriptPath,
                cancellationToken);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    public async Task DeleteAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            return;
        }

        string scriptPath = CreateNetshScript(
            routes,
            operation: "delete");

        try
        {
            await RunNetshScriptAsync(
                scriptPath,
                cancellationToken);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    private async Task RunNetshScriptAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _commandRunner.RunAsync(
            "netsh.exe",
            $"-f \"{scriptPath}\"",
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Windows route batch failed. " +
                $"Exit code: {result.ExitCode}. " +
                $"Output: {result.StandardOutput} " +
                $"Error: {result.StandardError}");
        }
    }

    private static string CreateNetshScript(
        IReadOnlyCollection<ManagedRoute> routes,
        string operation)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PathVeer");

        Directory.CreateDirectory(directory);

        string path = Path.Combine(
            directory,
            $"routes-{Guid.NewGuid():N}.txt");

        List<string> lines =
        [
            "interface ipv4"
        ];

        foreach (ManagedRoute route in routes)
        {
            if (operation == "add")
            {
                lines.Add(
                    $"add route " +
                    $"prefix={route.DestinationPrefix} " +
                    $"interface={route.InterfaceIndex} " +
                    $"nexthop={route.Gateway} " +
                    $"metric={route.Metric} " +
                    $"store=active");
            }
            else
            {
                lines.Add(
                    $"delete route " +
                    $"prefix={route.DestinationPrefix} " +
                    $"interface={route.InterfaceIndex} " +
                    $"nexthop={route.Gateway} " +
                    $"store=active");
            }
        }

        File.WriteAllLines(path, lines, Encoding.ASCII);

        return path;
    }

    private static IReadOnlyList<SystemRoute> ParseRoutes(
        string json)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            List<RouteJson>? many =
                JsonSerializer.Deserialize<List<RouteJson>>(
                    json,
                    options);

            if (many is not null)
            {
                return many
                    .Select(ConvertRoute)
                    .Where(route => route is not null)
                    .Cast<SystemRoute>()
                    .ToArray();
            }
        }
        catch (JsonException)
        {
            // PowerShell returns a single object instead of
            // an array when only one record exists.
        }

        RouteJson? single =
            JsonSerializer.Deserialize<RouteJson>(
                json,
                options);

        SystemRoute? converted =
            single is null ? null : ConvertRoute(single);

        return converted is null
            ? Array.Empty<SystemRoute>()
            : [converted];
    }

    private static SystemRoute? ConvertRoute(
        RouteJson route)
    {
        if (string.IsNullOrWhiteSpace(
                route.DestinationPrefix))
        {
            return null;
        }

        if (!IPAddress.TryParse(
                route.NextHop,
                out IPAddress? nextHop))
        {
            nextHop = IPAddress.Any;
        }

        return new SystemRoute
        {
            DestinationPrefix = route.DestinationPrefix,
            NextHop = nextHop,
            InterfaceIndex = route.InterfaceIndex,
            RouteMetric = route.RouteMetric
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary-file cleanup failure is non-fatal.
        }
    }

    private sealed class RouteJson
    {
        [JsonPropertyName("DestinationPrefix")]
        public string DestinationPrefix { get; set; } = "";

        [JsonPropertyName("NextHop")]
        public string NextHop { get; set; } = "";

        [JsonPropertyName("InterfaceIndex")]
        public uint InterfaceIndex { get; set; }

        [JsonPropertyName("RouteMetric")]
        public int RouteMetric { get; set; }
    }
}
