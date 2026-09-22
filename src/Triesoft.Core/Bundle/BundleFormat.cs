namespace Triesoft.Core.Bundle;

/// <summary>
/// Format isi bundle (plaintext SEBELUM dienkripsi menjadi .ts4; format .ts4 sendiri tidak berubah). Semua integer little-endian.
/// <code>
/// "TS4B" [versi]                                pembuka
/// (0x01 [u16 panjangNama] [nama UTF-8] [u64 ukuran] [isi])*   satu entri per file
/// 0x00 [u32 jumlahEntri]                        penutup + pemeriksaan jumlah
/// </code>
/// Versi 1: nama entri hanya nama file datar. Versi 2: nama entri boleh berupa path bersubfolder dengan pemisah "/"
/// (mis. <c>Laporan/sub/data.xlsx</c>), aturan tiap komponen sama dengan versi 1 (lihat <see cref="BundleNames"/>).
/// Penulis memilih versi terendah yang cukup: bundle yang isinya datar tetap versi 1 supaya bisa dibuka build lama.
/// Folder kosong tidak dimuat (yang tersimpan hanya file). Pembaca menerima kedua versi.
/// </summary>
internal static class BundleFormat
{
    public static readonly byte[] Magic = "TS4B"u8.ToArray();

    public const byte VersionFlat = 1;
    public const byte VersionSubfolders = 2;

    public const byte EntryTag = 0x01;
    public const byte EndTag = 0x00;

    public const int MaxEntries = BundleNames.MaxEntries;

    /// <summary>Batas kasar panjang nama dalam byte (UTF-8 maksimum 4 byte per karakter); cukup untuk u16.</summary>
    public const int MaxNameBytes = BundleNames.MaxPathChars * 4;
}
