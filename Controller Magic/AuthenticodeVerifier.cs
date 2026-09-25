using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace ControllerMagic;

// Verifies a downloaded installer's Authenticode signature before it's ever executed - the one
// genuinely security-sensitive piece of new code this feature needs, kept isolated in its own
// file for easy review. Uses WinVerifyTrust, the same mechanism behind Explorer's "Digital
// Signatures" tab and Get-AuthenticodeSignature, which validates the trust chain and that the
// signature covers the whole file. The signer is read from that same verification's state, never
// by re-opening the file, so the certificate checked is the one WinVerifyTrust trusted.
internal static class AuthenticodeVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckNone = 0x10;

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

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr pProvData, uint idxSigner, [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner, uint idxCounterSigner);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr pSgnr, uint idxCert);

    // Only the certificate's Common Name is compared - the same field Windows' own Digital
    // Signatures UI surfaces as "the signer" - not the full subject string, whose other fields
    // (locality, org-unit ids, etc.) aren't meaningful to pin against.
    public static bool IsSignedBy(string filePath, string expectedSignerCommonName) =>
        IsSignedBy(filePath, fileHandle: null, expectedSignerCommonName);

    // Verifies the bytes behind an already-open handle, so the file can't change underneath.
    public static bool IsSignedBy(FileStream file, string expectedSignerCommonName)
    {
        ArgumentNullException.ThrowIfNull(file);
        return IsSignedBy(file.Name, file.SafeFileHandle, expectedSignerCommonName);
    }

    private static bool IsSignedBy(string filePath, SafeFileHandle? fileHandle, string expectedSignerCommonName)
    {
        using var signer = VerifyAndGetSigner(filePath, fileHandle);
        if (signer == null)
            return false;

        string? cn = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.Equals(cn, expectedSignerCommonName, StringComparison.Ordinal);
    }

    // Revocation is deliberately off for the leaf and the whole chain, so a slow or unreachable
    // CRL/OCSP server can neither hang nor fail an install; the chain must still reach a trusted root.
    private static X509Certificate2? VerifyAndGetSigner(string filePath, SafeFileHandle? fileHandle)
    {
        bool handleAddRefed = false;
        bool fileInfoMarshalled = false;
        IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
        try
        {
            fileHandle?.DangerousAddRef(ref handleAddRefed);
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath,
                hFile = handleAddRefed ? fileHandle!.DangerousGetHandle() : IntPtr.Zero,
            };
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            fileInfoMarshalled = true;

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdRevocationCheckNone,
            };
            Marshal.StructureToPtr(data, dataPtr, false);

            int result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr);
            try
            {
                return result == 0 ? ReadSigner(Marshal.PtrToStructure<WinTrustData>(dataPtr).hWVTStateData) : null;
            }
            finally
            {
                // The close call needs the state handle WinVerifyTrust wrote back into dataPtr, so
                // only the action field is patched rather than re-marshalling the managed copy.
                Marshal.WriteInt32(dataPtr, (int)Marshal.OffsetOf<WinTrustData>(nameof(WinTrustData.dwStateAction)), (int)WtdStateActionClose);
                _ = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr);
            }
        }
        finally
        {
            if (handleAddRefed)
                fileHandle!.DangerousRelease();

            // StructureToPtr allocated a native copy of the path string that FreeHGlobal alone leaks.
            if (fileInfoMarshalled)
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPtr);
            Marshal.FreeHGlobal(dataPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    // The X509Certificate2 duplicates the context, so it outlives the state closed right after.
    private static X509Certificate2? ReadSigner(IntPtr stateData)
    {
        IntPtr provData = WTHelperProvDataFromStateData(stateData);
        IntPtr signer = provData == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provData, 0, false, 0);
        IntPtr providerCert = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
        if (providerCert == IntPtr.Zero)
            return null;

        // CRYPT_PROVIDER_CERT is { DWORD cbStruct; PCCERT_CONTEXT pCert; ... }: pCert sits at pointer alignment.
        IntPtr certContext = Marshal.ReadIntPtr(providerCert, IntPtr.Size);
        return certContext == IntPtr.Zero ? null : new X509Certificate2(certContext);
    }
}
