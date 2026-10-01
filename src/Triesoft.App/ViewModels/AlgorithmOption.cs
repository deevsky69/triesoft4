using Triesoft.Core.Crypto;

namespace Triesoft.App.ViewModels;

/// <summary>Satu pilihan algoritma di ComboBox enkripsi: nilai enum + label dan penjelasan singkat untuk operator non-teknis.</summary>
public sealed record AlgorithmOption(AlgorithmProfile Profile, string Name, string Description)
{
    public static readonly AlgorithmOption AesGcm256 = new(
        AlgorithmProfile.AesGcm256,
        "AES-256-GCM (bawaan)",
        "Standar NIST yang dipakai luas di seluruh dunia. Pilihan default -- pakai ini kecuali ada alasan khusus untuk memilih yang lain.");

    public static readonly AlgorithmOption ChaCha20Poly1305 = new(
        AlgorithmProfile.ChaCha20Poly1305,
        "ChaCha20-Poly1305",
        "Algoritma alternatif modern (RFC 8439) dengan konstruksi berbeda dari AES. Berguna sebagai cadangan kalau suatu saat AES perlu dihindari, dan cepat di perangkat tanpa akselerasi AES perangkat keras.");

    public static readonly IReadOnlyList<AlgorithmOption> All = [AesGcm256, ChaCha20Poly1305];

    public static AlgorithmOption For(AlgorithmProfile profile) =>
        All.FirstOrDefault(o => o.Profile == profile) ?? AesGcm256;

    public override string ToString() => Name;
}
