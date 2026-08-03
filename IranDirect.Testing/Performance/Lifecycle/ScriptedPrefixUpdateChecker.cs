using IranDirect.Core.Prefixes;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// A scripted check outcome for <see cref="ScriptedPrefixUpdateChecker"/>.
/// </summary>
public sealed record ScriptedPrefixCheck(
    PrefixUpdateCheckStatus Status,
    PrefixSourceMetadata? CurrentMetadata = null,
    PrefixUpdateCheckRemoteMetadata? RemoteMetadata = null,
    string? Reason = null,
    string? ExceptionMessage = null);

/// <summary>
/// Deterministic <see cref="IPrefixUpdateChecker"/> that replays a
/// scripted sequence of check outcomes, repeating the last outcome
/// once the queue is exhausted. Checked timestamps come from an
/// injected (simulated) time provider.
/// </summary>
public sealed class ScriptedPrefixUpdateChecker : IPrefixUpdateChecker
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly List<ScriptedPrefixCheck> _script = [];
    private ScriptedPrefixCheck? _last;
    private int _checkCount;

    public ScriptedPrefixUpdateChecker(
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(
            nameof(timeProvider));
    }

    public int CheckCount
    {
        get
        {
            lock (_gate)
            {
                return _checkCount;
            }
        }
    }

    public void Enqueue(ScriptedPrefixCheck check) =>
        _script.Add(check);

    public void EnqueueCurrent(
        string? reason = null) =>
        Enqueue(new ScriptedPrefixCheck(
            PrefixUpdateCheckStatus.Current,
            Reason: reason));

    public void EnqueueUpdateAvailable(
        PrefixUpdateCheckRemoteMetadata? remote = null,
        string? reason = null) =>
        Enqueue(new ScriptedPrefixCheck(
            PrefixUpdateCheckStatus.UpdateAvailable,
            RemoteMetadata: remote,
            Reason: reason));

    public void EnqueueUnknown(string? reason = null) =>
        Enqueue(new ScriptedPrefixCheck(
            PrefixUpdateCheckStatus.Unknown,
            Reason: reason));

    public void EnqueueFailure(string reason) =>
        Enqueue(new ScriptedPrefixCheck(
            PrefixUpdateCheckStatus.Failed,
            Reason: reason));

    public void EnqueueException(string message) =>
        Enqueue(new ScriptedPrefixCheck(
            PrefixUpdateCheckStatus.Unknown,
            ExceptionMessage: message));

    public Task<PrefixUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ScriptedPrefixCheck check;

        lock (_gate)
        {
            _checkCount++;

            if (_script.Count > 0)
            {
                check = _script[0];
                _script.RemoveAt(0);
                _last = check;
            }
            else
            {
                check = _last ?? new ScriptedPrefixCheck(
                    PrefixUpdateCheckStatus.Unknown,
                    Reason: "No scripted outcome; defaulting to Unknown.");
            }
        }

        if (check.ExceptionMessage is not null)
        {
            throw new InvalidOperationException(
                check.ExceptionMessage);
        }

        return Task.FromResult(
            new PrefixUpdateCheckResult
            {
                Status = check.Status,
                CurrentMetadata = check.CurrentMetadata,
                RemoteMetadata = check.RemoteMetadata,
                CheckedAt = _timeProvider.GetUtcNow(),
                Reason = check.Reason
            });
    }
}
