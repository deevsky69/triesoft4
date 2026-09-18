using Triesoft.Core.KeyManagement;

namespace Triesoft.App.Services;

/// <summary>
/// PERINGATAN: protector palsu yang TIDAK melindungi apa pun -- byte kunci ditulis apa adanya.
/// Hanya ada di Triesoft.App, TIDAK PERNAH di Triesoft.Core, supaya library produksi tidak punya
/// jalan pintas tidak aman. Dipakai semata supaya aplikasi bisa dijalankan &amp; diverifikasi di
/// mesin non-Windows (DPAPI tidak tersedia di luar Windows) -- lihat juga Triesoft.Cli/Program.cs
/// yang memakai pola sama untuk dev harness CLI.
/// </summary>
public sealed class DevInsecurePassthroughProtector : IKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) => protectedBytes.ToArray();
}
