namespace Triesoft.Core.Bundle;

/// <summary>
/// Format isi bundle (plaintext SEBELUM dienkripsi menjadi .ts4; format .ts4 sendiri tidak berubah). Semua integer little-endian.
/// <code>
/// "TS4B" 0x01                                   pembuka + versi
/// (0x01 [u16 panjangNama] [nama UTF-8] [u64 ukuran] [isi])*   satu entri per file
/// 0x00 [u32 jumlahEntri]                        penutup + pemeriksaan jumlah
/// </code>
/// Versi 1 hanya mengenal nama file datar (tanpa subfolder); lihat <see cref="BundleNames"/>.
/// </summary>
internal static class BundleFormat
{
    public static readonly byte[] Magic = "TS4B"u8.ToArray();
    public const byte Version = 1;
    public const byte EntryTag = 0x01;
    public const byte EndTag = 0x00;

    public const int MaxEntries = 100_000;
    public const int MaxNameBytes = BundleNames.MaxEntryNameChars * 4;
}
