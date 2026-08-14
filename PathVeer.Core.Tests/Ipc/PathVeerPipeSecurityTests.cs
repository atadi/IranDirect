using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using PathVeer.Core.Ipc;
using Xunit;

namespace PathVeer.Core.Tests.Ipc;

/// <summary>
/// Verifies the explicit least-privilege named-pipe security descriptor used by the PathVeer
/// Service control pipes. These are real Windows ACL assertions (the test TFM is net10.0-windows);
/// they are skipped automatically on non-Windows where PipeSecurity cannot be inspected.
///
/// Intent under test:
///  - LocalSystem + Administrators retain FullControl (service authority + elevated clients).
///  - Builtin Users (ordinary locally-authenticated user, e.g. the certification user) can
///    connect + read + write — but NOT FullControl, NOT ACL write, NOT take-ownership.
///  - No World/Everyone/AuthenticatedUsers grant (narrowest correct local-client population).
///  - Both listen pipes share this single policy (covered by the shared Build() helper).
/// </summary>
public class PathVeerPipeSecurityTests
{
    private static readonly SecurityIdentifier LocalSystem =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier BuiltinUsers =
        new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier World =
        new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsers =
        new(WellKnownSidType.AuthenticatedUserSid, null);

    [Fact]
    public void Build_GrantsIntendedSids_AndNothingElse()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // platform-appropriate: cannot inspect PipeSecurity off Windows.
        }

        PipeSecurity security = PathVeerPipeSecurity.Build();

        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            targetType: typeof(SecurityIdentifier));

        // Three explicit allow rules, least privilege: no deny rules, no extras.
        Assert.Equal(3, rules.Count);
        foreach (PipeAccessRule rule in rules)
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }

        AssertHas(security, LocalSystem, PipeAccessRights.FullControl);
        AssertHas(security, Administrators, PipeAccessRights.FullControl);
        AssertHas(security, BuiltinUsers, PipeAccessRights.ReadWrite);

        // Ordinary users must NOT be granted FullControl (no ACL write / take-ownership).
        AssertDoesNotHave(security, BuiltinUsers, PipeAccessRights.FullControl);
        AssertDoesNotHave(security, BuiltinUsers, PipeAccessRights.TakeOwnership);
        AssertDoesNotHave(security, BuiltinUsers, PipeAccessRights.ChangePermissions);

        // No over-broad principal grants.
        AssertNoRuleFor(security, World);
        AssertNoRuleFor(security, AuthenticatedUsers);
    }

    [Fact]
    public void Build_AppliedToPipe_IsVisibleViaGetAccessControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string pipeName = "PathVeer.Test.PipeSecurity." + Guid.NewGuid().ToString("N");
        PipeSecurity security = PathVeerPipeSecurity.Build();

        using NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security,
            HandleInheritability.None,
            (PipeAccessRights)0);

        try
        {
            PipeSecurity applied = pipe.GetAccessControl();

            AssertHas(applied, LocalSystem, PipeAccessRights.FullControl);
            AssertHas(applied, Administrators, PipeAccessRights.FullControl);
            AssertHas(applied, BuiltinUsers, PipeAccessRights.ReadWrite);
            AssertNoRuleFor(applied, World);
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private static void AssertHas(
        PipeSecurity security,
        SecurityIdentifier sid,
        PipeAccessRights rights)
    {
        AuthorizationRuleCollection rules = security.GetAccessRules(
            true, false, typeof(SecurityIdentifier));
        foreach (PipeAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier id &&
                id.Equals(sid) &&
                rule.AccessControlType == AccessControlType.Allow &&
                rule.PipeAccessRights.HasFlag(rights))
            {
                return;
            }
        }

        Assert.Fail($"Expected allow rule for {sid.Value} with {rights}.");
    }

    private static void AssertDoesNotHave(
        PipeSecurity security,
        SecurityIdentifier sid,
        PipeAccessRights rights)
    {
        AuthorizationRuleCollection rules = security.GetAccessRules(
            true, false, typeof(SecurityIdentifier));
        foreach (PipeAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier id &&
                id.Equals(sid) &&
                rule.AccessControlType == AccessControlType.Allow &&
                rule.PipeAccessRights.HasFlag(rights))
            {
                Assert.Fail($"Did not expect allow rule for {sid.Value} with {rights}.");
            }
        }
    }

    private static void AssertNoRuleFor(PipeSecurity security, SecurityIdentifier sid)
    {
        AuthorizationRuleCollection rules = security.GetAccessRules(
            true, false, typeof(SecurityIdentifier));
        foreach (PipeAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier id && id.Equals(sid))
            {
                Assert.Fail($"Did not expect any rule for {sid.Value}.");
            }
        }
    }
}
