using System.Buffers.Binary;
using System.Text;

namespace Triesoft.Core.Bundle;

/// <summary>
/// Mengekstrak isi bundle (lihat <see cref="BundleFormat"/>) ke satu folder. Dipanggil HANYA setelah seluruh .ts4
/// lolos autentikasi -- tapi tetap memperlakukan isinya sebagai tidak tepercaya: path entri divalidasi ketat per komponen
/// (<see cref="BundleNames.ValidateEntryPath"/>; versi 1 hanya menerima nama datar), file tidak pernah menimpa apa pun
/// (<c>CreateNew</c>), jumlah entri dan kedalaman dibatasi, nama tidak boleh sekaligus file dan folder, dan struktur harus utuh
/// sampai penutup tanpa data tambahan.
/// </summary>
public static class BundleExtractor
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Apakah stream (yang bisa di-seek) diawali pembuka bundle. Posisi dikembalikan seperti semula. Dipakai untuk tidak salah
    /// mengekstrak file biasa yang kebetulan bernama <c>*.ts4bundle</c> (mis. dibuat versi lama): file itu dikembalikan apa adanya.
    /// </summary>
    public static bool LooksLikeBundle(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var start = input.Position;
        try
        {
            Span<byte> head = stackalloc byte[4];
            return input.Read(head) == 4 && head.SequenceEqual(BundleFormat.Magic);
        }
        finally
        {
            input.Position = start;
        }
    }

    /// <summary>
    /// Menulis tiap entri sebagai file di <paramref name="destinationDirectory"/> (harus sudah ada, sebaiknya folder kosong
    /// yang baru dibuat), membuat subfolder yang dibutuhkan. Melempar <see cref="InvalidDataException"/> kalau bundle rusak
    /// atau tidak aman; pemanggil harus membuang folder tujuan kalau ini terjadi. Mengembalikan path relatif ("/") file yang ditulis.
    /// </summary>
    public static IReadOnlyList<string> Extract(Stream input, string destinationDirectory, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return ExtractCore(input, destinationDirectory, cancellation);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Bundle terpotong atau rusak.", ex);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Bundle berisi nama entri yang tidak valid.", ex);
        }
    }

    private static List<string> ExtractCore(Stream input, string destination, CancellationToken cancellation)
    {
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));

        Span<byte> preamble = stackalloc byte[5];
        input.ReadExactly(preamble);
        if (!preamble[..4].SequenceEqual(BundleFormat.Magic))
            throw new InvalidDataException("Isi file ini bukan bundle TRIESOFT.");
        var version = preamble[4];
        if (version is not (BundleFormat.VersionFlat or BundleFormat.VersionSubfolders))
            throw new InvalidDataException($"Versi bundle {version} tidak didukung.");
        var allowSubfolders = version == BundleFormat.VersionSubfolders;

        var names = new List<string>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[81920];

        // Dialokasikan sekali di luar loop (CA2014: stackalloc di dalam loop bisa stack overflow untuk bundle besar).
        Span<byte> scratch = stackalloc byte[8];

        while (true)
        {
            cancellation.ThrowIfCancellationRequested();

            var tag = input.ReadByte();
            if (tag < 0) throw new InvalidDataException("Bundle terpotong: tidak ada penutup.");

            if (tag == BundleFormat.EndTag)
            {
                input.ReadExactly(scratch[..4]);
                if (BinaryPrimitives.ReadUInt32LittleEndian(scratch) != (uint)names.Count)
                    throw new InvalidDataException("Jumlah entri bundle tidak cocok dengan penutupnya.");
                if (input.ReadByte() != -1)
                    throw new InvalidDataException("Ada data tambahan setelah akhir bundle.");
                return names;
            }

            if (tag != BundleFormat.EntryTag)
                throw new InvalidDataException("Bundle rusak: penanda entri tidak dikenal.");
            if (names.Count >= BundleFormat.MaxEntries)
                throw new InvalidDataException("Bundle berisi terlalu banyak file.");

            input.ReadExactly(scratch[..2]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(scratch);
            if (nameLength == 0 || nameLength > BundleFormat.MaxNameBytes)
                throw new InvalidDataException("Bundle rusak: panjang nama entri tidak wajar.");

            var nameBytes = new byte[nameLength];
            input.ReadExactly(nameBytes);
            var name = StrictUtf8.GetString(nameBytes);

            // Versi 1 = nama datar saja; versi 2 = path bersubfolder. Aturan tiap komponen sama.
            var problem = allowSubfolders ? BundleNames.ValidateEntryPath(name) : BundleNames.ValidateEntryName(name);
            if (problem is not null)
                throw new InvalidDataException($"Bundle ditolak: nama entri '{name}' tidak aman ({problem}).");
            if (!files.Add(name))
                throw new InvalidDataException($"Bundle ditolak: nama entri '{name}' muncul dua kali.");
            RegisterFolders(name, files, folders);

            input.ReadExactly(scratch);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(scratch);
            if (input.CanSeek && size > (ulong)(input.Length - input.Position))
                throw new InvalidDataException($"Bundle terpotong: entri '{name}' lebih besar dari sisa data.");

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            // Pertahanan berlapis: nama sudah divalidasi, tapi hasil akhirnya tetap harus berada di bawah folder tujuan.
            if (!targetPath.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Bundle ditolak: nama entri '{name}' keluar dari folder tujuan.");

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            // CreateNew: tidak pernah menimpa. Folder tujuan baru dan kosong, jadi setiap file yang sudah ada berarti masalah.
            using (var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var remaining = size;
                while (remaining > 0)
                {
                    var read = input.Read(buffer, 0, (int)Math.Min((ulong)buffer.Length, remaining));
                    if (read == 0) throw new InvalidDataException($"Bundle terpotong di tengah entri '{name}'.");
                    output.Write(buffer, 0, read);
                    remaining -= (ulong)read;
                }
            }

            names.Add(name);
        }
    }

    /// <summary>
    /// Mencatat folder induk sebuah path dan menolak konflik: sebuah nama tidak boleh sekaligus file dan folder
    /// ("a" lalu "a/b.txt", atau "a/b.txt" lalu "a"). Tanpa ini ekstraksi gagal dengan error IO yang membingungkan.
    /// </summary>
    private static void RegisterFolders(string path, HashSet<string> files, HashSet<string> folders)
    {
        if (folders.Contains(path))
            throw new InvalidDataException($"Bundle ditolak: '{path}' dipakai sebagai nama file sekaligus nama folder.");

        var slash = path.LastIndexOf('/');
        while (slash > 0)
        {
            var parent = path[..slash];
            if (files.Contains(parent))
                throw new InvalidDataException($"Bundle ditolak: '{parent}' dipakai sebagai nama file sekaligus nama folder.");
            folders.Add(parent);
            slash = path.LastIndexOf('/', slash - 1);
        }
    }
}
