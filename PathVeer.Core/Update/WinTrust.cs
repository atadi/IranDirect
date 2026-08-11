namespace PathVeer.Core.Update;

using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// Minimal, audited P/Invoke wrapper around WinVerifyTrust for Authenticode
/// verification on Windows. Intentionally narrow: it verifies the default
/// Authenticode action and reads the signer certificate subject. No signing,
/// no export, no private-key access.
///
/// On non-Windows platforms the methods short-circuit (VerifyAuthenticode returns
/// a non-Success result so callers fall back to hash-only validation).
/// </summary>
internal static class WinTrust
{
    public enum TrustResult : uint
    {
        Success = 0,
        ProviderUnknown = 0x800B0001,
        ActionUnknown = 0x800B0002,
        SubjectFormUnknown = 0x800B0003,
        SubjectNotTrusted = 0x800B0004,
        InvalidSignature = 0x800B0100,
        IncompleteSignature = 0x800B0100,
        UntrustedRoot = 0x800B0109,
        Chronological = 0x800B0102,
        GenericError = 0x800B0100
    }

    // WINTRUST_ACTION_GENERIC_VERIFY_V2
    private static readonly Guid WintrustActionGenericVerifyV2 =
        new(0xaac56b, 0xcd44, 0x11d0, 0x8c, 0xc2, 0x0, 0xc0, 0x4f, 0xc2, 0x95, 0xe1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    private const uint WtdChoiceFile = 1;
    private const uint WtdUICreate = 1; // WTD_UI_NONE is 2; choose none for silent
    private const uint WtdUiNone = 2;
    private const uint WtdRevocationCheckNone = 0;
    private const uint WtdRevocationCheckFull = 3;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdProvFlagsNone = 0;

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.Struct)] Guid pgActionID, IntPtr pWvtData);

    public static TrustResult VerifyAuthenticode(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return (TrustResult)1; // non-zero => not success
        IntPtr pData = IntPtr.Zero;
        IntPtr pFile = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            var wtd = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevocationCheckFull,
                dwUnionChoice = WtdChoiceFile,
                pFile = IntPtr.Zero,
                dwStateAction = WtdStateActionIgnore,
                dwProvFlags = WtdProvFlagsNone
            };
            pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, pFile, false);
            wtd.pFile = pFile;
            pData = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(wtd, pData, false);

            var hr = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, pData);
            return (TrustResult)hr;
        }
        catch
        {
            return TrustResult.GenericError;
        }
        finally
        {
            if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
            if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
        }
    }

    public static string? GetCertificateSubject(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            // X509Certificate2 on Windows reads the signer info for Authenticode.
            using var cert = new X509Certificate2(filePath);
            if (string.IsNullOrEmpty(cert.Subject)) return null;
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }
}
