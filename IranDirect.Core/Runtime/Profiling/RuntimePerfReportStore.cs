namespace IranDirect.Core.Runtime.Profiling;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <summary>
/// Persists <see cref="RuntimeCyclePerfReport"/> instances as
/// timestamped JSON files and reads reports back, optionally
/// filtered by trigger.
///
/// Filenames embed the completion timestamp so they sort
/// lexicographically by completion time:
/// <c>enable-20260731-120706-123.json</c>. Reports written by older
/// builds using the <c>cycle-&lt;stamp&gt;-&lt;trigger&gt;.json</c>
/// convention are still read back.
/// </summary>
public sealed class RuntimePerfReportStore
{
    public const string FilePattern = "*.json";

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "IranDirect",
            "perf");

    private static readonly Regex s_reportNameRegex = new(
        @"^(?<trigger>[a-zA-Z]+)-(?<stamp>\d{8}-\d{6}-\d{3})" +
        @"(?:-(?<legacyTrigger>[a-zA-Z]+))?\.json$",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions s_jsonOptions =
        CreateJsonOptions();

    private readonly string _directory;

    public RuntimePerfReportStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string Directory => _directory;

    public string Write(RuntimeCyclePerfReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        System.IO.Directory.CreateDirectory(_directory);

        string fileName =
            $"{report.Trigger}-" +
            $"{report.CompletedAt:yyyyMMdd-HHmmss-fff}.json";

        string path = Path.Combine(_directory, fileName);

        string json = JsonSerializer.Serialize(
            report, s_jsonOptions);

        File.WriteAllText(path, json);

        return path;
    }

    public RuntimeCyclePerfReport? ReadLatest()
    {
        string? path = GetLatestReportPath();

        if (path is null)
            return null;

        return ReadFile(path);
    }

    public RuntimeCyclePerfReport? ReadLatest(string trigger)
    {
        string? path = GetLatestReportPath(trigger);

        if (path is null)
            return null;

        return ReadFile(path);
    }

    public async Task<RuntimeCyclePerfReport?> ReadLatestAsync(
        CancellationToken cancellationToken = default)
    {
        string? path = GetLatestReportPath();

        if (path is null)
            return null;

        return await ReadFileAsync(
            path, cancellationToken);
    }

    public async Task<RuntimeCyclePerfReport?> ReadLatestAsync(
        string trigger,
        CancellationToken cancellationToken = default)
    {
        string? path = GetLatestReportPath(trigger);

        if (path is null)
            return null;

        return await ReadFileAsync(
            path, cancellationToken);
    }

    public async Task<IReadOnlyList<RuntimeCyclePerfReport>> ListAsync(
        string? trigger = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        if (!System.IO.Directory.Exists(_directory))
            return [];

        List<RuntimeCyclePerfReport> reports = [];

        foreach (string path in EnumerateReportPaths(trigger)
                     .Take(limit))
        {
            RuntimeCyclePerfReport? report =
                await ReadFileAsync(path, cancellationToken);

            if (report is not null)
                reports.Add(report);
        }

        return reports;
    }

    public string? GetLatestReportPath(
        string? trigger = null)
    {
        if (!System.IO.Directory.Exists(_directory))
            return null;

        return EnumerateReportPaths(trigger)
            .FirstOrDefault();
    }

    private IEnumerable<string> EnumerateReportPaths(
        string? trigger = null)
    {
        if (!System.IO.Directory.Exists(_directory))
            return [];

        return System.IO.Directory
            .EnumerateFiles(_directory, FilePattern)
            .Select(path => new
            {
                Path = path,
                Name = Path.GetFileName(path)
            })
            .Where(entry =>
                TryParseReportName(entry.Name, out string? nameTrigger)
                && (trigger is null
                    || string.Equals(
                        nameTrigger, trigger,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new
            {
                entry.Path,
                Stamp = s_reportNameRegex.Match(entry.Name)
                    .Groups["stamp"].Value
            })
            .OrderByDescending(entry => entry.Stamp)
            .ThenByDescending(entry => entry.Path)
            .Select(entry => entry.Path);
    }

    private RuntimeCyclePerfReport? ReadFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<
                RuntimeCyclePerfReport>(
                json, s_jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<RuntimeCyclePerfReport?> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(
                path, cancellationToken);

            return JsonSerializer.Deserialize<
                RuntimeCyclePerfReport>(
                json, s_jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseReportName(
        string fileName,
        out string trigger)
    {
        Match match = s_reportNameRegex.Match(fileName);

        if (!match.Success)
        {
            trigger = string.Empty;
            return false;
        }

        trigger = match.Groups["legacyTrigger"].Success
            ? match.Groups["legacyTrigger"].Value
            : match.Groups["trigger"].Value;

        return true;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
