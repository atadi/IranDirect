using System.Security.Cryptography;
using System.Text.Json;
using PathVeer.Core.Cloud;

namespace PathVeer.Service.Cloud;

/// <summary>
/// Upgrades an existing D2 Cloud registration to the hardened storage model.
///
/// The original D2 build stored the credential as a <c>LocalMachine</c>-scoped
/// DPAPI blob directly inside <c>%ProgramData%\PathVeer\cloud-registration.json</c>,
/// a directory whose ACL inherited <c>BUILTIN\Users</c> read access — allowing
/// any local user who could read the file to decrypt the device credential.
///
/// The hardened model (see <see cref="CloudStateSecurity"/> and
/// <see cref="WindowsDpapiCloudSecretProtector"/>) moves the state into a
/// dedicated, ACL-hardened <c>cloud\</c> subdirectory and protects the blob with
/// <c>CurrentUser</c> DPAPI (the LocalSystem service profile), which ordinary
/// users cannot decrypt.
///
/// This migrator performs the upgrade idempotently:
/// <list type="bullet">
///   <item>New location already enrolled -> already migrated, no-op.</item>
///   <item>Legacy location absent -> nothing to migrate.</item>
///   <item>Legacy location present -> decrypt with the legacy decoder, re-protect
///         with the current protector, write to the new secured location, then
///         delete the legacy file.</item>
///   <item>Revoked legacy record -> migrated without a credential blob.</item>
/// </list>
/// Failures are contained: a migration error does NOT delete the legacy file and
/// does NOT lose the enrollment; the Service simply retries on next start.
/// </summary>
public sealed class CloudRegistrationMigrator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _legacyPath;
    private readonly string _newPath;
    private readonly ICloudSecretProtector _protector;
    private readonly LegacyCloudCredentialDecoder _legacyDecoder;

    public CloudRegistrationMigrator(
        string legacyPath,
        string newPath,
        ICloudSecretProtector protector,
        LegacyCloudCredentialDecoder legacyDecoder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(legacyDecoder);

        _legacyPath = legacyPath;
        _newPath = newPath;
        _protector = protector;
        _legacyDecoder = legacyDecoder;
    }

    /// <summary>
    /// Migrates the legacy registration if needed. Returns true when a legacy
    /// record was migrated during this call.
    /// </summary>
    public async Task<bool> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        // Already on the new model (or never enrolled) -> nothing to do.
        if (File.Exists(_newPath) && await NewLocationIsEnrolledAsync(cancellationToken))
        {
            return false;
        }

        if (!File.Exists(_legacyPath))
        {
            return false;
        }

        CloudRegistrationRecord legacy;
        try
        {
            string json = await File.ReadAllTextAsync(
                _legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<CloudRegistrationRecord>(
                         json, s_jsonOptions)
                     ?? new CloudRegistrationRecord();
        }
        catch (JsonException)
        {
            // Corrupt legacy file: do not delete (operator may recover), do not
            // throw — enrollment is preserved absent a usable record.
            return false;
        }

        // Re-protect the credential with the new (CurrentUser) protector.
        if (!string.IsNullOrEmpty(legacy.CredentialProtectedBase64)
            && !legacy.CredentialRevoked)
        {
            string rawCredential;
            try
            {
                rawCredential = _legacyDecoder.Decode(
                    legacy.CredentialProtectedBase64);
            }
            catch (CryptographicException)
            {
                // Legacy blob unreadable (e.g. not actually LocalMachine). Leave
                // the legacy file in place; do not lose enrollment.
                return false;
            }

            legacy.CredentialProtectedBase64 = _protector.Protect(rawCredential);
        }
        else
        {
            // Revoked or credential-less: keep no protected blob.
            legacy.CredentialProtectedBase64 = null;
        }

        // Persist into the new, hardened location. The directory ACL is applied
        // separately by CloudStateSecurity.EnsureSecured before this runs, but
        // create it defensively here in case migration runs before hardening.
        string? newDir = Path.GetDirectoryName(_newPath);
        if (!string.IsNullOrEmpty(newDir))
        {
            Directory.CreateDirectory(newDir);
        }

        await File.WriteAllTextAsync(
            _newPath,
            JsonSerializer.Serialize(legacy, s_jsonOptions),
            cancellationToken);

        // Harden the migrated file explicitly: it is written directly (not via
        // the store's post-write hook), so apply the dedicated-directory ACL
        // here to keep the ordinary-user read boundary intact post-migration.
        try
        {
            CloudStateSecurity.HardenFile(_newPath);
        }
        catch (UnauthorizedAccessException)
        {
            // Hardening is defense-in-depth; the CurrentUser DPAPI scope remains
            // the primary boundary. Do not lose the enrollment over an ACL set.
        }

        // Only now is it safe to remove the legacy file.
        File.Delete(_legacyPath);
        return true;
    }

    private async Task<bool> NewLocationIsEnrolledAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(
                _newPath, cancellationToken);
            var record = JsonSerializer.Deserialize<CloudRegistrationRecord>(
                json, s_jsonOptions);
            return record is { IsEnrolled: true };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
