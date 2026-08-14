using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PathVeer.Core.Ipc;

/// <summary>
/// Least-privilege security descriptor for the PathVeer control named pipes.
///
/// The PathVeer Service is a privileged persistent authority (runs as LocalSystem).
/// The Tray and CLI are ordinary-user controllers that must be able to CONNECT to
/// the Service control pipe and issue protocol requests (including read-only status)
/// WITHOUT elevation. The Service remains responsible for authorizing operations.
///
/// A <see cref="NamedPipeServerStream"/> created with no explicit <see cref="PipeSecurity"/>
/// inherits the process token's DEFAULT DACL, which on a LocalSystem service grants
/// SYSTEM + Administrators full control and OMITS ordinary local users — so an
/// intended local client (e.g. the certification user) receives Windows
/// "Access to the path is denied" opening either pipe, while administrative identities
/// connect fine. That is an access-identity policy defect, not a readiness/protocol defect.
///
/// This helper applies an EXPLICIT descriptor so the intended local client population
/// (Builtin Users) can connect + read/write, while SYSTEM and Administrators keep full
/// control. It is deliberately NOT Everyone/World and grants users ReadWrite only (no
/// FullControl, no ACL write, no take-ownership). Both the primary and legacy pipes use
/// this single policy so the two front doors have identical client-access behavior.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PathVeerPipeSecurity
{
    /// <summary>
    /// Builds the explicit pipe security descriptor shared by every PathVeer listen pipe.
    /// </summary>
    public static PipeSecurity Build()
    {
        var security = new PipeSecurity();

        // Service runs as LocalSystem -> must retain full control of its own pipes.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Elevated users / JEA virtual-admin identity (member of Administrators) keep full control.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Intended ordinary local client population (Tray/CLI running as the interactive
        // local user). Narrowest correct choice for a local-machine control channel:
        // Builtin Users covers locally authenticated users without extending to domain/
        // network principals. Grants connect + read + write; NOT FullControl.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
    }
}
