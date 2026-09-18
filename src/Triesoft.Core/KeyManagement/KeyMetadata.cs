namespace Triesoft.Core.KeyManagement;

public enum KeyStatus
{
    /// <summary>Kunci bulan berjalan -- boleh dipakai untuk enkripsi file baru.</summary>
    Active,

    /// <summary>Kunci bulan lalu -- tidak lagi dipakai untuk enkripsi baru, tapi tetap bisa mendekripsi arsip lama.</summary>
    Expired,

    /// <summary>Dicabut paksa (mis. dicurigai bocor). Material sudah dihapus permanen -- tidak bisa dipakai sama sekali.</summary>
    Revoked,

    /// <summary>Sudah melewati masa retensi dan dihapus permanen lewat proses normal (bukan pencabutan darurat).</summary>
    Purged,
}

/// <summary>Metadata satu kunci bulanan. Tidak berisi material kunci itu sendiri.</summary>
public sealed record KeyMetadata(
    string KeyId,
    KeyStatus Status,
    DateTimeOffset ImportedAt,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    string? StatusReason = null);
