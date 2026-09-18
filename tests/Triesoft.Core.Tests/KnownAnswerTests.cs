using System.Security.Cryptography;
using Xunit;

namespace Triesoft.Core.Tests;

/// <summary>
/// Menguji AES-256-GCM bawaan runtime .NET terhadap test vector publik yang dikenal luas,
/// untuk memastikan primitif kriptografi berperilaku sesuai spesifikasi pada platform yang
/// dipakai (bukan menguji kode TRIESOFT sendiri -- itu ada di EnvelopeCipherTests).
/// </summary>
public class KnownAnswerTests
{
    /// <summary>
    /// Sumber: "The Galois/Counter Mode of Operation (GCM)" — McGrew &amp; Viega, Test Case 13
    /// (AES-256, kunci &amp; IV all-zero, plaintext &amp; AAD kosong). Vektor ini juga dipakai
    /// luas di test suite OpenSSL/BoringSSL/Go. Jika assertion ini gagal, verifikasi ulang
    /// nilai tag terhadap NIST SP 800-38D / RFC sebelum menyalahkan implementasi TRIESOFT.
    /// </summary>
    [Fact]
    public void AesGcm256_AllZeroKeyAndIv_EmptyPlaintext_MatchesKnownTag()
    {
        var key = new byte[32];
        var iv = new byte[12];
        var expectedTag = Convert.FromHexString("530F8AFBC74536B9A963B4F1C4CB738B");

        using var aes = new AesGcm(key, 16);
        var ciphertext = Array.Empty<byte>();
        var tag = new byte[16];
        aes.Encrypt(iv, ReadOnlySpan<byte>.Empty, ciphertext, tag);

        Assert.Equal(expectedTag, tag);
    }
}
