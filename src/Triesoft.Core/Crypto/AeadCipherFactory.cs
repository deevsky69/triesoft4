using System.Buffers;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using BcChaCha20Poly1305 = Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305;

namespace Triesoft.Core.Crypto;

/// <summary>Membuat <see cref="IAeadCipher"/> untuk sebuah <see cref="AlgorithmProfile"/>. Titik tunggal yang tahu pemetaan profil -> primitif.</summary>
internal static class AeadCipherFactory
{
    public static IAeadCipher Create(AlgorithmProfile profile, ReadOnlySpan<byte> key) => profile switch
    {
        AlgorithmProfile.AesGcm256 => new AesGcmAeadCipher(key),
        AlgorithmProfile.ChaCha20Poly1305 => new ChaCha20Poly1305AeadCipher(key),
        _ => throw new NotSupportedException($"Algorithm profile {profile} belum diimplementasikan."),
    };
}

/// <summary>Adaptor tipis di atas <see cref="AesGcm"/> bawaan .NET -- sudah cocok persis dengan <see cref="IAeadCipher"/>.</summary>
internal sealed class AesGcmAeadCipher : IAeadCipher
{
    private readonly AesGcm _inner;

    public AesGcmAeadCipher(ReadOnlySpan<byte> key) => _inner = new AesGcm(key, Ts4Constants.GcmTagSize);

    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag, ReadOnlySpan<byte> associatedData) =>
        _inner.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

    public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext, ReadOnlySpan<byte> associatedData) =>
        _inner.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// ChaCha20-Poly1305 (RFC 8439) lewat BouncyCastle. Dipilih sebagai algoritma kedua alih-alih
/// <see cref="System.Security.Cryptography.ChaCha20Poly1305"/> bawaan .NET karena kelas itu hanya didukung
/// mulai Windows 11/Server 2022 (CNG) -- di Windows 10, <c>IsSupported</c> bernilai false. Karena mesin Mabes
/// dan tiap Polda harus bisa saling membuka file terlepas versi Windows-nya, dipakai library yang identik
/// perilakunya di semua versi Windows (dan OS lain), bukan bergantung dukungan sistem operasi.
/// </summary>
internal sealed class ChaCha20Poly1305AeadCipher : IAeadCipher
{
    private const int MacSizeBits = Ts4Constants.GcmTagSize * 8;

    private readonly byte[] _key;

    public ChaCha20Poly1305AeadCipher(ReadOnlySpan<byte> key) => _key = key.ToArray();

    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag, ReadOnlySpan<byte> associatedData)
    {
        var cipher = new BcChaCha20Poly1305();
        cipher.Init(forEncryption: true, BuildParameters(nonce, associatedData));

        // BC menggabungkan ciphertext+tag di satu buffer keluaran; dipisah lagi ke parameter ciphertext/tag terpisah
        // supaya pemanggil (EnvelopeCipher) tidak perlu tahu perbedaan ini antar-algoritma.
        var combined = ArrayPool<byte>.Shared.Rent(cipher.GetOutputSize(plaintext.Length));
        try
        {
            var written = cipher.ProcessBytes(plaintext.ToArray(), 0, plaintext.Length, combined, 0);
            written += cipher.DoFinal(combined, written);

            combined.AsSpan(0, ciphertext.Length).CopyTo(ciphertext);
            combined.AsSpan(ciphertext.Length, Ts4Constants.GcmTagSize).CopyTo(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined.AsSpan(0, cipher.GetOutputSize(plaintext.Length)));
            ArrayPool<byte>.Shared.Return(combined);
        }
    }

    public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var cipher = new BcChaCha20Poly1305();
        cipher.Init(forEncryption: false, BuildParameters(nonce, associatedData));

        var combined = ArrayPool<byte>.Shared.Rent(ciphertext.Length + Ts4Constants.GcmTagSize);
        try
        {
            ciphertext.CopyTo(combined);
            tag.CopyTo(combined.AsSpan(ciphertext.Length));

            var outBuffer = ArrayPool<byte>.Shared.Rent(cipher.GetOutputSize(ciphertext.Length + Ts4Constants.GcmTagSize));
            try
            {
                int written;
                try
                {
                    written = cipher.ProcessBytes(combined, 0, ciphertext.Length + Ts4Constants.GcmTagSize, outBuffer, 0);
                    written += cipher.DoFinal(outBuffer, written);
                }
                catch (InvalidCipherTextException ex)
                {
                    // Disamakan dengan AesGcm.Decrypt supaya pemanggil tidak perlu tahu/menangkap tipe khusus BouncyCastle.
                    throw new CryptographicException("Tag autentikasi tidak valid -- data telah diubah atau rusak.", ex);
                }

                outBuffer.AsSpan(0, plaintext.Length).CopyTo(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(outBuffer);
                ArrayPool<byte>.Shared.Return(outBuffer);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined);
            ArrayPool<byte>.Shared.Return(combined);
        }
    }

    private AeadParameters BuildParameters(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData) =>
        new(new KeyParameter(_key), MacSizeBits, nonce.ToArray(), associatedData.ToArray());

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
