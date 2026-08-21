using System.Security.AccessControl;
using System.Security.Principal;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Exercises the on-disk security boundary for the Cloud credential:
/// the dedicated directory must deny ordinary users read access, retain
/// SYSTEM + Administrators, and harden files created inside it (including the
/// atomic tmp file and any pre-placed permissive file).
/// </summary>
public sealed class CloudStateSecurityTests
{
    private static readonly SecurityIdentifier s_users =
        new(WellKnownSidType.BuiltinUsersSid, null);

    [Fact]
    public void EnsureSecured_RemovesUsersAndKeepsSystemAdmin()
    {
        string dir = NewTempDir();
        try
        {
            CloudStateSecurity.EnsureSecured(dir);

            Assert.True(CloudStateSecurity.IsSecured(dir));

            var security = new DirectoryInfo(dir).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected,
                "inheritance must be disabled so parent Users-read ACL is not inherited");

            bool usersRead = false;
            bool systemFull = false;
            bool adminFull = false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                         true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var sid = (SecurityIdentifier)rule.IdentityReference;
                if (sid == s_users
                    && (rule.FileSystemRights & FileSystemRights.Read) != 0)
                {
                    usersRead = true;
                }
                else if (sid == new SecurityIdentifier(
                             WellKnownSidType.LocalSystemSid, null)
                         && rule.FileSystemRights.HasFlag(
                             FileSystemRights.FullControl))
                {
                    systemFull = true;
                }
                else if (sid == new SecurityIdentifier(
                             WellKnownSidType.BuiltinAdministratorsSid, null)
                         && rule.FileSystemRights.HasFlag(
                             FileSystemRights.FullControl))
                {
                    adminFull = true;
                }
            }

            Assert.False(usersRead, "BUILTIN\\Users must not have read access");
            Assert.True(systemFull, "SYSTEM must retain FullControl");
            Assert.True(adminFull, "Administrators must retain FullControl");
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
            // Second call must not throw and must keep the boundary intact.
            CloudStateSecurity.EnsureSecured(dir);

            Assert.True(CloudStateSecurity.IsSecured(dir));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void EnsureSecured_HardensFilesCreatedInside()
    {
        string dir = NewTempDir();
        try
        {
            CloudStateSecurity.EnsureSecured(dir);

            // Simulate the atomic JSON store writing into the hardened dir:
            // the persisted file (final, post-move) must NOT be readable by
            // Users. We exercise the real store so the post-write hardening
            // hook fires.
            string finalFile = Path.Combine(dir, "cloud-registration.json");
            var store = new PathVeer.Core.Cloud.CloudRegistrationStore(
                finalFile,
                new PathVeer.Core.Cloud.InMemoryCloudSecretProtector(),
                onFilePersisted: PathVeer.Service.Cloud.CloudStateSecurity.HardenFile);
            store.SaveAsync(
                new PathVeer.Core.Cloud.CloudRegistrationRecord
                {
                    State = PathVeer.Core.Cloud.CloudConnectionState.Connected,
                    DeviceId = "dev",
                    OrganizationId = "org"
                }).GetAwaiter().GetResult();

            Assert.True(File.Exists(finalFile));

            var security = new FileInfo(finalFile).GetAccessControl();
            bool usersRead = false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                         true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow
                    && (SecurityIdentifier)rule.IdentityReference == s_users
                    && (rule.FileSystemRights & FileSystemRights.Read) != 0)
                {
                    usersRead = true;
                }
            }

            Assert.False(usersRead,
                "a file written inside the hardened dir must not grant Users read");
            Assert.True(security.AreAccessRulesProtected);
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
            // Create a file with default (inherited/permissive) ACL BEFORE
            // hardening the directory, simulating a legacy state file that an
            // ordinary user may have been able to read.
            string file = Path.Combine(dir, "legacy.json");
            File.WriteAllText(file, "{}");
            Assert.False(CloudStateSecurity.IsSecured(dir));

            CloudStateSecurity.EnsureSecured(dir);

            var security = new FileInfo(file).GetAccessControl();
            bool usersRead = false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                         true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow
                    && (SecurityIdentifier)rule.IdentityReference == s_users
                    && (rule.FileSystemRights & FileSystemRights.Read) != 0)
                {
                    usersRead = true;
                }
            }

            Assert.False(usersRead,
                "pre-placed file must be re-hardened (TOCTOU / pre-created-file)");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void IsSecured_FalseForInheritingDirectory()
    {
        // A plain temp directory inherits the parent ACL (Users read on
        // ProgramData roots) and must NOT be reported as secured.
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
