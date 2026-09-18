using System.Security.Cryptography;

namespace Triesoft.Core.Crypto;

/// <summary>
/// Representasi kunci bulanan (KEK — Key Encryption Key) yang didistribusikan
/// Bidsandi Mabes ke Polda. Kunci ini TIDAK dipakai langsung untuk mengenkripsi
/// isi file — hanya untuk membungkus DEK acak yang dibuat baru per file
/// (lihat <see cref="EnvelopeCipher"/>).
/// </summary>
public sealed class MonthlyKey : IDisposable
{
    /// <summary>Ukuran kunci yang diterima: 32 byte (AES-256).</summary>
    public const int KeySizeInBytes = 32;

    private readonly byte[] _keyMaterial;
    private bool _disposed;

    /// <summary>
    /// Identitas kunci ini, misal <c>"2026-09-POLDA-JATIM"</c>. Dipakai untuk
    /// mencocokkan file terenkripsi dengan kunci yang benar saat dekripsi,
    /// dan diikat sebagai associated data pada setiap operasi AEAD yang memakai kunci ini.
    /// </summary>
    public string KeyId { get; }

    private MonthlyKey(string keyId, byte[] keyMaterial)
    {
        KeyId = keyId;
        _keyMaterial = keyMaterial;
    }

    /// <summary>
    /// Membuat <see cref="MonthlyKey"/> dari material kunci 32-byte yang sudah ada
    /// (mis. hasil pembangkitan/distribusi dari Bidsandi Mabes).
    /// </summary>
    public static MonthlyKey FromBytes(string keyId, ReadOnlySpan<byte> keyMaterial)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("KeyId tidak boleh kosong.", nameof(keyId));
        if (keyMaterial.Length != KeySizeInBytes)
        {
            throw new ArgumentException(
                $"Kunci bulanan harus {KeySizeInBytes} byte (AES-256), diterima {keyMaterial.Length} byte.",
                nameof(keyMaterial));
        }

        return new MonthlyKey(keyId, keyMaterial.ToArray());
    }

    internal ReadOnlySpan<byte> KeyMaterial
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _keyMaterial;
        }
    }

    /// <summary>Menghapus material kunci dari memori (zeroize). Selalu panggil ini setelah selesai memakai kunci.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_keyMaterial);
        _disposed = true;
    }
}
