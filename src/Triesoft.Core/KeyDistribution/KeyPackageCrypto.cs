using System.Security.Cryptography;

namespace Triesoft.Core.KeyDistribution;

/// <summary>
/// Kriptografi paket kunci: ECDH P-384 efemeral (ECIES) -> HKDF-SHA384 -> AES-256-GCM untuk membungkus
/// kunci bulanan, lalu ECDSA P-384 (SHA-384) dari Mabes atas seluruh paket. Semuanya bawaan .NET.
/// </summary>
public static class KeyPackageCrypto
{
    private const string P384Oid = "1.3.132.0.34";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static KeyPackage Create(
        string keyId,
        ReadOnlySpan<byte> monthlyKey,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        ECDsa issuerKey,
        byte[] issuerPublicKey,
        byte[] recipientPublicKey)
    {
        if (monthlyKey.Length != Crypto.MonthlyKey.KeySizeInBytes)
            throw new ArgumentException("Kunci bulanan harus 32 byte.", nameof(monthlyKey));

        using var recipient = ImportEcdhPublicKey(recipientPublicKey);
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var ephemeralPublic = ephemeral.ExportSubjectPublicKeyInfo();

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var draft = new KeyPackage(
            KeyPackage.CurrentVersion, keyId, validFrom, validUntil, DateTimeOffset.UtcNow,
            KeyFingerprint.Compute(issuerPublicKey), KeyFingerprint.Compute(recipientPublicKey),
            ephemeralPublic, nonce, [], [], []);
        var header = draft.HeaderBytes();

        var wrappingKey = DeriveWrappingKey(ephemeral.DeriveRawSecretAgreement(recipient.PublicKey), header);
        var wrapped = new byte[monthlyKey.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Encrypt(nonce, monthlyKey, wrapped, tag, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        var unsigned = draft with { WrappedKey = wrapped, Tag = tag };
        return unsigned with { Signature = issuerKey.SignData(unsigned.SignedBytes(), HashAlgorithmName.SHA384) };
    }

    /// <summary>
    /// Memverifikasi dan membuka paket. Urutan pemeriksaan disengaja: penerbit dan tanda tangan dulu,
    /// baru penerima, baru dekripsi -- paket palsu ditolak sebelum kunci privat dipakai.
    /// Pemanggil wajib men-zeroize hasilnya.
    /// </summary>
    public static byte[] Open(
        KeyPackage package,
        ECDiffieHellman recipientKey,
        byte[] recipientPublicKey,
        byte[] trustedIssuerPublicKey)
    {
        if (package.Version != KeyPackage.CurrentVersion)
            throw new KeyDistributionException($"Versi paket kunci {package.Version} tidak didukung.");
        if (package.IssuerFingerprint != KeyFingerprint.Compute(trustedIssuerPublicKey))
            throw new KeyDistributionException("Paket berasal dari penerbit yang tidak dipercaya mesin ini.");

        using (var issuer = ImportEcdsaPublicKey(trustedIssuerPublicKey))
        {
            if (!issuer.VerifyData(package.SignedBytes(), package.Signature, HashAlgorithmName.SHA384))
                throw new KeyDistributionException("Tanda tangan paket tidak valid -- paket rusak atau dipalsukan.");
        }

        if (package.RecipientFingerprint != KeyFingerprint.Compute(recipientPublicKey))
            throw new KeyDistributionException("Paket ini ditujukan untuk mesin lain, bukan mesin ini.");
        if (package.Nonce.Length != NonceSize || package.Tag.Length != TagSize
            || package.WrappedKey.Length != Crypto.MonthlyKey.KeySizeInBytes)
        {
            throw new KeyDistributionException("Struktur paket kunci tidak valid.");
        }

        var header = package.HeaderBytes();
        using var ephemeral = ImportEcdhPublicKey(package.EphemeralPublicKey);
        var wrappingKey = DeriveWrappingKey(recipientKey.DeriveRawSecretAgreement(ephemeral.PublicKey), header);
        var monthlyKey = new byte[Crypto.MonthlyKey.KeySizeInBytes];
        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(package.Nonce, package.WrappedKey, package.Tag, monthlyKey, header);
            return monthlyKey;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(monthlyKey);
            throw new KeyDistributionException("Paket kunci gagal dibuka -- data rusak.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private static byte[] DeriveWrappingKey(byte[] sharedSecret, byte[] info)
    {
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA384, sharedSecret, 32, salt: null, info: info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    internal static ECDiffieHellman ImportEcdhPublicKey(byte[] subjectPublicKeyInfo)
    {
        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            RequireP384(key.ExportParameters(false).Curve);
            return key;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key.Dispose();
            throw new KeyDistributionException("Kunci publik ECDH tidak valid (harus P-384).", ex);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    internal static ECDsa ImportEcdsaPublicKey(byte[] subjectPublicKeyInfo)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            RequireP384(key.ExportParameters(false).Curve);
            return key;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key.Dispose();
            throw new KeyDistributionException("Kunci publik ECDSA tidak valid (harus P-384).", ex);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static void RequireP384(ECCurve curve)
    {
        if (curve.Oid?.Value != P384Oid)
            throw new CryptographicException("Kurva bukan P-384.");
    }
}
