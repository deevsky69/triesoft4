using System.Buffers.Binary;
using System.Text;
using Triesoft.Core.Bundle;
using Triesoft.Core.Crypto;
using Xunit;

namespace Triesoft.Core.Tests.Bundle;

public class BundleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("triesoft4-bundle-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string NewDestination()
    {
        var dir = Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static byte[] Bytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>Merakit bundle mentah secara manual, supaya bisa dibuat versi yang sengaja rusak atau berbahaya.</summary>
    private static byte[] Raw(IEnumerable<(string Name, byte[] Data)> entries, bool trailer = true, uint? count = null, byte version = 1, byte[]? extra = null)
    {
        using var ms = new MemoryStream();
        ms.Write("TS4B"u8);
        ms.WriteByte(version);
        var n = 0u;
        foreach (var (name, data) in entries)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            ms.WriteByte(0x01);
            var len = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)nameBytes.Length);
            ms.Write(len);
            ms.Write(nameBytes);
            var size = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(size, (ulong)data.Length);
            ms.Write(size);
            ms.Write(data);
            n++;
        }
        if (trailer)
        {
            ms.WriteByte(0x00);
            var c = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(c, count ?? n);
            ms.Write(c);
        }
        if (extra is not null) ms.Write(extra);
        return ms.ToArray();
    }

    private IReadOnlyList<string> Extract(byte[] bundle, out string destination)
    {
        destination = NewDestination();
        return BundleExtractor.Extract(new MemoryStream(bundle), destination);
    }

    // --- pembuatan dan ekstraksi ---------------------------------------------------------------------

    [Fact]
    public void ReadStream_ProducesExactlyLengthBytes_AndRoundTripsThroughExtractor()
    {
        var a = Bytes(1000, 1);
        var empty = Array.Empty<byte>();
        var big = Bytes(300_000, 2);
        var sources = new[]
        {
            new BundleSource(WriteFile("a.bin", a), "a.bin"),
            new BundleSource(WriteFile("kosong.txt", empty), "kosong.txt"),
            new BundleSource(WriteFile("besar.bin", big), "besar.bin"),
        };

        using var stream = new BundleReadStream(sources);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        Assert.Equal(stream.Length, ms.Length);
        Assert.Equal(stream.Length, stream.Position);

        var names = Extract(ms.ToArray(), out var dest);
        Assert.Equal(["a.bin", "kosong.txt", "besar.bin"], names);
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(dest, "a.bin")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(dest, "kosong.txt")));
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(dest, "besar.bin")));
    }

    [Fact]
    public void FullPipeline_EncryptBundle_Decrypt_Extract_MultiChunk()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["satu.pdf"] = Bytes(1_500_000, 10),  // > 1 chunk (1 MiB)
            ["dua.txt"] = Bytes(10, 11),
            ["tiga.docx"] = Bytes(2_100_000, 12), // > 2 chunk
        };
        var sources = files.Select(f => new BundleSource(WriteFile(f.Key, f.Value), f.Key)).ToList();
        using var kek = MonthlyKey.FromBytes("2026-09-TEST", new byte[32]);

        using var encrypted = new MemoryStream();
        var progress = new List<double>();
        using (var input = new BundleReadStream(sources))
            EnvelopeCipher.Encrypt(input, encrypted, kek, "paket.ts4bundle", progress: new SyncProgress(progress));
        Assert.Equal(1.0, progress[^1]);
        Assert.True(progress.SequenceEqual(progress.Order()), "progres tidak boleh mundur");

        encrypted.Position = 0;
        using var plain = new MemoryStream();
        var result = EnvelopeCipher.Decrypt(encrypted, plain, kek);
        Assert.Equal("paket.ts4bundle", result.OriginalFileName);
        Assert.True(BundleNames.IsBundleFileName(result.OriginalFileName));

        plain.Position = 0;
        var dest = NewDestination();
        var names = BundleExtractor.Extract(plain, dest);
        Assert.Equal(files.Keys, names);
        foreach (var (name, content) in files)
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(dest, name)));
    }

    private sealed class SyncProgress(List<double> sink) : IProgress<double>
    {
        public void Report(double value) => sink.Add(value);
    }

    [Fact]
    public void ReadStream_SmallReads_GiveSameBytesAsBulkRead()
    {
        var sources = new[]
        {
            new BundleSource(WriteFile("x.bin", Bytes(5000, 3)), "x.bin"),
            new BundleSource(WriteFile("y.bin", Bytes(7, 4)), "y.bin"),
        };
        using var bulk = new BundleReadStream(sources);
        using var bulkMs = new MemoryStream();
        bulk.CopyTo(bulkMs);

        using var tiny = new BundleReadStream(sources);
        var small = new List<byte>();
        var one = new byte[3];
        int n;
        while ((n = tiny.Read(one, 0, one.Length)) > 0) small.AddRange(one.Take(n));

        Assert.Equal(bulkMs.ToArray(), small.ToArray());
    }

    [Fact]
    public void ReadStream_FileShrinksWhileReading_Throws()
    {
        var path = WriteFile("menyusut.bin", Bytes(200_000, 5));
        using var stream = new BundleReadStream([new BundleSource(path, "menyusut.bin")]);
        var buffer = new byte[1000];
        stream.ReadExactly(buffer); // file terbuka, ukuran 200000 sudah dicatat di header

        try
        {
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer.SetLength(10);
        }
        catch (IOException)
        {
            // Windows: file dibuka dengan FileShare.Read sehingga pihak lain memang tidak bisa memodifikasinya selama
            // dibaca -- perlindungan yang lebih baik daripada mendeteksi perubahan setelah terjadi.
            return;
        }

        // OS lain (mis. Linux) mengizinkan file dipangkas saat dibaca: harus terdeteksi, bukan menghasilkan bundle terpotong.
        Assert.Throws<IOException>(() => stream.CopyTo(Stream.Null));
    }

    [Fact]
    public void ReadStream_CancellationStopsReading()
    {
        using var cts = new CancellationTokenSource();
        using var stream = new BundleReadStream([new BundleSource(WriteFile("c.bin", Bytes(5000, 6)), "c.bin")], cts.Token);
        stream.ReadExactly(new byte[10]);

        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => stream.Read(new byte[10], 0, 10));
    }

    [Fact]
    public void ReadStream_RejectsBadSources_AndUnsupportedOperations()
    {
        var path = WriteFile("ok.txt", [1]);
        Assert.Throws<ArgumentException>(() => new BundleReadStream([]));
        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(path, "../x")]));
        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(path, "a.txt"), new BundleSource(path, "A.TXT")]));

        using var stream = new BundleReadStream([new BundleSource(path, "ok.txt")]);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
    }

    // --- ekstraksi bundle yang rusak atau berbahaya ------------------------------------------------------

    [Theory]
    [InlineData("../keluar.txt")]
    [InlineData("..\\keluar.txt")]
    [InlineData("sub/a.txt")]
    [InlineData("sub\\a.txt")]
    [InlineData("C:\\Windows\\x.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("a:stream")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT1.log")]
    [InlineData("akhir.")]
    [InlineData("akhir ")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("bintang*.txt")]
    public void Extract_UnsafeEntryName_IsRejected_AndWritesNothing(string name)
    {
        var bundle = Raw([(name, [1, 2, 3])]);

        var dest = NewDestination();
        var ex = Assert.Throws<InvalidDataException>(() => BundleExtractor.Extract(new MemoryStream(bundle), dest));

        Assert.Contains("tidak aman", ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(dest));
        Assert.False(File.Exists(Path.Combine(_root, "keluar.txt")));
    }

    [Fact]
    public void Extract_DuplicateNames_CaseInsensitive_AreRejected()
    {
        var bundle = Raw([("a.txt", [1]), ("A.TXT", [2])]);
        var ex = Assert.Throws<InvalidDataException>(() => Extract(bundle, out _));
        Assert.Contains("dua kali", ex.Message);
    }

    [Fact]
    public void Extract_StructuralDamage_IsRejected()
    {
        var good = Raw([("a.txt", Bytes(100, 7))]);

        Assert.Throws<InvalidDataException>(() => Extract(good[..^3], out _));                 // penutup terpotong
        Assert.Throws<InvalidDataException>(() => Extract(good[..40], out _));                 // terpotong di tengah isi
        Assert.Throws<InvalidDataException>(() => Extract(Raw([("a.txt", [1])], trailer: false), out _)); // tanpa penutup
        Assert.Throws<InvalidDataException>(() => Extract(Raw([("a.txt", [1])], count: 2), out _));       // jumlah tidak cocok
        Assert.Throws<InvalidDataException>(() => Extract(Raw([("a.txt", [1])], extra: [9]), out _));     // data tambahan
        Assert.Throws<InvalidDataException>(() => Extract(Raw([("a.txt", [1])], version: 3), out _));     // versi asing
        Assert.Throws<InvalidDataException>(() => Extract("bukan bundle sama sekali"u8.ToArray(), out _));
        Assert.Throws<InvalidDataException>(() => Extract([], out _));
    }

    [Fact]
    public void Extract_SizeLargerThanRemainingData_IsRejectedEarly()
    {
        var raw = Raw([("a.txt", [1, 2, 3])]);
        // ubah ukuran entri (8 byte setelah nama) menjadi sangat besar
        var sizeOffset = 5 + 1 + 2 + "a.txt".Length;
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(sizeOffset), ulong.MaxValue);

        var ex = Assert.Throws<InvalidDataException>(() => Extract(raw, out _));
        Assert.Contains("lebih besar dari sisa data", ex.Message);
    }

    [Fact]
    public void Extract_InvalidUtf8Name_IsRejected()
    {
        using var ms = new MemoryStream();
        ms.Write("TS4B"u8);
        ms.WriteByte(1);
        ms.WriteByte(1);
        ms.Write([2, 0]);
        ms.Write([0xC3, 0x28]); // UTF-8 tidak valid
        ms.Write(new byte[8]);
        ms.WriteByte(0);
        ms.Write(new byte[4]);

        Assert.Throws<InvalidDataException>(() => Extract(ms.ToArray(), out _));
    }

    [Fact]
    public void Extract_NeverOverwritesExistingFiles()
    {
        var dest = NewDestination();
        File.WriteAllText(Path.Combine(dest, "a.txt"), "milik pengguna");

        Assert.ThrowsAny<IOException>(() =>
            BundleExtractor.Extract(new MemoryStream(Raw([("a.txt", [1])])), dest));

        Assert.Equal("milik pengguna", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Extract_ManyEntriesAndUnicodeNames_Work()
    {
        var entries = Enumerable.Range(0, 300).Select(i => ($"berkas-{i:D3}.txt", new[] { (byte)i })).ToList();
        entries.Add(("laporan rahasia — Ünïcode.docx", [1, 2]));

        var names = Extract(Raw(entries.Select(e => (e.Item1, e.Item2))), out var dest);

        Assert.Equal(301, names.Count);
        Assert.Equal([1, 2], File.ReadAllBytes(Path.Combine(dest, "laporan rahasia — Ünïcode.docx")));
    }

    // --- aturan nama -------------------------------------------------------------------------------------------

    [Fact]
    public void Sanitize_ProducesAlwaysValidNames()
    {
        foreach (var raw in new[] { "a:b.txt", "x/y", "CON", "nul.txt", "akhir. ", "", "...", "tab\there", new string('z', 400) })
        {
            var clean = BundleNames.Sanitize(raw);
            Assert.Null(BundleNames.ValidateEntryName(clean));
        }
        Assert.Equal("a_b.txt", BundleNames.Sanitize("a:b.txt"));
        Assert.Equal("_CON", BundleNames.Sanitize("CON"));
        Assert.Equal("laporan.pdf", BundleNames.Sanitize("laporan.pdf")); // yang sudah valid tidak berubah
    }

    [Fact]
    public void MakeUnique_AddsCounters_CaseInsensitive()
    {
        var unique = BundleNames.MakeUnique(["a.txt", "A.txt", "a.txt", "b.txt"]);

        Assert.Equal(["a.txt", "A (2).txt", "a (3).txt", "b.txt"], unique);
        Assert.Equal(4, unique.Select(n => n.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public void FolderNameFor_StripsExtension_AndFallsBack()
    {
        Assert.Equal("laporan-mingguan", BundleNames.FolderNameFor("laporan-mingguan.ts4bundle"));
        Assert.Equal("laporan-mingguan", BundleNames.FolderNameFor("laporan-mingguan.TS4BUNDLE"));
        Assert.Equal("bundle", BundleNames.FolderNameFor(".ts4bundle"));
        Assert.Equal("a_b", BundleNames.FolderNameFor("a/b.ts4bundle"));
        Assert.True(BundleNames.IsBundleFileName("x.ts4bundle"));
        Assert.False(BundleNames.IsBundleFileName("x.ts4"));
    }
}
