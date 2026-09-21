using System.Security.Cryptography;

namespace Triesoft.Core.KeyDistribution;

/// <summary>
/// Sidik jari kunci publik: SHA-256 dari SubjectPublicKeyInfo, ditulis heksadesimal berpasangan
/// dipisah titik dua. Dipakai untuk verifikasi manual di luar jalur (telepon/tatap muka) saat
/// mendaftarkan kunci publik Mabes atau Polda -- ini yang menjawab masalah bootstrap kepercayaan.
/// </summary>
public static class KeyFingerprint
{
    public static string Compute(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var hash = SHA256.HashData(subjectPublicKeyInfo);
        return string.Join(':', hash.Select(b => b.ToString("X2")));
    }
}
