using System;
using Microsoft.Extensions.Logging;
using PathVeer.Core.Persistence;

namespace PathVeer.Service.Persistence;

/// <summary>
/// Bridges <see cref="JsonStoreRecoveryOptions.OnRecovery"/> (raised by the
/// persistence layer) into the Service's structured <see cref="ILogger"/>.
///
/// Only non-secret recovery detail is logged: the store path, the result
/// (succeeded/failed), an optional failure reason, the collision-safe
/// evidence file path (never its contents), and whether a usable ".bak"
/// survived. No document contents, Cloud credentials, or enrollment secrets
/// are ever observed here.
/// </summary>
internal sealed class JsonStoreRecoveryDiagnostics
{
    private const string EventIdRecoverySucceeded = "PersistenceRecoverySucceeded";
    private const string EventIdRecoveryFailed = "PersistenceRecoveryFailed";

    private readonly ILogger _logger;

    public JsonStoreRecoveryDiagnostics(ILogger logger)
    {
        _logger = logger;
    }

    public void OnRecovery(JsonStoreRecoveryEventArgs e)
    {
        if (e.Succeeded)
        {
            _logger.LogInformation(
                "{EventId}: store recovery succeeded. StorePath={StorePath} " +
                "BackupPreserved={BackupPreserved} EvidencePath={EvidencePath}",
                EventIdRecoverySucceeded,
                e.StorePath,
                e.BackupPreserved,
                e.EvidencePath ?? "<none>");
            return;
        }

        _logger.LogError(
            "{EventId}: store recovery failed. StorePath={StorePath} " +
            "BackupPreserved={BackupPreserved} EvidencePath={EvidencePath} " +
            "Reason={FailureReason}",
            EventIdRecoveryFailed,
            e.StorePath,
            e.BackupPreserved,
            e.EvidencePath ?? "<none>",
            e.FailureReason ?? "<unknown>");
    }
}
