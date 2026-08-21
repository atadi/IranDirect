// PathVeer.CloudStateProbe — LocalSystem integration certification helper.
//
// This EXE is invoked by tools/certification/Test-PathVeerCloudStateSecurity.ps1
// inside a disposable NT AUTHORITY\SYSTEM scheduled task. It exercises the EXACT
// production CloudStateSecurity methods against hostile disposable filesystem
// objects and writes a structured result JSON. It contains no product logic of
// its own — it only calls the real hardening/validation entry points:
//
//   PathVeer.Service.Cloud.CloudStateSecurity.HardenDirectory
//   PathVeer.Service.Cloud.CloudStateSecurity.HardenFile
//   PathVeer.Service.Cloud.CloudStateSecurity.IsSecured
//
// It never touches C:\ProgramData\PathVeer and never starts the PathVeer
// service. Exit code is 0 on PASS, 1 on FAIL.

using System.Security.AccessControl;
using System.Security.Principal;

var dir  = args[0];
var file = args[1];
var resultJson = args[2];

var result = new Dictionary<string, object?>
{
    ["identity"] = null,
    ["identitySid"] = null,
    ["dirOwnerAfter"] = null,
    ["fileOwnerAfter"] = null,
    ["dirProtected"] = false,
    ["fileProtected"] = false,
    ["dirUnauthAceCnt"] = -1,
    ["fileUnauthAceCnt"] = -1,
    ["isSecured"] = false,
    ["pass"] = false,
    ["error"] = null,
};

try
{
    var id = WindowsIdentity.GetCurrent();
    result["identity"] = id.Name;
    result["identitySid"] = id.User?.Value;

    // Exact production methods, run under the SYSTEM task identity.
    PathVeer.Service.Cloud.CloudStateSecurity.HardenDirectory(dir);
    PathVeer.Service.Cloud.CloudStateSecurity.HardenFile(file);

    var dirAcl = new DirectoryInfo(dir).GetAccessControl();
    var fileAcl = new FileInfo(file).GetAccessControl();

    result["dirOwnerAfter"] = dirAcl.GetOwner(typeof(SecurityIdentifier))?.ToString();
    result["fileOwnerAfter"] = fileAcl.GetOwner(typeof(SecurityIdentifier))?.ToString();
    result["dirProtected"] = dirAcl.AreAccessRulesProtected;
    result["fileProtected"] = fileAcl.AreAccessRulesProtected;

    var approved = new HashSet<string> { "S-1-5-18", "S-1-5-32-544" }; // SYSTEM, Administrators
    int dirUnauth = 0, fileUnauth = 0;
    foreach (FileSystemAccessRule r in dirAcl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
    {
        if (r.AccessControlType == AccessControlType.Allow &&
            !approved.Contains(r.IdentityReference.Value)) dirUnauth++;
    }
    foreach (FileSystemAccessRule r in fileAcl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
    {
        if (r.AccessControlType == AccessControlType.Allow &&
            !approved.Contains(r.IdentityReference.Value)) fileUnauth++;
    }
    result["dirUnauthAceCnt"] = dirUnauth;
    result["fileUnauthAceCnt"] = fileUnauth;
    result["isSecured"] = PathVeer.Service.Cloud.CloudStateSecurity.IsSecured(dir);

    var sysSid = new SecurityIdentifier("S-1-5-18");
    var sysName = sysSid.Translate(typeof(NTAccount)).Value;
    // dirOwnerAfter/fileOwnerAfter are SID strings (see GetOwner above);
    // compare against the SYSTEM SID value, not the NTAccount name.
    result["pass"] =
        (string?)result["dirOwnerAfter"] == sysSid.Value &&
        (string?)result["fileOwnerAfter"] == sysSid.Value &&
        (bool)result["dirProtected"]! &&
        (bool)result["fileProtected"]! &&
        dirUnauth == 0 && fileUnauth == 0 &&
        (bool)result["isSecured"]!;
}
catch (Exception ex)
{
    result["error"] = ex.Message;
}

await File.WriteAllTextAsync(resultJson,
    System.Text.Json.JsonSerializer.Serialize(result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));

Environment.Exit((bool)result["pass"]! ? 0 : 1);
