using System.Buffers.Binary;
using System.Text;
using Triesoft.Core.Bundle;
using Triesoft.Core.Crypto;
using Xunit;

namespace Triesoft.Core.Tests.Bundle;

/// <summary>Bundle versi 2: path bersubfolder di dalam bundle.</summary>
public class BundleSubfolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("triesoft4-bundle-sub-").FullName;

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

    private static byte[] Raw(byte version, params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        ms.Write("TS4B"u8);
        ms.WriteByte(version);
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
        }
        ms.WriteByte(0x00);
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)entries.Length);
        ms.Write(count);
        return ms.ToArray();
    }

    private IReadOnlyList<string> Extract(byte[] bundle, out string destination)
    {
        destination = NewDestination();
        return BundleExtractor.Extract(new MemoryStream(bundle), destination);
    }

    private static string PathIn(string dest, string entry) => Path.Combine(dest, entry.Replace('/', Path.DirectorySeparatorChar));

    // --- pembuatan dan ekstraksi ---------------------------------------------------------------------

    [Fact]
    public void NestedPaths_RoundTrip_CreateFolders_AndUseVersion2()
    {
        var content = new Dictionary<string, byte[]>
        {
            ["Laporan/a.txt"] = Bytes(1000, 1),
            ["Laporan/sub/b.txt"] = Bytes(50_000, 2),
            ["Laporan/sub/deep/kosong.txt"] = [],
            ["Laporan/sub/deep/c.bin"] = Bytes(300_000, 3),
            ["akar.txt"] = Bytes(10, 4),
        };
        var sources = content.Select(kv => new BundleSource(WriteFile(kv.Key.Replace('/', '_'), kv.Value), kv.Key)).ToList();

        using var stream = new BundleReadStream(sources);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Assert.Equal(stream.Length, ms.Length);
        Assert.Equal(2, ms.ToArray()[4]); // pembuka "TS4B" lalu nomor versi

        var names = Extract(ms.ToArray(), out var dest);

        Assert.Equal(content.Keys, names);
        foreach (var (name, data) in content)
            Assert.Equal(data, File.ReadAllBytes(PathIn(dest, name)));
        Assert.True(Directory.Exists(Path.Combine(dest, "Laporan", "sub", "deep")));
    }

    [Fact]
    public void FlatBundle_StaysVersion1_SoOlderBuildsCanStillOpenIt()
    {
        var sources = new[] { new BundleSource(WriteFile("a.txt", [1]), "a.txt"), new BundleSource(WriteFile("b.txt", [2]), "b.txt") };
        using var stream = new BundleReadStream(sources);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        Assert.Equal(1, ms.ToArray()[4]);
    }

    [Fact]
    public void FullPipeline_NestedBundle_ThroughEnvelopeCipher()
    {
        var content = new Dictionary<string, byte[]>
        {
            ["Proyek/dok/rencana.docx"] = Bytes(1_500_000, 10), // lebih dari satu chunk
            ["Proyek/dok/arsip/lama.pdf"] = Bytes(2_100_000, 11),
            ["Proyek/README.txt"] = Bytes(20, 12),
        };
        var sources = content.Select(kv => new BundleSource(WriteFile(kv.Key.Replace('/', '_'), kv.Value), kv.Key)).ToList();
        using var kek = MonthlyKey.FromBytes("2026-09-TEST", new byte[32]);

        using var encrypted = new MemoryStream();
        using (var input = new BundleReadStream(sources))
            EnvelopeCipher.Encrypt(input, encrypted, kek, "proyek.ts4bundle");
        encrypted.Position = 0;
        using var plain = new MemoryStream();
        EnvelopeCipher.Decrypt(encrypted, plain, kek);
        plain.Position = 0;

        var dest = NewDestination();
        var names = BundleExtractor.Extract(plain, dest);

        Assert.Equal(content.Keys, names);
        foreach (var (name, data) in content)
            Assert.Equal(data, File.ReadAllBytes(PathIn(dest, name)));
    }

    [Fact]
    public void Extract_Version1_StillWorksAndStillRejectsSubfolders()
    {
        var ok = Extract(Raw(1, ("a.txt", [1])), out _);
        Assert.Equal(["a.txt"], ok);

        var ex = Assert.Throws<InvalidDataException>(() => Extract(Raw(1, ("a/b.txt", [1])), out _));
        Assert.Contains("tidak aman", ex.Message);
    }

    // --- path berbahaya di versi 2 ------------------------------------------------------------------------

    [Theory]
    [InlineData("a/../b.txt")]
    [InlineData("../keluar.txt")]
    [InlineData("a/b/../../../keluar.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("a//b.txt")]
    [InlineData("a/")]
    [InlineData("a/b/")]
    [InlineData("a\\b.txt")]
    [InlineData("a\\..\\b.txt")]
    [InlineData("C:/Windows/x.txt")]
    [InlineData("CON/x.txt")]
    [InlineData("dok/NUL.txt")]
    [InlineData("dok/b:stream")]
    [InlineData("dok/ /x.txt")]
    [InlineData("dok./x.txt")]
    [InlineData("./x.txt")]
    [InlineData("a/./b.txt")]
    public void Extract_UnsafeNestedPath_IsRejected_AndWritesNothing(string name)
    {
        var dest = NewDestination();
        var ex = Assert.Throws<InvalidDataException>(() =>
            BundleExtractor.Extract(new MemoryStream(Raw(2, (name, [1, 2, 3]))), dest));

        Assert.Contains("tidak aman", ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(dest));
        Assert.False(File.Exists(Path.Combine(_root, "keluar.txt")));
    }

    [Fact]
    public void Extract_TooDeepOrTooLongPath_IsRejected()
    {
        var tooDeep = string.Join('/', Enumerable.Repeat("d", BundleNames.MaxDepth)) + "/x.txt"; // 33 komponen
        var atLimit = string.Join('/', Enumerable.Repeat("d", BundleNames.MaxDepth - 1)) + "/x.txt"; // 32 komponen
        var tooLong = string.Join('/', Enumerable.Repeat(new string('n', 200), 6)) + "/x.txt";

        Assert.Throws<InvalidDataException>(() => Extract(Raw(2, (tooDeep, [1])), out _));
        Assert.Throws<InvalidDataException>(() => Extract(Raw(2, (tooLong, [1])), out _));
        Assert.Equal([atLimit], Extract(Raw(2, (atLimit, [1])), out _));
    }

    [Fact]
    public void Extract_NameUsedAsBothFileAndFolder_IsRejected()
    {
        var fileThenFolder = Assert.Throws<InvalidDataException>(() => Extract(Raw(2, ("a", [1]), ("a/b.txt", [2])), out _));
        var folderThenFile = Assert.Throws<InvalidDataException>(() => Extract(Raw(2, ("a/b.txt", [2]), ("a", [1])), out _));
        var deeper = Assert.Throws<InvalidDataException>(() => Extract(Raw(2, ("x/y", [1]), ("x/y/z.txt", [2])), out _));

        Assert.All([fileThenFolder, folderThenFile, deeper], ex => Assert.Contains("file sekaligus nama folder", ex.Message));
    }

    [Fact]
    public void Extract_DuplicatePaths_CaseInsensitive_AreRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            Extract(Raw(2, ("Dok/a.txt", [1]), ("dok/A.TXT", [2])), out _));
        Assert.Contains("dua kali", ex.Message);
    }

    [Fact]
    public void Extract_NeverOverwritesExistingFile_InSubfolder()
    {
        var dest = NewDestination();
        Directory.CreateDirectory(Path.Combine(dest, "dok"));
        File.WriteAllText(Path.Combine(dest, "dok", "a.txt"), "milik pengguna");

        Assert.ThrowsAny<IOException>(() =>
            BundleExtractor.Extract(new MemoryStream(Raw(2, ("dok/a.txt", [1]))), dest));

        Assert.Equal("milik pengguna", File.ReadAllText(Path.Combine(dest, "dok", "a.txt")));
    }

    // --- pembuat aliran menolak input yang mustahil diekstrak -------------------------------------------------

    [Fact]
    public void ReadStream_RejectsUnsafePathsAndFileFolderClashes()
    {
        var f = WriteFile("f.txt", [1]);

        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(f, "a/../b.txt")]));
        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(f, "/abs.txt")]));
        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(f, "a\\b.txt")]));
        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(f, "a"), new BundleSource(f, "a/b.txt")]));
        Assert.Throws<ArgumentException>(() => new BundleReadStream([new BundleSource(f, "Dok/a.txt"), new BundleSource(f, "dok/A.TXT")]));
    }

    // --- perencanaan nama di dalam bundle --------------------------------------------------------------------

    [Fact]
    public void PlanEntryPaths_GroupsFolderFilesUnderOneRoot_AndKeepsSingleFilesAtTheRoot()
    {
        var plan = BundleNames.PlanEntryPaths([
            new BundlePlanItem("g1", "Laporan", "a.txt", "a.txt"),
            new BundlePlanItem("g1", "Laporan", "sub/b.txt", "b.txt"),
            new BundlePlanItem(null, null, null, "lepas.pdf"),
        ]);

        Assert.Equal(["Laporan/a.txt", "Laporan/sub/b.txt", "lepas.pdf"], plan);
    }

    [Fact]
    public void PlanEntryPaths_TwoDifferentFoldersWithSameName_DoNotMerge()
    {
        var plan = BundleNames.PlanEntryPaths([
            new BundlePlanItem("g1", "Laporan", "a.txt", "a.txt"),
            new BundlePlanItem("g2", "Laporan", "a.txt", "a.txt"),
            new BundlePlanItem("g2", "Laporan", "b.txt", "b.txt"),
            new BundlePlanItem("g3", "LAPORAN", "a.txt", "a.txt"), // tidak peka huruf besar/kecil
        ]);

        Assert.Equal(["Laporan/a.txt", "Laporan (2)/a.txt", "Laporan (2)/b.txt", "LAPORAN (3)/a.txt"], plan);
    }

    [Fact]
    public void PlanEntryPaths_SingleFilesWithSameName_GetCounters_AndFileVersusFolderClashIsResolved()
    {
        var plan = BundleNames.PlanEntryPaths([
            new BundlePlanItem(null, null, null, "catatan.txt"),
            new BundlePlanItem(null, null, null, "catatan.txt"),
            new BundlePlanItem(null, null, null, "Laporan"),          // file bernama sama dengan folder di bawah
            new BundlePlanItem("g1", "Laporan", "a.txt", "a.txt"),
        ]);

        Assert.Equal(["catatan.txt", "catatan (2).txt", "Laporan", "Laporan (2)/a.txt"], plan);
        // dan hasilnya selalu bisa dijadikan aliran bundle yang valid
        var f = WriteFile("x.txt", [1]);
        using var stream = new BundleReadStream(plan.Select(p => new BundleSource(f, p)).ToList());
    }

    [Fact]
    public void PlanEntryPaths_SanitizesUnusualNames_AndIsAlwaysValid()
    {
        var plan = BundleNames.PlanEntryPaths([
            new BundlePlanItem("g1", "CON", "x.txt", "x.txt"),
            new BundlePlanItem("g2", "a:b?", "y.txt", "y.txt"),
            new BundlePlanItem(null, null, null, "NUL"),
        ]);

        Assert.All(plan, p => Assert.Null(BundleNames.ValidateEntryPath(p)));
        Assert.Equal("_CON/x.txt", plan[0]);
        Assert.Equal("a_b_/y.txt", plan[1]);
    }

    [Fact]
    public void ValidateEntryPath_AcceptsNormalPaths_IncludingUnicodeAndSpaces()
    {
        Assert.Null(BundleNames.ValidateEntryPath("Laporan Rahasia/Ünïcode — data/berkas (2).docx"));
        Assert.Null(BundleNames.ValidateEntryPath("a.txt"));
        Assert.NotNull(BundleNames.ValidateEntryPath(""));
    }
}
