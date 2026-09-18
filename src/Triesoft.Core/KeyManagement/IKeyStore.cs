using Triesoft.Core.Crypto;

namespace Triesoft.Core.KeyManagement;

/// <summary>
/// Penyimpanan mentah untuk kunci bulanan: metadata + material terproteksi. Tidak menegakkan
/// kebijakan siklus hidup (satu Active, dsb) -- itu tanggung jawab <see cref="MonthlyKeyManager"/>.
/// </summary>
public interface IKeyStore
{
    /// <summary>Menyimpan kunci baru sebagai entri Active. Melempar exception kalau KeyId sudah pernah dipakai (termasuk yang sudah Purged/Revoked).</summary>
    void Save(MonthlyKey key, DateTimeOffset validFrom, DateTimeOffset validUntil);

    /// <summary>Memuat material kunci. Melempar exception kalau file material tidak ada (sudah dihapus lewat Revoke/Purge).</summary>
    MonthlyKey LoadKeyMaterial(string keyId);

    KeyMetadata? GetMetadata(string keyId);

    IReadOnlyList<KeyMetadata> ListMetadata();

    /// <summary>Menimpa entri metadata (dipakai untuk transisi status).</summary>
    void UpdateMetadata(KeyMetadata metadata);

    /// <summary>Menghapus file material saja. Entri metadata (tombstone) tetap ada untuk audit.</summary>
    void DeleteMaterial(string keyId);
}
