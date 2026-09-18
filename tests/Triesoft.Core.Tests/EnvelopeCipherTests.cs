using System.Security.Cryptography;
using System.Text;
using Triesoft.Core.Crypto;
using Xunit;

namespace Triesoft.Core.Tests;

public class EnvelopeCipherTests
{
    private const int DefaultChunkSize = 1024 * 1024;

    private static MonthlyKey NewKey(string keyId = "2026-09-TEST") =>
        MonthlyKey.FromBytes(keyId, RandomNumberGenerator.GetBytes(MonthlyKey.KeySizeInBytes));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(64 * 1024)]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 + 1)]
    [InlineData(5 * 1024 * 1024)]
    public void RoundTrip_ProducesIdenticalPlaintext(int size)
    {
        var plaintext = RandomNumberGenerator.GetBytes(size);
        using var kek = NewKey();

        using var inputStream = new MemoryStream(plaintext);
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(inputStream, encrypted, kek, "dokumen-rahasia.pdf");

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        var result = EnvelopeCipher.Decrypt(encrypted, decrypted, kek);

        Assert.Equal(plaintext, decrypted.ToArray());
        Assert.Equal("dokumen-rahasia.pdf", result.OriginalFileName);
        Assert.Equal(kek.KeyId, result.KeyId);
        Assert.Equal(AlgorithmProfile.AesGcm256, result.Profile);
    }

    [Fact]
    public void RoundTrip_ManyChunks_WithSmallChunkSize()
    {
        var plaintext = RandomNumberGenerator.GetBytes(500_000);
        using var kek = NewKey();

        using var inputStream = new MemoryStream(plaintext);
        using var encrypted = new MemoryStream();
        EnvelopeCipher.Encrypt(inputStream, encrypted, kek, "besar.docx", chunkSize: 4096);

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        EnvelopeCipher.Decrypt(encrypted, decrypted, kek);

        Assert.Equal(plaintext, decrypted.ToArray());
    }

    [Fact]
    public void Decrypt_TamperedCiphertextByte_Throws()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "a.jpg", "isi rahasia sedang diuji"u8.ToArray());

        encrypted[encrypted.Length - 20] ^= 0xFF;

        using var input = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        Assert.ThrowsAny<CryptographicException>(() => EnvelopeCipher.Decrypt(input, output, kek));
    }

    [Fact]
    public void Decrypt_TamperedFinalTag_Throws()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "a.jpg", "data uji"u8.ToArray());
        encrypted[^1] ^= 0xFF; // byte terakhir = bagian dari tag chunk akhir

        using var input = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        Assert.ThrowsAny<CryptographicException>(() => EnvelopeCipher.Decrypt(input, output, kek));
    }

    [Fact]
    public void Decrypt_TruncatedMidChunk_Throws()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "besar.pptx", RandomNumberGenerator.GetBytes(200_000), chunkSize: 4096);

        var truncated = encrypted[..(encrypted.Length / 2)];

        using var input = new MemoryStream(truncated);
        using var output = new MemoryStream();
        Assert.ThrowsAny<Exception>(() => EnvelopeCipher.Decrypt(input, output, kek));
    }

    [Fact]
    public void Decrypt_AppendedTrailingBytes_Throws()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "kecil.txt", "data"u8.ToArray()).ToList();
        encrypted.Add(0xAB);

        using var input = new MemoryStream(encrypted.ToArray());
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => EnvelopeCipher.Decrypt(input, output, kek));
    }

    [Fact]
    public void Decrypt_WrongKeyMaterial_SameKeyId_Throws()
    {
        using var kek = NewKey("2026-09-SHARED-ID");
        var encrypted = EncryptSample(kek, "a.pdf", "rahasia"u8.ToArray());

        using var wrongKek = MonthlyKey.FromBytes("2026-09-SHARED-ID", RandomNumberGenerator.GetBytes(32));

        using var input = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        Assert.ThrowsAny<CryptographicException>(() => EnvelopeCipher.Decrypt(input, output, wrongKek));
    }

    [Fact]
    public void Decrypt_MismatchedKeyId_ThrowsWithClearMessage()
    {
        using var kek = NewKey("2026-09-A");
        var encrypted = EncryptSample(kek, "a.pdf", "rahasia"u8.ToArray());

        using var otherKek = MonthlyKey.FromBytes("2026-09-B", RandomNumberGenerator.GetBytes(32));

        using var input = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        var ex = Assert.Throws<CryptographicException>(() => EnvelopeCipher.Decrypt(input, output, otherKek));
        Assert.Contains("2026-09-A", ex.Message);
        Assert.Contains("2026-09-B", ex.Message);
    }

    [Fact]
    public void Decrypt_CorruptedMagicBytes_Throws()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "a.pdf", "data"u8.ToArray());
        encrypted[0] ^= 0xFF;

        using var input = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => EnvelopeCipher.Decrypt(input, output, kek));
    }

    [Fact]
    public void EncryptedFile_DoesNotContainPlaintextFileName()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "laporan-intel-sangat-rahasia.docx", "isi"u8.ToArray());

        var headerRegion = Encoding.UTF8.GetString(encrypted, 0, Math.Min(encrypted.Length, 512));
        Assert.DoesNotContain("laporan-intel-sangat-rahasia", headerRegion);
    }

    private static byte[] EncryptSample(MonthlyKey kek, string fileName, byte[] plaintext, int chunkSize = DefaultChunkSize)
    {
        using var input = new MemoryStream(plaintext);
        using var output = new MemoryStream();
        EnvelopeCipher.Encrypt(input, output, kek, fileName, chunkSize);
        return output.ToArray();
    }

    [Fact]
    public void PeekKeyId_ReturnsKeyId_WithoutNeedingAnyKey()
    {
        using var kek = NewKey("2026-09-PEEK-TEST");
        var encrypted = EncryptSample(kek, "a.pdf", "data"u8.ToArray());

        using var stream = new MemoryStream(encrypted);
        var keyId = EnvelopeCipher.PeekKeyId(stream);

        Assert.Equal("2026-09-PEEK-TEST", keyId);
    }

    [Fact]
    public void PeekKeyId_ResetsStreamPosition_SoDecryptCanFollow()
    {
        using var kek = NewKey("2026-09-PEEK-RESET");
        var plaintext = "isi rahasia"u8.ToArray();
        var encrypted = EncryptSample(kek, "a.pdf", plaintext);

        using var stream = new MemoryStream(encrypted);
        var keyId = EnvelopeCipher.PeekKeyId(stream);
        Assert.Equal(0, stream.Position);

        using var output = new MemoryStream();
        var result = EnvelopeCipher.Decrypt(stream, output, kek);

        Assert.Equal(keyId, result.KeyId);
        Assert.Equal(plaintext, output.ToArray());
    }

    [Fact]
    public void PeekKeyId_NonSeekableStream_Throws()
    {
        using var kek = NewKey();
        var encrypted = EncryptSample(kek, "a.pdf", "data"u8.ToArray());

        using var nonSeekable = new NonSeekableStream(new MemoryStream(encrypted));
        Assert.Throws<NotSupportedException>(() => EnvelopeCipher.PeekKeyId(nonSeekable));
    }

    [Fact]
    public void Encrypt_ReportsProgress_ReachingOne()
    {
        using var kek = NewKey();
        var plaintext = RandomNumberGenerator.GetBytes(500_000);
        var progress = new SyncProgress<double>();

        using var input = new MemoryStream(plaintext);
        using var output = new MemoryStream();
        EnvelopeCipher.Encrypt(input, output, kek, "besar.pdf", chunkSize: 4096, progress: progress);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal(1.0, progress.Reports[^1]);
    }

    // System.Progress<T> memarshal callback lewat SynchronizationContext/ThreadPool (asinkron) --
    // tidak cocok untuk assertion langsung setelah pemanggilan sinkron di test. IProgress<T> ini
    // memanggil balik secara langsung/sinkron, sama seperti EnvelopeCipher memanggilnya.
    private sealed class SyncProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];
        public void Report(T value) => Reports.Add(value);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
