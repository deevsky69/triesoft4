using System.Security.Cryptography;
using Triesoft.Core.Crypto;
using Xunit;

namespace Triesoft.Core.Tests;

/// <summary>
/// Cakupan yang sama dengan <see cref="EnvelopeCipherTests"/> tapi untuk profil ChaCha20-Poly1305,
/// plus pengecekan lintas-algoritma. Round-trip AES sendiri tetap ada di <see cref="EnvelopeCipherTests"/>.
/// </summary>
public class EnvelopeCipherAlgorithmTests
{
    private static MonthlyKey NewKey(string keyId = "2026-09-TEST") =>
        MonthlyKey.FromBytes(keyId, RandomNumberGenerator.GetBytes(MonthlyKey.KeySizeInBytes));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 + 1)]
    [InlineData(2_500_000)]
    public void ChaCha20Poly1305_RoundTrip_ProducesIdenticalPlaintext(int size)
    {
        var plaintext = RandomNumberGenerator.GetBytes(size);
        using var kek = NewKey();

        using var inputStream = new MemoryStream(plaintext);
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(inputStream, encrypted, kek, "dokumen.pdf", algorithm: AlgorithmProfile.ChaCha20Poly1305);

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        var result = EnvelopeCipher.Decrypt(encrypted, decrypted, kek);

        Assert.Equal(plaintext, decrypted.ToArray());
        Assert.Equal("dokumen.pdf", result.OriginalFileName);
        Assert.Equal(AlgorithmProfile.ChaCha20Poly1305, result.Profile);
    }

    [Fact]
    public void ChaCha20Poly1305_ManyChunks_WithSmallChunkSize()
    {
        var plaintext = RandomNumberGenerator.GetBytes(500_000);
        using var kek = NewKey();

        using var inputStream = new MemoryStream(plaintext);
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(inputStream, encrypted, kek, "besar.docx", algorithm: AlgorithmProfile.ChaCha20Poly1305, chunkSize: 4096);

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        EnvelopeCipher.Decrypt(encrypted, decrypted, kek);

        Assert.Equal(plaintext, decrypted.ToArray());
    }

    [Theory]
    [InlineData(20)] // di dalam tag chunk akhir
    [InlineData(200)] // di tengah ciphertext
    public void ChaCha20Poly1305_TamperedByte_Throws(int offsetFromEnd)
    {
        using var kek = NewKey();
        var plaintext = RandomNumberGenerator.GetBytes(2000);
        using var input = new MemoryStream(plaintext);
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(input, encrypted, kek, "a.jpg", algorithm: AlgorithmProfile.ChaCha20Poly1305);
        var bytes = encrypted.ToArray();
        bytes[^offsetFromEnd] ^= 0xFF;

        using var tamperedInput = new MemoryStream(bytes);
        using var output = new MemoryStream();
        Assert.ThrowsAny<CryptographicException>(() => EnvelopeCipher.Decrypt(tamperedInput, output, kek));
    }

    [Fact]
    public void ChaCha20Poly1305_WrongKeyMaterial_Throws()
    {
        using var kek = NewKey("2026-09-SHARED");
        using var input = new MemoryStream("rahasia"u8.ToArray());
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(input, encrypted, kek, "a.pdf", algorithm: AlgorithmProfile.ChaCha20Poly1305);

        using var wrongKek = MonthlyKey.FromBytes("2026-09-SHARED", RandomNumberGenerator.GetBytes(32));
        encrypted.Position = 0;
        using var output = new MemoryStream();
        Assert.ThrowsAny<CryptographicException>(() => EnvelopeCipher.Decrypt(encrypted, output, wrongKek));
    }

    [Fact]
    public void DecryptDoesNotNeedAlgorithmSpecified_AutoDetectedFromHeader()
    {
        // Decrypt tidak punya parameter algoritma sama sekali -- ini membuktikan file ChaCha20
        // tetap terbuka lewat pemanggilan Decrypt yang identik dengan file AES-GCM.
        using var kek = NewKey();
        using var input = new MemoryStream("isi"u8.ToArray());
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(input, encrypted, kek, "x.txt", algorithm: AlgorithmProfile.ChaCha20Poly1305);
        encrypted.Position = 0;

        using var output = new MemoryStream();
        var result = EnvelopeCipher.Decrypt(encrypted, output, kek);

        Assert.Equal(AlgorithmProfile.ChaCha20Poly1305, result.Profile);
    }

    [Fact]
    public void DefaultAlgorithm_IsStillAesGcm256_ExistingCallersUnaffected()
    {
        using var kek = NewKey();
        using var input = new MemoryStream("data"u8.ToArray());
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(input, encrypted, kek, "x.txt"); // tanpa parameter algorithm sama sekali

        encrypted.Position = 0;
        using var output = new MemoryStream();
        var result = EnvelopeCipher.Decrypt(encrypted, output, kek);

        Assert.Equal(AlgorithmProfile.AesGcm256, result.Profile);
    }

    [Fact]
    public void SameKeyAndPlaintext_DifferentAlgorithm_ProducesDifferentCiphertext()
    {
        using var kek = NewKey();
        var plaintext = RandomNumberGenerator.GetBytes(5000);

        using var aesOut = new MemoryStream();
        EnvelopeCipher.Encrypt(new MemoryStream(plaintext), aesOut, kek, "x.txt", algorithm: AlgorithmProfile.AesGcm256);

        using var chachaOut = new MemoryStream();
        EnvelopeCipher.Encrypt(new MemoryStream(plaintext), chachaOut, kek, "x.txt", algorithm: AlgorithmProfile.ChaCha20Poly1305);

        Assert.NotEqual(aesOut.ToArray(), chachaOut.ToArray());
    }

    [Theory]
    [InlineData(AlgorithmProfile.NationalReserved)]
    [InlineData(AlgorithmProfile.PostQuantumReserved)]
    public void Encrypt_UnimplementedProfile_ThrowsBeforeWritingAnything(AlgorithmProfile profile)
    {
        using var kek = NewKey();
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() =>
            EnvelopeCipher.Encrypt(new MemoryStream("x"u8.ToArray()), output, kek, "x.txt", algorithm: profile));

        Assert.Equal(0, output.Length); // gagal cepat -- tidak ada header/byte parsial yang tertulis
    }

    [Fact]
    public void Decrypt_UnimplementedProfileInHeader_IsRejected()
    {
        // Header dengan profile byte yang dicadangkan tapi belum diimplementasikan (0x03) harus ditolak,
        // bukan diperlakukan sebagai AES-GCM secara diam-diam.
        using var kek = NewKey();
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(new MemoryStream("x"u8.ToArray()), encrypted, kek, "x.txt");
        var bytes = encrypted.ToArray();

        var profileByteOffset = 4 + 1; // magic (4) + versi format (1)
        Assert.Equal(1, bytes[profileByteOffset]); // sanity: memang AesGcm256 = 0x01 di posisi itu
        bytes[profileByteOffset] = 0x03;

        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => EnvelopeCipher.Decrypt(input, output, kek));
    }
}
