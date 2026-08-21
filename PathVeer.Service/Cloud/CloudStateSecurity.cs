using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PathVeer.Service.Cloud;

/// <summary>
/// Hardens the on-disk boundary around the PathVeer Cloud device credential.
///
/// The Cloud credential is the single most sensitive secret PathVeer holds.
/// Two independent controls protect it:
///
///   1. Cryptographic scope — the Windows DPAPI protector uses
///      <see cref="DataProtectionScope.CurrentUser"/>. Because the PathVeer
///      Service runs as LocalSystem, "current user" is the LocalSystem
///      profile; ordinary local users therefore CANNOT decrypt the blob even
///      if they obtain its bytes.
///
///   2. Filesystem ACL — this helper ensures the dedicated Cloud state
///      directory (and everything created inside it by the atomic JSON store)
///      is NOT readable by ordinary local users. Inheritance is disabled and
///      BUILTIN\Users is removed; only SYSTEM and Administrators retain access.
///
/// Defense in depth: even if one control is somehow bypassed, the other still
/// prevents an ordinary user from obtaining/decrypting the credential merely
/// by reading PathVeer state.
///
/// The helper is idempotent and best-effort: it never throws for benign
/// conditions (e.g. directory already correctly secured) and is a no-op on
/// non-Windows platforms.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CloudStateSecurity
{
    private static readonly SecurityIdentifier s_system =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier s_administrators =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier s_users =
        new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>
    /// Creates (if missing) and hardens the Cloud state directory so that only
    /// SYSTEM and Administrators can read/write it. Ordinary users (including
    /// BUILTIN\Users) are denied access. Safe to call repeatedly.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void EnsureSecured(string cloudDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudDirectory);

        DirectoryInfo dir = Directory.CreateDirectory(cloudDirectory);
        var info = new DirectoryInfo(dir.FullName);

        var security = info.GetAccessControl();

        // Do not inherit the parent (%ProgramData%\PathVeer) ACL, which grants
        // BUILTIN\Users read access.
        security.SetAccessRuleProtection(
            isProtected: true,
            preserveInheritance: false);

        // Owner-level identities that must always retain full control.
        AddFullControl(security, s_system);
        AddFullControl(security, s_administrators);

        // Explicitly strip any ordinary-user read access that might have
        // leaked in from a prior (inherited) state.
        RemoveSid(security, s_users);

        info.SetAccessControl(security);

        // Re-apply to any files already present (e.g. a legacy blob being
        // migrated) so a pre-existing file with a permissive ACE is tightened.
        foreach (string file in Directory.EnumerateFiles(info.FullName))
        {
            HardenFile(file);
        }
    }

    /// <summary>
    /// Hardens a single Cloud-state file: SYSTEM + Administrators full control,
    /// BUILTIN\Users removed, inheritance disabled. Used for both freshly
    /// written files and migrated legacy files.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void HardenFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var info = new FileInfo(filePath);
        var security = info.GetAccessControl();

        security.SetAccessRuleProtection(
            isProtected: true,
            preserveInheritance: false);

        AddFullControl(security, s_system);
        AddFullControl(security, s_administrators);
        RemoveSid(security, s_users);

        info.SetAccessControl(security);
    }

    /// <summary>
    /// True when the directory carries the hardened boundary (SYSTEM +
    /// Administrators full control, no BUILTIN\Users access, inheritance
    /// disabled). Used by tests and by startup self-checks.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsSecured(string cloudDirectory)
    {
        if (!Directory.Exists(cloudDirectory))
        {
            return false;
        }

        var info = new DirectoryInfo(cloudDirectory);
        var security = info.GetAccessControl();
        if (!security.AreAccessRulesProtected)
        {
            // Inheritance still enabled -> inherits parent Users-read ACL.
            return false;
        }

        bool usersHasAccess = false;
        bool systemFull = false;
        bool adminFull = false;

        foreach (FileSystemAccessRule rule in
                 security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: false,
                     typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid == s_users)
            {
                if ((rule.FileSystemRights & FileSystemRights.Read) != 0)
                {
                    usersHasAccess = true;
                }
            }
            else if (sid == s_system)
            {
                if (rule.FileSystemRights.HasFlag(
                        FileSystemRights.FullControl))
                {
                    systemFull = true;
                }
            }
            else if (sid == s_administrators)
            {
                if (rule.FileSystemRights.HasFlag(
                        FileSystemRights.FullControl))
                {
                    adminFull = true;
                }
            }
        }

        return systemFull && adminFull && !usersHasAccess;
    }

    private static void AddFullControl(
        ObjectSecurity security,
        SecurityIdentifier sid)
    {
        if (security is DirectorySecurity ds)
        {
            ds.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        else if (security is FileSecurity fs)
        {
            fs.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        }
    }

    private static void RemoveSid(
        ObjectSecurity security,
        SecurityIdentifier sid)
    {
        if (security is DirectorySecurity ds)
        {
            ds.RemoveAccessRuleAll(new FileSystemAccessRule(
                sid,
                FileSystemRights.Read,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            ds.RemoveAccessRuleAll(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        else if (security is FileSecurity fs)
        {
            fs.RemoveAccessRuleAll(new FileSystemAccessRule(
                sid,
                FileSystemRights.Read,
                AccessControlType.Allow));
            fs.RemoveAccessRuleAll(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        }
    }
}
