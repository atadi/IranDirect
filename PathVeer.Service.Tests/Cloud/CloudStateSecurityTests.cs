using System.Security.AccessControl;
using System.Security.Principal;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Unit tests for the CANONICAL Cloud security-descriptor definition.
///
/// These tests exercise the pure descriptor logic (the single definition of
/// "canonical" shared by hardening and validation) WITHOUT persisting SYSTEM
/// ownership onto real filesystem objects. That separation is deliberate:
/// assigning the SYSTEM owner requires NT AUTHORITY\SYSTEM authority
/// (SeTakeOwnershipPrivilege / running as LocalSystem), which an ordinary
/// `dotnet test` runner does not have. The actual filesystem persistence of
/// "attacker owner -&gt; SYSTEM owner" is proven by the separate LocalSystem
/// integration tool `tools/certification/Test-PathVeerCloudStateSecurity.ps1`,
/// not by these unit tests.
///
/// The canonical descriptor is:
///   - OWNER = NT AUTHORITY\SYSTEM (a non-SYSTEM owner retains implicit
///     WRITE_DAC and could rewrite the DACL);
///   - DACL = exactly SYSTEM + Administrators, both FullControl;
///   - inheritance disabled (no parent Users-read ACE leaks in).
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
    // attacker-inserted explicit ACE or an attacker owner.
    private static readonly SecurityIdentifier s_arbitraryUser =
        new(WellKnownSidType.BuiltinGuestsSid, null);

    // ---- descriptor construction helpers (no filesystem) ----

    private static DirectorySecurity CanonicalDirectoryDescriptor()
    {
        var ds = new DirectorySecurity();
        ds.SetOwner(s_system);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        return ds;
    }

    private static FileSecurity CanonicalFileDescriptor()
    {
        var fs = new FileSecurity();
        fs.SetOwner(s_system);
        fs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        fs.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.FullControl,
            AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            AccessControlType.Allow));
        return fs;
    }

    // ---- pure descriptor unit tests (no filesystem, no privilege) ----

    [Fact]
    public void IsCanonicalSecurityDescriptor_TrueForSystemOwnerAndSystemAdminFullControl()
    {
        Assert.True(CloudStateSecurity.IsCanonicalSecurityDescriptor(
            CanonicalDirectoryDescriptor()));
        Assert.True(CloudStateSecurity.IsCanonicalSecurityDescriptor(
            CanonicalFileDescriptor()));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForNonSystemOwner()
    {
        // Canonical DACL, but an attacker owns the object -> FALSE. The owner
        // implicitly holds WRITE_DAC and can rewrite the DACL even with no
        // granting ACE, so a non-SYSTEM owner is never "canonical".
        var ds = new DirectorySecurity();
        ds.SetOwner(s_arbitraryUser);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.SetOwner(s_arbitraryUser);
        ds.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForAdministratorsOwner()
    {
        // The Service store is owned by SYSTEM, not Administrators.
        var ds = new DirectorySecurity();
        ds.SetOwner(s_administrators);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForEveryoneAce()
    {
        var ds = CanonicalDirectoryDescriptor();
        ds.AddAccessRule(new FileSystemAccessRule(
            s_everyone, FileSystemRights.Read,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForUsersAce()
    {
        var ds = CanonicalDirectoryDescriptor();
        ds.AddAccessRule(new FileSystemAccessRule(
            s_users, FileSystemRights.Read,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForAuthenticatedUsersAce()
    {
        var ds = CanonicalDirectoryDescriptor();
        ds.AddAccessRule(new FileSystemAccessRule(
            s_authenticatedUsers, FileSystemRights.Read,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForCreatorOwnerAce()
    {
        var ds = CanonicalDirectoryDescriptor();
        ds.AddAccessRule(new FileSystemAccessRule(
            s_creatorOwner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForArbitrarySidAce()
    {
        var ds = CanonicalDirectoryDescriptor();
        ds.AddAccessRule(new FileSystemAccessRule(
            s_arbitraryUser, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseForInheritanceEnabled()
    {
        var ds = new DirectorySecurity();
        ds.SetOwner(s_system);
        // Inheritance NOT disabled -> the permissive parent ACL could leak in.
        ds.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseWhenAdminMissing()
    {
        var ds = new DirectorySecurity();
        ds.SetOwner(s_system);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseWhenSystemMissing()
    {
        var ds = new DirectorySecurity();
        ds.SetOwner(s_system);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_FalseWhenOnlyReadNotFullControl()
    {
        var ds = new DirectorySecurity();
        ds.SetOwner(s_system);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.AddAccessRule(new FileSystemAccessRule(
            s_system, FileSystemRights.Read,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            s_administrators, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }

    [Fact]
    public void IsCanonicalSecurityDescriptor_DenyAceForUnauthorizedSidDoesNotBreakAllowSet()
    {
        // A DENY ACE for an unauthorized SID is not an access grant and is
        // wiped by ApplyCanonicalAcl (which resets the whole DACL). The
        // canonical definition validates the ALLOW set, so a deny does not
        // make an otherwise-canonical descriptor invalid.
        var ds = CanonicalDirectoryDescriptor();
        ds.AddAccessRule(new FileSystemAccessRule(
            s_everyone, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Deny));

        Assert.True(CloudStateSecurity.IsCanonicalSecurityDescriptor(ds));
    }
}
