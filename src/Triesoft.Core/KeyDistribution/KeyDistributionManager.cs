using System.Security.Cryptography;
using Triesoft.Core.KeyManagement;

namespace Triesoft.Core.KeyDistribution;

/// <summary>
/// Orkestrasi distribusi kunci di atas <see cref="FileDistributionStore"/> dan <see cref="MonthlyKeyManager"/>:
/// sisi Mabes menerbitkan paket per Polda, sisi Polda memverifikasi dan mengimpor paket ke key store.
/// </summary>
public sealed class KeyDistributionManager(FileDistributionStore store, MonthlyKeyManager keyManager)
{
    /// <summary>
    /// Sisi Mabes: membangkitkan kunci bulanan acak baru dan membuat satu paket per penerima.
    /// Kalau <paramref name="importLocally"/> true, kunci yang sama juga diimpor ke key store mesin ini
    /// (mesin Mabes ikut bisa mengenkripsi/mendekripsi). Kunci di memori selalu di-zeroize.
    /// </summary>
    public IReadOnlyList<(TrustedParty Recipient, KeyPackage Package)> IssueMonthlyKey(
        string keyId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        IReadOnlyCollection<string> recipientFingerprints,
        bool importLocally)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID wajib diisi.", nameof(keyId));
        if (validUntil <= validFrom)
            throw new ArgumentException("Tanggal berakhir harus setelah tanggal mulai.", nameof(validUntil));
        if (recipientFingerprints.Count == 0)
            throw new ArgumentException("Pilih minimal satu penerima.", nameof(recipientFingerprints));
        if (keyManager.ListKeys().Any(k => k.KeyId == keyId))
            throw new ArgumentException($"KeyId '{keyId}' sudah pernah dipakai.", nameof(keyId));

        var registered = store.ListRecipients();
        var recipients = recipientFingerprints
            .Select(fp => registered.FirstOrDefault(r => r.Fingerprint == fp)
                ?? throw new KeyNotFoundException("Penerima yang dipilih tidak terdaftar."))
            .ToList();

        using var issuerKey = store.LoadIssuerKey();
        var issuerPublic = issuerKey.ExportSubjectPublicKeyInfo();
        var monthlyKey = RandomNumberGenerator.GetBytes(Crypto.MonthlyKey.KeySizeInBytes);
        try
        {
            var packages = recipients
                .Select(r => (r, KeyPackageCrypto.Create(keyId, monthlyKey, validFrom, validUntil, issuerKey, issuerPublic, r.PublicKey)))
                .ToList();

            if (importLocally)
                keyManager.ImportMonthlyKey(keyId, monthlyKey, validFrom, validUntil);

            return packages;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(monthlyKey);
        }
    }

    /// <summary>Sisi Polda: memverifikasi paket terhadap penerbit tepercaya, membukanya, dan mengimpor kuncinya sebagai Active.</summary>
    public KeyPackage ImportPackage(KeyPackage package)
    {
        // Dicek sebelum "penerbit tepercaya": pencabutan menghapus penerbit tepercaya, dan pesannya harus menjelaskan sebabnya.
        // Ini hanya untuk pesan yang jelas -- keamanan tetap dari pencocokan sidik jari dan verifikasi tanda tangan di Open.
        if (store.ListRevoked().FirstOrDefault(r => r.Kind == PublicKeyKind.Issuer && r.Fingerprint == package.IssuerFingerprint) is { } revoked)
        {
            throw new KeyDistributionException(
                $"Paket berasal dari penerbit yang kuncinya sudah dicabut ({revoked.Reason}). Hubungi Bidsandi Mabes untuk kunci publik penerbit yang baru.");
        }

        var issuer = store.GetTrustedIssuer()
            ?? throw new KeyDistributionException("Belum ada penerbit tepercaya -- daftarkan kunci publik Mabes terlebih dahulu.");

        using var recipientKey = store.LoadRecipientKey();
        var monthlyKey = KeyPackageCrypto.Open(package, recipientKey, recipientKey.ExportSubjectPublicKeyInfo(), issuer.PublicKey);
        try
        {
            keyManager.ImportMonthlyKey(package.KeyId, monthlyKey, package.ValidFrom, package.ValidUntil);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(monthlyKey);
        }
        return package;
    }
}
