using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ControllerMagic;

// Verifies a downloaded installer's Authenticode signature before it's ever executed - the one
// genuinely security-sensitive piece of new code this feature needs, kept isolated in its own
// file for easy review. Uses WinVerifyTrust, the same mechanism behind Explorer's "Digital
// Signatures" tab and Get-AuthenticodeSignature, rather than the weaker
// X509Certificate.CreateFromSignedFile alone, which doesn't validate the trust chain or that the
// signature actually covers the whole file - that's used here only afterward, to read which
// certificate a signature WinVerifyTrust already trusted came from.
internal static class AuthenticodeVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdSaferFlag = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
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

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

    // Only the certificate's Common Name is compared - the same field Windows' own Digital
    // Signatures UI surfaces as "the signer" - not the full subject string, whose other fields
    // (locality, org-unit ids, etc.) aren't meaningful to pin against.
    public static bool IsSignedBy(string filePath, string expectedSignerCommonName)
    {
        if (!TryVerifyTrust(filePath))
            return false;

        try
        {
            // X509CertificateLoader (the modern, non-obsolete API) only reads standalone
            // certificate containers (DER/PEM/PFX) - it can't pull the signer certificate back out
            // of a signed PE file, which is the one thing needed here. CreateFromSignedFile is the
            // API built for exactly that extraction; WinVerifyTrust above is what actually
            // establishes trust, so this is only ever used afterward to read which certificate a
            // signature it already trusted came from.
#pragma warning disable SYSLIB0057
            using var rawCert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var cert = new X509Certificate2(rawCert);
            string? cn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.Equals(cn, expectedSignerCommonName, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning($"AuthenticodeVerifier: failed to read the signer of {filePath}", ex);
            return false;
        }
    }

    // Revocation is deliberately not checked (WtdRevokeNone): that needs live network on top of
    // the download that already succeeded, and would turn a transient CRL/OCSP outage into an
    // install failure for an otherwise-genuine signature. WtdSaferFlag still requires a full,
    // valid chain to a trusted root.
    private static bool TryVerifyTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = filePath,
        };

        IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdSaferFlag,
            };
            Marshal.StructureToPtr(data, dataPtr, false);

            int result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr);

            // Always tell WinVerifyTrust to release the state it allocated, regardless of outcome.
            data.dwStateAction = WtdStateActionClose;
            Marshal.StructureToPtr(data, dataPtr, false);
            _ = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr);

            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }
}
