using Triesoft.Core.KeyManagement;

namespace Triesoft.App.Tests;

/// <summary>Protector palsu khusus test -- lihat padanannya di Triesoft.Core.Tests/KeyManagement.</summary>
internal sealed class PassthroughKeyProtector : IKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) => protectedBytes.ToArray();
}
