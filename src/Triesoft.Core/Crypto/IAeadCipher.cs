namespace Triesoft.Core.Crypto;

/// <summary>
/// Primitif AEAD (authenticated encryption with associated data) yang dipakai <see cref="EnvelopeCipher"/>,
/// dengan tanda tangan method yang sama persis dengan <see cref="System.Security.Cryptography.AesGcm"/> supaya
/// satu instance bisa dipakai berulang kali dengan nonce berbeda-beda (satu per chunk) tanpa membangun ulang
/// jadwal kunci tiap panggilan. Semua profil yang didukung memakai nonce 12 byte dan tag 16 byte
/// (lihat <see cref="Ts4Constants"/>), jadi format file tidak berubah sama sekali antar profil.
/// </summary>
internal interface IAeadCipher : IDisposable
{
    void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag, ReadOnlySpan<byte> associatedData);

    /// <summary>Melempar <see cref="System.Security.Cryptography.CryptographicException"/> kalau tag tidak valid (data dipalsukan/rusak), terlepas dari algoritmanya.</summary>
    void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext, ReadOnlySpan<byte> associatedData);
}
