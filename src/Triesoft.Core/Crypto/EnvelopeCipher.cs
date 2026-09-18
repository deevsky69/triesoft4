using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Triesoft.Core.Crypto;

/// <summary>Informasi yang dipulihkan dari file .ts4 setelah dekripsi berhasil diverifikasi.</summary>
public sealed record DecryptResult(string OriginalFileName, string KeyId, AlgorithmProfile Profile);

/// <summary>
/// API inti enkripsi/dekripsi file TRIESOFT 4. Memakai envelope encryption dua lapis:
/// DEK (Data Encryption Key) acak baru per file mengenkripsi isi &amp; nama file, dan DEK itu
/// sendiri dibungkus oleh KEK (kunci bulanan) yang disuplai lewat <see cref="MonthlyKey"/>.
/// Isi file dienkripsi per-chunk (AES-256-GCM) supaya file besar tidak perlu dimuat penuh ke
/// memori, dan supaya truncation/reorder/append pada file terenkripsi selalu terdeteksi.
/// </summary>
public static class EnvelopeCipher
{
    /// <summary>
    /// Mengenkripsi <paramref name="plaintextInput"/> ke <paramref name="output"/> sebagai
    /// container .ts4, dibungkus dengan <paramref name="kek"/>.
    /// </summary>
    public static void Encrypt(
        Stream plaintextInput,
        Stream output,
        MonthlyKey kek,
        string originalFileName,
        int chunkSize = Ts4Constants.DefaultChunkSize,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plaintextInput);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(kek);
        ArgumentNullException.ThrowIfNull(originalFileName);
        if (chunkSize <= 0 || chunkSize > Ts4Constants.MaxAllowedChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));

        var dek = CryptoRandom.GetBytes(Ts4Constants.DekSize);
        try
        {
            var keyIdBytes = Encoding.UTF8.GetBytes(kek.KeyId);

            // Bungkus DEK dengan KEK (kunci bulanan dari Bidsandi Mabes).
            var wrapNonce = CryptoRandom.GetBytes(Ts4Constants.WrapNonceSize);
            var wrappedDek = new byte[Ts4Constants.DekSize];
            var wrapTag = new byte[Ts4Constants.GcmTagSize];
            using (var kekCipher = new AesGcm(kek.KeyMaterial, Ts4Constants.GcmTagSize))
            {
                kekCipher.Encrypt(wrapNonce, dek, wrappedDek, wrapTag, keyIdBytes);
            }

            // Enkripsi nama file asli dengan DEK -- metadata ikut dilindungi, bukan cuma isi.
            var nameNonce = CryptoRandom.GetBytes(Ts4Constants.NameNonceSize);
            var nameBytes = Encoding.UTF8.GetBytes(originalFileName);
            var encryptedName = new byte[nameBytes.Length];
            var nameTag = new byte[Ts4Constants.GcmTagSize];
            using (var dekCipherForName = new AesGcm(dek, Ts4Constants.GcmTagSize))
            {
                dekCipherForName.Encrypt(nameNonce, nameBytes, encryptedName, nameTag, keyIdBytes);
            }

            var contentBaseNonce = CryptoRandom.GetBytes(Ts4Constants.BaseNonceSize);

            var header = new Ts4Header
            {
                AlgorithmProfile = AlgorithmProfile.AesGcm256,
                KeyId = kek.KeyId,
                ChunkSize = (uint)chunkSize,
                ContentBaseNonce = contentBaseNonce,
                WrapNonce = wrapNonce,
                WrappedDek = wrappedDek,
                WrapTag = wrapTag,
                NameNonce = nameNonce,
                EncryptedFileName = encryptedName,
                NameTag = nameTag,
            };

            var headerBytes = Ts4FileFormat.SerializeHeader(header);
            output.Write(headerBytes);

            EncryptChunks(plaintextInput, output, dek, contentBaseNonce, headerBytes, chunkSize, progress);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>
    /// Membaca KeyId dari header sebuah container .ts4 TANPA butuh kunci apa pun (KeyId tersimpan
    /// plaintext di header -- hanya DEK dan nama file yang terenkripsi). Dipakai alur dekripsi UI:
    /// tentukan dulu kunci bulanan mana yang dibutuhkan sebelum memintanya dari key store, baru
    /// panggil <see cref="Decrypt"/>. Butuh stream yang bisa di-seek -- posisi stream dikembalikan
    /// ke titik semula sebelum method ini kembali, supaya stream yang sama bisa langsung dipakai
    /// lagi untuk <see cref="Decrypt"/>.
    /// </summary>
    public static string PeekKeyId(Stream ciphertextInput)
    {
        ArgumentNullException.ThrowIfNull(ciphertextInput);
        if (!ciphertextInput.CanSeek)
            throw new NotSupportedException("PeekKeyId butuh stream yang bisa di-seek.");

        var startPosition = ciphertextInput.Position;
        try
        {
            var (header, _) = Ts4FileFormat.ReadHeader(ciphertextInput);
            return header.KeyId;
        }
        finally
        {
            ciphertextInput.Position = startPosition;
        }
    }

    /// <summary>
    /// Mendekripsi container .ts4 dari <paramref name="ciphertextInput"/> ke
    /// <paramref name="output"/>. TIDAK ADA byte plaintext yang ditulis untuk chunk mana pun
    /// sebelum tag autentikasi chunk itu terverifikasi -- kalau ada chunk yang gagal verifikasi,
    /// rusak, atau file dipotong/ditambah data, operasi ini melempar exception dan caller harus
    /// membuang isi <paramref name="output"/> (jangan dipakai sebagai hasil parsial).
    /// </summary>
    public static DecryptResult Decrypt(Stream ciphertextInput, Stream output, MonthlyKey kek, IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertextInput);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(kek);

        var (header, headerBytes) = Ts4FileFormat.ReadHeader(ciphertextInput);

        if (header.KeyId != kek.KeyId)
        {
            throw new CryptographicException(
                $"Kunci tidak cocok: file ini dienkripsi dengan KeyId '{header.KeyId}', " +
                $"tetapi kunci yang diberikan adalah '{kek.KeyId}'.");
        }

        var keyIdBytes = Encoding.UTF8.GetBytes(kek.KeyId);
        var dek = new byte[Ts4Constants.DekSize];
        try
        {
            using (var kekCipher = new AesGcm(kek.KeyMaterial, Ts4Constants.GcmTagSize))
            {
                kekCipher.Decrypt(header.WrapNonce, header.WrappedDek, header.WrapTag, dek, keyIdBytes);
            }

            var nameBuffer = new byte[header.EncryptedFileName.Length];
            using (var dekCipherForName = new AesGcm(dek, Ts4Constants.GcmTagSize))
            {
                dekCipherForName.Decrypt(header.NameNonce, header.EncryptedFileName, header.NameTag, nameBuffer, keyIdBytes);
            }

            var originalFileName = Encoding.UTF8.GetString(nameBuffer);

            DecryptChunks(ciphertextInput, output, dek, header.ContentBaseNonce, headerBytes, (int)header.ChunkSize, progress);

            return new DecryptResult(originalFileName, header.KeyId, header.AlgorithmProfile);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static void EncryptChunks(
        Stream input, Stream output, byte[] dek, byte[] baseNonce, byte[] headerBytes, int chunkSize,
        IProgress<double>? progress)
    {
        using var cipher = new AesGcm(dek, Ts4Constants.GcmTagSize);
        var reader = new ChunkedPlaintextReader(input, chunkSize);
        var plainBuffer = new byte[chunkSize];
        var cipherBuffer = new byte[chunkSize];
        var tag = new byte[Ts4Constants.GcmTagSize];
        var aad = BuildAadBuffer(headerBytes);
        var totalBytes = input.CanSeek ? input.Length - input.Position : (long?)null;

        Span<byte> lenPrefix = stackalloc byte[4];

        try
        {
            ulong counter = 0;
            while (reader.TryReadNextChunk(plainBuffer, out int length, out bool isFinal))
            {
                var nonce = BuildChunkNonce(baseNonce, counter);
                WriteChunkAad(aad, headerBytes.Length, counter, isFinal, length);

                var plainSpan = plainBuffer.AsSpan(0, length);
                var cipherSpan = cipherBuffer.AsSpan(0, length);
                cipher.Encrypt(nonce, plainSpan, cipherSpan, tag, aad);

                BinaryPrimitives.WriteUInt32LittleEndian(lenPrefix, (uint)length);
                output.Write(lenPrefix);
                output.WriteByte((byte)(isFinal ? 1 : 0));
                output.Write(cipherSpan);
                output.Write(tag);

                CryptographicOperations.ZeroMemory(plainSpan);
                counter++;

                if (totalBytes is > 0)
                    progress?.Report(Math.Min(1.0, (double)input.Position / totalBytes.Value));

                if (isFinal) break;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBuffer);
        }

        progress?.Report(1.0);
    }

    private static void DecryptChunks(
        Stream input, Stream output, byte[] dek, byte[] baseNonce, byte[] headerBytes, int chunkSize,
        IProgress<double>? progress)
    {
        using var cipher = new AesGcm(dek, Ts4Constants.GcmTagSize);
        var aad = BuildAadBuffer(headerBytes);
        var totalBytes = input.CanSeek ? input.Length : (long?)null;

        ulong counter = 0;
        bool sawFinal = false;

        Span<byte> lenPrefix = stackalloc byte[4];
        Span<byte> isFinalByte = stackalloc byte[1];

        while (true)
        {
            if (!StreamHelpers.TryReadExactly(input, lenPrefix))
            {
                if (!sawFinal)
                    throw new InvalidDataException("File .ts4 terpotong: chunk akhir tidak ditemukan sebelum EOF.");
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(lenPrefix);
            if (length < 0 || length > chunkSize)
                throw new InvalidDataException("File .ts4 rusak: panjang chunk tidak valid.");

            if (!StreamHelpers.TryReadExactly(input, isFinalByte))
                throw new InvalidDataException("File .ts4 terpotong di tengah chunk.");
            bool isFinal = isFinalByte[0] != 0;

            var cipherBuffer = new byte[length];
            if (!StreamHelpers.TryReadExactly(input, cipherBuffer))
                throw new InvalidDataException("File .ts4 terpotong di tengah chunk.");

            var tag = new byte[Ts4Constants.GcmTagSize];
            if (!StreamHelpers.TryReadExactly(input, tag))
                throw new InvalidDataException("File .ts4 terpotong: tag autentikasi tidak lengkap.");

            var nonce = BuildChunkNonce(baseNonce, counter);
            WriteChunkAad(aad, headerBytes.Length, counter, isFinal, length);

            var plainBuffer = new byte[length];
            try
            {
                // Melempar CryptographicException kalau tag tidak valid (data dipalsukan/rusak).
                cipher.Decrypt(nonce, cipherBuffer, tag, plainBuffer, aad);
                output.Write(plainBuffer);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBuffer);
            }

            counter++;

            if (totalBytes is > 0)
                progress?.Report(Math.Min(1.0, (double)input.Position / totalBytes.Value));

            if (isFinal)
            {
                sawFinal = true;
                break;
            }
        }

        if (!sawFinal)
            throw new InvalidDataException("File .ts4 terpotong: chunk akhir tidak ditemukan.");

        // Pastikan tidak ada data tambahan setelah chunk akhir (append/tamper attack).
        Span<byte> trailing = stackalloc byte[1];
        if (StreamHelpers.TryReadExactly(input, trailing))
            throw new InvalidDataException("Data tambahan terdeteksi setelah akhir file — kemungkinan file telah dimodifikasi.");

        progress?.Report(1.0);
    }

    private static byte[] BuildAadBuffer(byte[] headerBytes)
    {
        var aad = new byte[headerBytes.Length + Ts4Constants.ChunkCounterSize + 1 + 4];
        headerBytes.CopyTo(aad, 0);
        return aad;
    }

    private static byte[] BuildChunkNonce(byte[] baseNonce, ulong counter)
    {
        var nonce = new byte[Ts4Constants.BaseNonceSize + Ts4Constants.ChunkCounterSize];
        baseNonce.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(Ts4Constants.BaseNonceSize), counter);
        return nonce;
    }

    private static void WriteChunkAad(byte[] aad, int headerLength, ulong counter, bool isFinal, int length)
    {
        var span = aad.AsSpan(headerLength);
        BinaryPrimitives.WriteUInt64BigEndian(span, counter);
        span[8] = (byte)(isFinal ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[9..], (uint)length);
    }
}
