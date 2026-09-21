using System.Text.Json;

namespace Triesoft.Core.KeyDistribution;

public enum PublicKeyKind
{
    /// <summary>Kunci publik ECDH milik penerima (Polda) -- dipakai Mabes untuk membungkus kunci bulanan.</summary>
    Recipient,

    /// <summary>Kunci publik ECDSA milik penerbit (Mabes) -- dipakai Polda untuk memverifikasi tanda tangan paket.</summary>
    Issuer,
}

/// <summary>
/// Isi file <c>.ts4pub</c>: kunci publik + nama pemiliknya. Bukan rahasia, tapi keasliannya HARUS
/// diverifikasi lewat sidik jari (<see cref="Fingerprint"/>) melalui jalur lain sebelum dipercaya.
/// Sidik jari selalu dihitung ulang dari kunci, tidak pernah dibaca dari file.
/// </summary>
public sealed record PublicKeyFile(PublicKeyKind Kind, string Name, byte[] PublicKey)
{
    public string Fingerprint => KeyFingerprint.Compute(PublicKey);

    public string ToJson() => JsonSerializer.Serialize(this);

    public static PublicKeyFile Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<PublicKeyFile>(json);
            if (file is null || string.IsNullOrWhiteSpace(file.Name) || file.PublicKey is not { Length: > 0 })
                throw new KeyDistributionException("File kunci publik tidak valid.");
            return file;
        }
        catch (JsonException ex)
        {
            throw new KeyDistributionException("File kunci publik tidak valid.", ex);
        }
    }
}
