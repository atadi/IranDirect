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

    // Filesystem-security operations. Defaults to the real production
    // canonicalization so the migrator's actual deployed behavior is unchanged.
    // A caller (e.g. a unit test without SeTakeOwnershipPrivilege) may inject
    // substitutes so migration DECISION logic is exercised without requiring
    // LocalSystem authority to persist SYSTEM ownership. The injected delegates
    // are NOT the security guarantee — they only stand in for it under test.
    private readonly Action<string> _secureLegacy;
    private readonly Action<string> _hardenDirectory;
    private readonly Action<string> _hardenFile;

    public CloudRegistrationMigrator(
        string legacyPath,
        string newPath,
        ICloudSecretProtector protector,
        LegacyCloudCredentialDecoder legacyDecoder)
        : this(legacyPath, newPath, protector, legacyDecoder,
               CloudStateSecurity.SecureLegacyFileIfPresent,
               CloudStateSecurity.HardenDirectory,
               CloudStateSecurity.HardenFile)
    {
    }

    public CloudRegistrationMigrator(
        string legacyPath,
        string newPath,
        ICloudSecretProtector protector,
        LegacyCloudCredentialDecoder legacyDecoder,
        Action<string> secureLegacy,
        Action<string> hardenDirectory,
        Action<string> hardenFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(legacyDecoder);
        ArgumentNullException.ThrowIfNull(secureLegacy);
        ArgumentNullException.ThrowIfNull(hardenDirectory);
        ArgumentNullException.ThrowIfNull(hardenFile);

        _legacyPath = legacyPath;
        _newPath = newPath;
        _protector = protector;
        _legacyDecoder = legacyDecoder;
        _secureLegacy = secureLegacy;
        _hardenDirectory = hardenDirectory;
        _hardenFile = hardenFile;
    }

    /// <summary>
    /// Migrates the legacy registration if needed. Returns true when a legacy
    /// record was migrated during this call.
    /// </summary>
    public async Task<bool> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        // Reduce exposure of the original vulnerable LocalMachine blob: deny
        // ordinary users read access to the legacy bytes as early as safely
        // possible, before any other work. The migration still must complete
        // (the blob remains decryptable by LocalSystem) for the credential to
        // survive, so this is best-effort hardening, not a state change.
        _secureLegacy(_legacyPath);

        // Decide whether the current (new-location) state is authoritative.
        // Authority is NOT given by attacker-controlled JSON flags
        // (IsEnrolled / State / DeviceId / OrganizationId / mere presence of a
        // blob). A non-revoked current registration is authoritative ONLY if
        // the Service can actually decrypt and use its CurrentUser credential.
        // Otherwise an attacker-planted "Connected" document must not suppress
        // a recoverable legacy registration.
        bool currentAuthoritative =
            await NewLocationIsAuthoritativeAsync(cancellationToken);

        if (currentAuthoritative)
        {
            // Genuine, usable CurrentUser registration already present: safe
            // to no-op. (A 'Revoked' current record is non-authoritative here,
            // so it never suppresses a recoverable legacy below.)
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
        // separately by CloudStateSecurity.HardenDirectory before this runs
        // (onDirectoryPrepared callback), but create + harden it defensively
        // here in case migration runs before that.
        string? newDir = Path.GetDirectoryName(_newPath);
        if (!string.IsNullOrEmpty(newDir))
        {
            _hardenDirectory(newDir);
        }

        await File.WriteAllTextAsync(
            _newPath,
            JsonSerializer.Serialize(legacy, s_jsonOptions),
            cancellationToken);

        // Harden the migrated file explicitly: it is written directly (not via
        // the store's post-write hook), so apply the canonical ACL here to keep
        // the ordinary-user read boundary intact post-migration.
        try
        {
            _hardenFile(_newPath);
        }
        catch (UnauthorizedAccessException)
        {
            // Hardening is defense-in-depth; the CurrentUser DPAPI scope remains
            // the primary boundary. Do not lose the enrollment over an ACL set.
        }

        // Verify the new state is actually usable (decryptable by the current
        // protector) before declaring success. If it is not, remove the new
        // file so we do NOT leave a fabricated/non-functional registration and
        // do NOT delete the legacy file — next start retries safely.
        if (!string.IsNullOrEmpty(legacy.CredentialProtectedBase64)
            && !legacy.CredentialRevoked)
        {
            try
            {
                _ = _protector.Unprotect(legacy.CredentialProtectedBase64);
            }
            catch (CryptographicException)
            {
                File.Delete(_newPath);
                return false;
            }
            catch (FormatException)
            {
                File.Delete(_newPath);
                return false;
            }
        }

        // Only now is it safe to remove the legacy file.
        File.Delete(_legacyPath);
        return true;
    }

    private async Task<bool> NewLocationIsAuthoritativeAsync(
        CancellationToken cancellationToken)
    {
        string? json;
        try
        {
            json = await File.ReadAllTextAsync(
                _newPath, cancellationToken);
        }
        catch (IOException)
        {
            // Unreadable (missing, locked) -> not authoritative.
            return false;
        }

        CloudRegistrationRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<CloudRegistrationRecord>(
                json, s_jsonOptions);
        }
        catch (JsonException)
        {
            // Malformed current file -> not authoritative; a recoverable
            // legacy registration (if any) must not be suppressed by it.
            return false;
        }

        if (record is null)
        {
            return false;
        }

        // Revoked current state is intentionally credential-less and therefore
        // non-authoritative; it must NOT suppress a recoverable legacy.
        if (record.CredentialRevoked)
        {
            return false;
        }

        // The single authoritative test: can the Service actually decrypt and
        // use the stored CurrentUser credential? This reuses the exact same
        // definition used by CloudRegistrationStore, so JSON flags alone never
        // grant authority.
        return CloudRegistrationStore.IsUsableRegistration(
            record, _protector);
    }
}
