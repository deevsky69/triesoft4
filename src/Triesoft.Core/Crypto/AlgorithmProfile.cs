namespace Triesoft.Core.Crypto;

/// <summary>
/// Profil algoritma yang dipakai untuk mengenkripsi isi file dan membungkus DEK.
/// Nilai byte ini ditulis ke header file .ts4 sehingga dekripsi tahu primitif
/// apa yang harus dipakai, dan supaya format bisa berkembang tanpa migrasi total.
/// </summary>
public enum AlgorithmProfile : byte
{
    /// <summary>Profile A — AES-256-GCM. Satu-satunya profil yang diimplementasikan saat ini.</summary>
    AesGcm256 = 0x01,

    /// <summary>Profile B — ChaCha20-Poly1305. Dicadangkan untuk fase berikutnya.</summary>
    ChaCha20Poly1305 = 0x02,

    /// <summary>Profile C — dicadangkan untuk algoritma kriptografi nasional (BSSN).</summary>
    NationalReserved = 0x03,

    /// <summary>Profile D — dicadangkan untuk migrasi post-quantum di masa depan.</summary>
    PostQuantumReserved = 0x04,
}
