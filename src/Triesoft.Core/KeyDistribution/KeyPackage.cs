using System.Text.Json;

namespace Triesoft.Core.KeyDistribution;

/// <summary>
/// Paket kunci bulanan untuk satu Polda (file <c>.ts4kp</c>): kunci bulanan (KEK) dibungkus dengan
/// kunci publik Polda, lalu seluruh paket ditandatangani Mabes. Paket boleh lewat saluran tidak
/// tepercaya (email, USB) -- isinya tidak berguna bagi siapa pun selain penerima yang dituju.
/// </summary>
public sealed record KeyPackage(
    int Version,
    string KeyId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset IssuedAt,
    string IssuerFingerprint,
    string RecipientFingerprint,
    byte[] EphemeralPublicKey,
    byte[] Nonce,
    byte[] WrappedKey,
    byte[] Tag,
    byte[] Signature)
{
    public const int CurrentVersion = 1;

    private static readonly byte[] Magic = "TS4KP"u8.ToArray();

    /// <summary>Bagian yang diikat sebagai AAD pembungkusan dan info HKDF. Tidak termasuk ciphertext dan tanda tangan.</summary>
    internal byte[] HeaderBytes()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(Version);
        WriteField(w, KeyId);
        w.Write(ValidFrom.UtcTicks);
        w.Write(ValidUntil.UtcTicks);
        w.Write(IssuedAt.UtcTicks);
        WriteField(w, IssuerFingerprint);
        WriteField(w, RecipientFingerprint);
        WriteField(w, EphemeralPublicKey);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Bagian yang ditandatangani Mabes: header + nonce + ciphertext + tag.</summary>
    internal byte[] SignedBytes()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteField(w, HeaderBytes());
        WriteField(w, Nonce);
        WriteField(w, WrappedKey);
        WriteField(w, Tag);
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteField(BinaryWriter w, string value) => WriteField(w, System.Text.Encoding.UTF8.GetBytes(value));

    private static void WriteField(BinaryWriter w, byte[] value)
    {
        w.Write(value.Length);
        w.Write(value);
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static KeyPackage Parse(string json)
    {
        try
        {
            var package = JsonSerializer.Deserialize<KeyPackage>(json);
            if (package is null
                || string.IsNullOrWhiteSpace(package.KeyId)
                || package.EphemeralPublicKey is null || package.Nonce is null || package.WrappedKey is null
                || package.Tag is null || package.Signature is null
                || package.IssuerFingerprint is null || package.RecipientFingerprint is null)
            {
                throw new KeyDistributionException("File paket kunci tidak valid.");
            }
            return package;
        }
        catch (JsonException ex)
        {
            throw new KeyDistributionException("File paket kunci tidak valid.", ex);
        }
    }
}
