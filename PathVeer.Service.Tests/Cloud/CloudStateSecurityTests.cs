using System.Security.AccessControl;
using System.Security.Principal;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Exercises the CANONICAL on-disk security boundary for the Cloud credential.
///
/// The dedicated directory/file DACL must contain ONLY the approved
/// identities (NT AUTHORITY\SYSTEM + BUILTIN\Administrators, FullControl) and
/// NO other discretionary access ACEs. This is enforced by RESETTING the DACL
/// to the allow-list, so any attacker-inserted ACE (Everyone, Authenticated
/// Users, BUILTIN\Users, arbitrary user/group SID, CREATOR OWNER) is erased,
/// and IsSecured() returns FALSE whenever any unauthorized ACE survives.
/// </summary>
public sealed class CloudStateSecurityTests
{
    private static readonly SecurityIdentifier s_users =
        new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier s_everyone =
        new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier s_authenticatedUsers =
        new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier s_creatorOwner =
        new(WellKnownSidType.CreatorOwnerSid, null);
    private static readonly SecurityIdentifier s_system =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier s_administrators =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    // An ordinary, non-privileged local account SID used to simulate an
    // attacker-inserted explicit ACE.
    private static readonly SecurityIdentifier s_arbitraryUser =
        new(WellKnownSidType.BuiltinGuestsSid, null);

    [Fact]
    public void EnsureSecured_RemovesEveryoneAuthenticatedUsersUsersArbitrarySids()
    {
        // Simulate an attacker (or permissive default) who pre-created the
        // directory with explicit ALLOW ACEs for several non-approved
        // identities BEFORE the Service hardened it.
        string dir = NewTempDir();
        try
        {
            var info = new DirectoryInfo(dir);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                s_everyone, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                s_authenticatedUsers, FileSystemRights.Read,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                s_users, FileSystemRights.Read,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                s_arbitraryUser, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                s_creatorOwner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);

            CloudStateSecurity.EnsureSecured(dir);

            Assert.True(CloudStateSecurity.IsSecured(dir),
                "after hardening, no unauthorized ACE may survive");

            Assert.False(HasAllow(dir, s_everyone));
            Assert.False(HasAllow(dir, s_authenticatedUsers));
            Assert.False(HasAllow(dir, s_users));
            Assert.False(HasAllow(dir, s_arbitraryUser));
            Assert.False(HasAllow(dir, s_creatorOwner));
            Assert.True(HasAllowFull(dir, s_system));
            Assert.True(HasAllowFull(dir, s_administrators));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void EnsureSecured_IsIdempotent()
    {
        string dir = NewTempDir();
        try
        {
            CloudStateSecurity.EnsureSecured(dir);
            // Inject an attacker ACE between calls, then re-harden.
            var info = new DirectoryInfo(dir);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                s_everyone, FileSystemRights.Read,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);

            CloudStateSecurity.EnsureSecured(dir);

            Assert.True(CloudStateSecurity.IsSecured(dir));
            Assert.False(HasAllow(dir, s_everyone));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task EnsureSecured_HardensFilesCreatedInside()
    {
        string dir = NewTempDir();
        try
        {
            CloudStateSecurity.EnsureSecured(dir);

            // Exercise the real store so the directory-prepared callback
            // fires (canonical dir before tmp) and the post-move file callback
            // fires (canonical final file).
            string finalFile = Path.Combine(dir, "cloud-registration.json");
            var store = new PathVeer.Core.Cloud.CloudRegistrationStore(
                finalFile,
                new PathVeer.Core.Cloud.InMemoryCloudSecretProtector(),
                onDirectoryPrepared:
                    PathVeer.Service.Cloud.CloudStateSecurity.HardenDirectory,
                onFilePersisted:
                    PathVeer.Service.Cloud.CloudStateSecurity.HardenFile);
            await store.SaveAsync(
                new PathVeer.Core.Cloud.CloudRegistrationRecord
                {
                    State = PathVeer.Core.Cloud.CloudConnectionState.Connected,
                    DeviceId = "dev",
                    OrganizationId = "org"
                });

            Assert.True(File.Exists(finalFile));
            Assert.True(IsFileCanonical(finalFile),
                "a file written inside the hardened dir must carry the " +
                "canonical DACL (SYSTEM + Administrators only)");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void EnsureSecured_ReHardensPrePlacedPermissiveFile()
    {
        string dir = NewTempDir();
        try
        {
            // Pre-place a file with a hostile explicit ACE before hardening.
            string file = Path.Combine(dir, "legacy.json");
            File.WriteAllText(file, "{}");
            var info = new FileInfo(file);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                s_everyone, FileSystemRights.FullControl,
                AccessControlType.Allow));
            info.SetAccessControl(security);

            CloudStateSecurity.EnsureSecured(dir);

            Assert.True(IsFileCanonical(file),
                "pre-placed file must be re-canonicalized (TOCTOU / " +
                "pre-created-file)");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void TempFileInheritsCanonicalDirNotHostileParent()
    {
        // Hostile PARENT grants Everyone. The Cloud subdir is canonicalized
        // (inheritance disabled), so a tmp file created inside it must NOT
        // inherit the parent's Everyone ACE.
        string parent = NewTempDir();
        try
        {
            var pinfo = new DirectoryInfo(parent);
            var psec = pinfo.GetAccessControl();
            psec.AddAccessRule(new FileSystemAccessRule(
                s_everyone, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            pinfo.SetAccessControl(psec);

            string cloud = Path.Combine(parent, "cloud");
            CloudStateSecurity.EnsureSecured(cloud);

            string tmp = Path.Combine(cloud, "cloud-registration.json.tmp");
            File.WriteAllText(tmp, "{}");

            Assert.False(HasAllow(tmp, s_everyone),
                "tmp file must NOT inherit the hostile parent Everyone ACE");
            Assert.True(IsFileCanonical(tmp)
                || !HasAllow(tmp, s_everyone),
                "tmp file must not grant Everyone access");
        }
        finally
        {
            TryDelete(parent);
        }
    }

    [Fact]
    public void IsSecured_FalseForInheritingDirectory()
    {
        string dir = NewTempDir();
        try
        {
            Assert.False(CloudStateSecurity.IsSecured(dir));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void IsSecured_FalseWhenUnauthorizedExplicitAceSurvives()
    {
        // Canonical but with an injected Everyone allow -> must be FALSE.
        string dir = NewTempDir();
        try
        {
            CloudStateSecurity.EnsureSecured(dir);
            var info = new DirectoryInfo(dir);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                s_everyone, FileSystemRights.Read,
                InheritanceFlags.ContainerInherit
                | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);

            Assert.False(CloudStateSecurity.IsSecured(dir),
                "IsSecured must reject any unauthorized explicit ACE");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void SecureLegacyFileIfPresent_CanonicalizesExistingFile()
    {
        string dir = NewTempDir();
        try
        {
            string legacy = Path.Combine(dir, "cloud-registration.json");
            File.WriteAllText(legacy, "{}");
            var info = new FileInfo(legacy);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                s_everyone, FileSystemRights.FullControl,
                AccessControlType.Allow));
            info.SetAccessControl(security);

            CloudStateSecurity.SecureLegacyFileIfPresent(legacy);

            Assert.True(IsFileCanonical(legacy),
                "legacy file must be canonicalized so ordinary users cannot " +
                "read the vulnerable LocalMachine blob");

            // No-op when absent.
            CloudStateSecurity.SecureLegacyFileIfPresent(
                Path.Combine(dir, "does-not-exist.json"));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---- helpers ----

    private static bool HasAllow(string path, SecurityIdentifier sid)
    {
        var security = new FileInfo(path).GetAccessControl();
        foreach (FileSystemAccessRule rule in
                 security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && (SecurityIdentifier)rule.IdentityReference == sid)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAllowFull(string path, SecurityIdentifier sid)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        foreach (FileSystemAccessRule rule in
                 security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && (SecurityIdentifier)rule.IdentityReference == sid
                && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the file carries the canonical DACL: inheritance disabled and
    /// every allow ACE is exactly SYSTEM or Administrators with FullControl.
    /// </summary>
    private static bool IsFileCanonical(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        if (!security.AreAccessRulesProtected)
        {
            return false;
        }

        bool systemFull = false;
        bool adminFull = false;
        foreach (FileSystemAccessRule rule in
                 security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid == s_system && rule.FileSystemRights.HasFlag(
                    FileSystemRights.FullControl))
            {
                systemFull = true;
            }
            else if (sid == s_administrators && rule.FileSystemRights.HasFlag(
                         FileSystemRights.FullControl))
            {
                adminFull = true;
            }
            else
            {
                return false;
            }
        }

        return systemFull && adminFull;
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "pvcloud-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup in CI.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup in CI.
        }
    }
}
