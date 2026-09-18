namespace Triesoft.Core.KeyManagement;

/// <summary>
/// Melindungi byte kunci saat disimpan di disk. Implementasi saat ini: <see cref="DpapiKeyProtector"/>
/// (Windows DPAPI). Diabstraksikan supaya nanti bisa diganti smart card/HSM (PKCS#11) tanpa
/// mengubah <see cref="FileKeyStore"/> atau <see cref="MonthlyKeyManager"/>.
/// </summary>
public interface IKeyProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> protectedBytes);
}
