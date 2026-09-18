using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Triesoft.Core.KeyManagement;

/// <summary>
/// Melindungi byte kunci memakai Windows DPAPI (<see cref="ProtectedData"/>).
/// </summary>
/// <remarks>
/// Memakai <see cref="DataProtectionScope.LocalMachine"/>, bukan <see cref="DataProtectionScope.CurrentUser"/>,
/// karena beberapa Operator biasanya memakai satu mesin Polda yang sama lewat login aplikasi TRIESOFT
/// sendiri (bukan akun Windows terpisah per operator). Pemisahan akses antar-operator ditegakkan oleh
/// RBAC aplikasi di fase UI, bukan oleh Windows. Perlindungan DPAPI di sini menutup skenario disk/file
/// dicuri atau berjalan keluar dari mesin -- bukan skenario user lain yang sudah login di mesin yang sama.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = "Triesoft4.KeyStore.v1"u8.ToArray();

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        RequireWindows();
        return ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.LocalMachine);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes)
    {
        RequireWindows();
        return ProtectedData.Unprotect(protectedBytes.ToArray(), Entropy, DataProtectionScope.LocalMachine);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DpapiKeyProtector hanya didukung di Windows.");
    }
}
