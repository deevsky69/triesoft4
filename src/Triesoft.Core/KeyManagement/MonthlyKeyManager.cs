using Triesoft.Core.Crypto;

namespace Triesoft.Core.KeyManagement;

/// <summary>
/// Kebijakan siklus hidup kunci bulanan di atas <see cref="IKeyStore"/>: hanya satu kunci Active
/// pada satu waktu, kunci lama tetap bisa dipakai untuk dekripsi arsip sampai di-Revoke/Purge.
/// </summary>
public sealed class MonthlyKeyManager(IKeyStore keyStore)
{
    public void ImportMonthlyKey(string keyId, ReadOnlySpan<byte> keyMaterial, DateTimeOffset validFrom, DateTimeOffset validUntil)
    {
        if (validUntil <= validFrom)
            throw new ArgumentException("ValidUntil harus setelah ValidFrom.", nameof(validUntil));
        if (keyStore.GetMetadata(keyId) is not null)
            throw new ArgumentException($"KeyId '{keyId}' sudah pernah dipakai.", nameof(keyId));

        foreach (var existing in keyStore.ListMetadata())
        {
            if (existing.Status == KeyStatus.Active)
                keyStore.UpdateMetadata(existing with { Status = KeyStatus.Expired });
        }

        using var key = MonthlyKey.FromBytes(keyId, keyMaterial);
        keyStore.Save(key, validFrom, validUntil);
    }

    public MonthlyKey GetActiveKeyForEncryption()
    {
        var activeKeys = keyStore.ListMetadata().Where(k => k.Status == KeyStatus.Active).ToList();
        if (activeKeys.Count == 0)
        {
            throw new InvalidOperationException(
                "Tidak ada kunci aktif untuk enkripsi -- hubungi Bidsandi Mabes untuk kunci bulan ini.");
        }
        if (activeKeys.Count > 1)
        {
            throw new InvalidOperationException(
                $"Ditemukan {activeKeys.Count} kunci Active sekaligus -- state key store tidak konsisten.");
        }

        return keyStore.LoadKeyMaterial(activeKeys[0].KeyId);
    }

    public MonthlyKey GetKeyForDecryption(string keyId)
    {
        var metadata = keyStore.GetMetadata(keyId)
            ?? throw new KeyNotFoundException($"Kunci dengan KeyId '{keyId}' tidak ditemukan di key store ini.");

        switch (metadata.Status)
        {
            case KeyStatus.Revoked:
                throw new InvalidOperationException(
                    $"Kunci '{keyId}' telah dicabut (revoked){(metadata.StatusReason is { } r ? $": {r}" : "")} dan tidak bisa dipakai lagi.");
            case KeyStatus.Purged:
                throw new InvalidOperationException(
                    $"Kunci '{keyId}' sudah dihapus permanen (purged) -- arsip yang dienkripsi dengan kunci ini tidak bisa lagi didekripsi.");
        }

        return keyStore.LoadKeyMaterial(keyId);
    }

    public IReadOnlyList<KeyMetadata> ListKeys() => keyStore.ListMetadata();

    public void RevokeKey(string keyId, string reason)
    {
        var metadata = keyStore.GetMetadata(keyId)
            ?? throw new KeyNotFoundException($"Kunci dengan KeyId '{keyId}' tidak ditemukan di key store ini.");
        if (metadata.Status is KeyStatus.Revoked or KeyStatus.Purged)
            throw new InvalidOperationException($"Kunci '{keyId}' sudah berstatus {metadata.Status}, tidak bisa dicabut ulang.");

        keyStore.UpdateMetadata(metadata with { Status = KeyStatus.Revoked, StatusReason = reason });
        keyStore.DeleteMaterial(keyId);
    }

    public void PurgeExpiredKey(string keyId)
    {
        var metadata = keyStore.GetMetadata(keyId)
            ?? throw new KeyNotFoundException($"Kunci dengan KeyId '{keyId}' tidak ditemukan di key store ini.");
        if (metadata.Status != KeyStatus.Expired)
        {
            throw new InvalidOperationException(
                $"Kunci '{keyId}' berstatus {metadata.Status}, hanya kunci Expired yang boleh di-purge.");
        }

        keyStore.UpdateMetadata(metadata with { Status = KeyStatus.Purged });
        keyStore.DeleteMaterial(keyId);
    }
}
