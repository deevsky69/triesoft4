using System.Buffers.Binary;
using System.Text;

namespace Triesoft.Core.Bundle;

/// <summary>
/// Mengekstrak isi bundle (lihat <see cref="BundleFormat"/>) ke satu folder. Dipanggil HANYA setelah seluruh .ts4
/// lolos autentikasi -- tapi tetap memperlakukan isinya sebagai tidak tepercaya: nama entri divalidasi ketat
/// (<see cref="BundleNames.ValidateEntryName"/>), file tidak pernah menimpa apa pun (<c>CreateNew</c>), jumlah entri dibatasi,
/// dan struktur harus utuh sampai penutup tanpa data tambahan.
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
    /// yang baru dibuat). Melempar <see cref="InvalidDataException"/> kalau bundle rusak atau tidak aman; pemanggil harus
    /// membuang folder tujuan kalau ini terjadi. Mengembalikan nama file yang ditulis.
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
        Span<byte> preamble = stackalloc byte[5];
        input.ReadExactly(preamble);
        if (!preamble[..4].SequenceEqual(BundleFormat.Magic))
            throw new InvalidDataException("Isi file ini bukan bundle TRIESOFT.");
        if (preamble[4] != BundleFormat.Version)
            throw new InvalidDataException($"Versi bundle {preamble[4]} tidak didukung.");

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            if (BundleNames.ValidateEntryName(name) is { } problem)
                throw new InvalidDataException($"Bundle ditolak: nama entri '{name}' tidak aman ({problem}).");
            if (!seen.Add(name))
                throw new InvalidDataException($"Bundle ditolak: nama entri '{name}' muncul dua kali.");

            input.ReadExactly(scratch);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(scratch);
            if (input.CanSeek && size > (ulong)(input.Length - input.Position))
                throw new InvalidDataException($"Bundle terpotong: entri '{name}' lebih besar dari sisa data.");

            // CreateNew: tidak pernah menimpa. Nama sudah divalidasi datar, jadi tidak bisa keluar dari folder tujuan.
            using (var output = new FileStream(Path.Combine(destination, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
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
}
