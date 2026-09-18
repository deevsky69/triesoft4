using Triesoft.Core.KeyManagement;

namespace Triesoft.Core.Tests.KeyManagement;

/// <summary>
/// Protector palsu (passthrough) khusus untuk test -- TIDAK PERNAH dipakai kode produksi.
/// Dipakai supaya test storage/lifecycle bisa jalan lintas-OS tanpa bergantung pada DPAPI
/// (yang hanya tersedia di Windows).
/// </summary>
internal sealed class PlaintextKeyProtectorForTests : IKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) => protectedBytes.ToArray();
}
