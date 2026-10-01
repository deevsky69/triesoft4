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

    /// <summary>
    /// Sumber: RFC 8439 §2.8.2, "An Example" -- vektor uji resmi AEAD_CHACHA20_POLY1305, diambil
    /// langsung dari teks RFC (rfc-editor.org/rfc/rfc8439.txt), bukan dari ingatan. Menguji BouncyCastle
    /// (dipakai lewat <see cref="Triesoft.Core.Crypto.ChaCha20Poly1305AeadCipher"/>), bukan kode TRIESOFT sendiri.
    /// </summary>
    [Fact]
    public void ChaCha20Poly1305_Rfc8439Vector_MatchesKnownCiphertextAndTag()
    {
        var plaintext = "Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it."u8.ToArray();
        var aad = Convert.FromHexString("50515253c0c1c2c3c4c5c6c7");
        var key = Convert.FromHexString("808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f");
        var nonce = Convert.FromHexString("070000004041424344454647");
        var expectedCiphertext = Convert.FromHexString(
            "d31a8d34648e60db7b86afbc53ef7ec2" +
            "a4aded51296e08fea9e2b5a736ee62d6" +
            "3dbea45e8ca9671282fafb69da92728b" +
            "1a71de0a9e060b2905d6a5b67ecd3b36" +
            "92ddbd7f2d778b8c9803aee328091b58" +
            "fab324e4fad675945585808b4831d7bc" +
            "3ff4def08e4b7a9de576d26586cec64b" +
            "6116");
        var expectedTag = Convert.FromHexString("1ae10b594f09e26a7e902ecbd0600691");

        using var cipher = Triesoft.Core.Crypto.AeadCipherFactory.Create(Triesoft.Core.Crypto.AlgorithmProfile.ChaCha20Poly1305, key);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        Assert.Equal(expectedCiphertext, ciphertext);
        Assert.Equal(expectedTag, tag);

        var decrypted = new byte[plaintext.Length];
        cipher.Decrypt(nonce, ciphertext, tag, decrypted, aad);
        Assert.Equal(plaintext, decrypted);
    }
}
