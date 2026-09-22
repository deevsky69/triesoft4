using System.Buffers.Binary;
using System.Text;
using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Bundle;
using Triesoft.Core.Crypto;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

/// <summary>Mode bundle: banyak file menjadi satu .ts4, dan dekripsinya menjadi satu folder.</summary>
public class BundleViewModelTests : IDisposable
{
    private readonly string _keyDir = Directory.CreateTempSubdirectory("triesoft4-bundlevm-keys-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("triesoft4-bundlevm-audit-").FullName;
    private readonly string _workDir = Directory.CreateTempSubdirectory("triesoft4-bundlevm-work-").FullName;
    private readonly MonthlyKeyManager _keys;
    private readonly FileAuditLog _audit;
    private readonly SessionContext _session = new();

    public BundleViewModelTests()
    {
        _keys = new MonthlyKeyManager(new FileKeyStore(_keyDir, new PassthroughKeyProtector()));
        _keys.ImportMonthlyKey("2026-09-TEST", new byte[32], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
        _audit = new FileAuditLog(_auditDir);
        _session.SignIn(new UserAccount("op1", "Operator", UserRole.Operator, UserStatus.Active, [], [], DateTimeOffset.UtcNow, "admin1"));
    }

    public void Dispose()
    {
        foreach (var d in new[] { _keyDir, _auditDir, _workDir })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private EncryptViewModel NewEncrypt(string bundleName = "paket") =>
        new(_keys, _audit, _session) { BundleMode = true, BundleName = bundleName };

    private DecryptViewModel NewDecrypt() => new(_keys, _audit, _session);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_workDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // Dinormalkan supaya sama persis dengan path yang dikembalikan aplikasi (pemisah "/" dan "\" tidak tercampur).
    private string P(string relative) => Path.GetFullPath(Path.Combine(_workDir, relative));

    // --- membuat bundle ------------------------------------------------------------------------------

    [Fact]
    public async Task Bundle_ProducesSingleTs4_NoPerFileOutputs_AndOneAuditEntryListingContents()
    {
        var files = new[] { Write("x/laporan.pdf", "L"), Write("y/foto.jpg", "F"), Write("z/data.xlsx", "D") };
        var vm = NewEncrypt("paket-rahasia");
        vm.AddFiles(files);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Contains("3 file berhasil dibuat", vm.SuccessMessage);
        var bundlePath = P("x/paket-rahasia.ts4"); // folder file pertama
        Assert.True(File.Exists(bundlePath));
        Assert.All(files, f => Assert.False(File.Exists(f + ".ts4")));
        Assert.Empty(Directory.GetFiles(_workDir, "*.partial", SearchOption.AllDirectories));
        Assert.All(vm.Files, f => Assert.Equal(BatchStatus.Succeeded, f.Status));

        var entry = Assert.Single(_audit.ReadAll());
        Assert.Equal(AuditAction.FileEncrypted, entry.Action);
        Assert.Contains("bundle=paket-rahasia", entry.Details);
        Assert.Contains("files=3", entry.Details);
        Assert.Contains("laporan.pdf | foto.jpg | data.xlsx", entry.Details);
        Assert.True(_audit.VerifyChain().IsIntact);
    }

    [Fact]
    public async Task Bundle_UsesChosenFolder_StripsTs4Suffix_SanitizesName_AndNeverOverwrites()
    {
        var f1 = Write("a/1.txt", "1");
        var out1 = Directory.CreateDirectory(P("keluar")).FullName;
        var vm = NewEncrypt("rap:or/an.ts4");
        vm.AddFiles([f1]);
        vm.BundleOutputFolder = out1;

        await vm.EncryptCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(out1, "rap_or_an.ts4")));

        vm.ClearFilesCommand.Execute(null);
        vm.AddFiles([f1]);
        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(out1, "rap_or_an (2).ts4"))); // yang pertama tidak ditimpa
        Assert.Equal(2, Directory.GetFiles(out1, "*.ts4").Length);
    }

    [Fact]
    public async Task Bundle_FilesWithSameNameFromDifferentFolders_GetUniqueEntryNames()
    {
        var f1 = Write("x/catatan.txt", "dari x");
        var f2 = Write("y/catatan.txt", "dari y");
        var enc = NewEncrypt("dua");
        enc.AddFiles([f1, f2]);
        await enc.EncryptCommand.ExecuteAsync(null);
        File.Delete(f1);
        File.Delete(f2);

        var dec = NewDecrypt();
        dec.AddFiles([P("x/dua.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        var folder = P("x/dua");
        Assert.Equal("dari x", File.ReadAllText(Path.Combine(folder, "catatan.txt")));
        Assert.Equal("dari y", File.ReadAllText(Path.Combine(folder, "catatan (2).txt")));
    }

    [Fact]
    public async Task Bundle_IsAtomic_OneUnreadableFile_MeansNoBundleAtAll()
    {
        var good1 = Write("a/1.txt", "1");
        var missing = P("a/hilang.txt");
        var good2 = Write("a/2.txt", "2");
        var vm = NewEncrypt("paket");
        vm.AddFiles([good1, missing, good2]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Equal([BatchStatus.Canceled, BatchStatus.Failed, BatchStatus.Canceled], vm.Files.Select(f => f.Status));
        Assert.Equal("File tidak ditemukan.", vm.Files[1].Message);
        Assert.Contains("Bundle tidak dibuat", vm.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_workDir, "*.ts4*", SearchOption.AllDirectories));
        Assert.Contains(_audit.ReadAll(), e => e.Action == AuditAction.FileEncryptFailed && e.Details.Contains("hilang.txt"));
        Assert.DoesNotContain(_audit.ReadAll(), e => e.Action == AuditAction.FileEncrypted);
    }

    [Fact]
    public async Task Bundle_ValidatesNameAndFolder_BeforeDoingAnything()
    {
        var f = Write("a/1.txt", "1");
        var vm = NewEncrypt("   ");
        vm.AddFiles([f]);
        await vm.EncryptCommand.ExecuteAsync(null);
        Assert.Contains("Nama bundle wajib diisi", vm.ErrorMessage);

        vm.BundleName = "ok";
        vm.BundleOutputFolder = P("folder-tidak-ada");
        await vm.EncryptCommand.ExecuteAsync(null);
        Assert.Contains("Folder tujuan", vm.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_workDir, "*.ts4*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Bundle_NoActiveKey_FailsAllRowsWithClearMessage_AndLeavesNoPartial()
    {
        _keys.RevokeKey("2026-09-TEST", "uji");
        var vm = NewEncrypt("paket");
        vm.AddFiles([Write("a/1.txt", "1"), Write("a/2.txt", "2")]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.All(vm.Files, f =>
        {
            Assert.Equal(BatchStatus.Failed, f.Status);
            Assert.Contains("kunci aktif", f.Message);
        });
        Assert.Empty(Directory.GetFiles(_workDir, "*.ts4*", SearchOption.AllDirectories));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Bundle_Cancel_LeavesNothingBehind_AndAllRowsCanceled()
    {
        var vm = NewEncrypt("batal");
        vm.AddFiles([Write("a/1.txt", "1"), Write("a/2.txt", "2")]);
        vm.Files[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchFileItem.Status) && vm.Files[0].Status == BatchStatus.Running)
                vm.CancelCommand.Execute(null);
        };

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.All(vm.Files, f => Assert.Equal(BatchStatus.Canceled, f.Status));
        Assert.Contains("dibatalkan", vm.SuccessMessage);
        Assert.Empty(Directory.GetFiles(_workDir, "*.ts4*", SearchOption.AllDirectories));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SingleMode_RejectsFileNamedLikeABundle()
    {
        var reserved = Write("a/palsu.ts4bundle", "bukan bundle");
        var vm = new EncryptViewModel(_keys, _audit, _session); // BundleMode mati
        vm.AddFiles([reserved]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Failed, vm.Files.Single().Status);
        Assert.Contains(".ts4bundle", vm.Files.Single().Message);
        Assert.False(File.Exists(reserved + ".ts4"));
    }

    [Fact]
    public void BundleMode_ButtonTextAndEffectiveFolderFollowState()
    {
        var vm = new EncryptViewModel(_keys, _audit, _session);
        Assert.Equal("Enkripsi Semua", vm.RunButtonText);

        vm.BundleMode = true;
        Assert.Equal("Buat Bundle", vm.RunButtonText);
        Assert.Equal("", vm.EffectiveBundleFolder);

        vm.AddFiles([Write("q/1.txt", "1")]);
        Assert.Equal(P("q"), vm.EffectiveBundleFolder);

        vm.BundleOutputFolder = P("lain");
        Assert.Equal(P("lain"), vm.EffectiveBundleFolder);
    }

    // --- membuka bundle ------------------------------------------------------------------------------

    [Fact]
    public async Task Bundle_RoundTrip_RestoresEveryFileIntoANewFolder()
    {
        var originals = new Dictionary<string, string>
        {
            [Write("x/laporan.pdf", "isi laporan")] = "isi laporan",
            [Write("y/foto.jpg", "isi foto")] = "isi foto",
        };
        var enc = NewEncrypt("misi-01");
        enc.AddFiles(originals.Keys);
        await enc.EncryptCommand.ExecuteAsync(null);
        foreach (var p in originals.Keys) File.Delete(p);

        var dec = NewDecrypt();
        dec.AddFiles([P("x/misi-01.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Null(dec.ErrorMessage);
        var folder = P("x/misi-01");
        Assert.Equal(folder, dec.Files.Single().Message);
        Assert.Equal("isi laporan", File.ReadAllText(Path.Combine(folder, "laporan.pdf")));
        Assert.Equal("isi foto", File.ReadAllText(Path.Combine(folder, "foto.jpg")));
        Assert.Equal(2, Directory.GetFiles(folder).Length);

        // tidak ada file sementara tersisa di folder .ts4
        Assert.Equal(["misi-01", "misi-01.ts4"], Directory.GetFileSystemEntries(P("x")).Select(Path.GetFileName).Order());
        var entries = _audit.ReadAll();
        Assert.Equal(AuditAction.FileDecrypted, entries[^1].Action);
        Assert.Contains("2 file", entries[^1].Details);
        Assert.True(_audit.VerifyChain().IsIntact);
    }

    [Fact]
    public async Task Bundle_Decrypt_NeverMergesIntoExistingFolder()
    {
        var f = Write("x/a.txt", "baru");
        var enc = NewEncrypt("paket");
        enc.AddFiles([f]);
        await enc.EncryptCommand.ExecuteAsync(null);
        Directory.CreateDirectory(P("x/paket"));
        File.WriteAllText(P("x/paket/milik-pengguna.txt"), "jangan disentuh");

        var dec = NewDecrypt();
        dec.AddFiles([P("x/paket.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal(P("x/paket (2)"), dec.Files.Single().Message);
        Assert.Equal("baru", File.ReadAllText(P("x/paket (2)/a.txt")));
        Assert.Equal(["milik-pengguna.txt"], Directory.GetFiles(P("x/paket")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Bundle_Decrypt_TwoBundlesWithSameNameInOneRun_GetSeparateFolders()
    {
        var f1 = Write("x/a.txt", "satu");
        var f2 = Write("x/b.txt", "dua");
        var enc = NewEncrypt("sama");
        enc.AddFiles([f1]);
        await enc.EncryptCommand.ExecuteAsync(null);
        File.Move(P("x/sama.ts4"), P("x/salinan-1.ts4"));
        enc.ClearFilesCommand.Execute(null);
        enc.AddFiles([f2]);
        await enc.EncryptCommand.ExecuteAsync(null);
        File.Move(P("x/sama.ts4"), P("x/salinan-2.ts4"));

        var dec = NewDecrypt();
        dec.AddFiles([P("x/salinan-1.ts4"), P("x/salinan-2.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.All(dec.Files, f => Assert.Equal(BatchStatus.Succeeded, f.Status));
        Assert.Equal("satu", File.ReadAllText(P("x/sama/a.txt")));
        Assert.Equal("dua", File.ReadAllText(P("x/sama (2)/b.txt")));
    }

    [Fact]
    public async Task Bundle_Decrypt_Tampered_LeavesNoFolderAndNoTempFiles()
    {
        var f = Write("x/besar.bin", new string('q', 300_000));
        var enc = NewEncrypt("rusak");
        enc.AddFiles([f]);
        await enc.EncryptCommand.ExecuteAsync(null);
        var bytes = File.ReadAllBytes(P("x/rusak.ts4"));
        bytes[^20] ^= 0xFF;
        File.WriteAllBytes(P("x/rusak.ts4"), bytes);
        var before = Directory.GetFileSystemEntries(P("x")).Select(Path.GetFileName).Order().ToList();

        var dec = NewDecrypt();
        dec.AddFiles([P("x/rusak.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Failed, dec.Files.Single().Status);
        Assert.Equal(before, Directory.GetFileSystemEntries(P("x")).Select(Path.GetFileName).Order());
        Assert.Contains(_audit.ReadAll(), e => e.Action == AuditAction.FileDecryptFailed);
    }

    [Fact]
    public async Task Bundle_Decrypt_MaliciousBundle_IsRejected_WithNothingWrittenAnywhere()
    {
        // Pengirim yang memegang kunci sah membuat bundle dengan nama entri berbahaya (mis. lewat alat sendiri).
        var evil = RawBundle(1, ("aman.txt", "ok"u8.ToArray()), ("..\\..\\keluar.txt", "jahat"u8.ToArray()));
        var dir = Directory.CreateDirectory(P("kotak")).FullName;
        using (var kek = _keys.GetActiveKeyForEncryption())
        using (var output = File.Create(Path.Combine(dir, "jahat.ts4")))
            EnvelopeCipher.Encrypt(new MemoryStream(evil), output, kek, "jahat" + BundleNames.Extension);
        var before = Directory.GetFileSystemEntries(_workDir, "*", SearchOption.AllDirectories).Length;

        var dec = NewDecrypt();
        dec.AddFiles([Path.Combine(dir, "jahat.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Failed, dec.Files.Single().Status);
        Assert.Contains("tidak aman", dec.Files.Single().Message);
        Assert.Equal(before, Directory.GetFileSystemEntries(_workDir, "*", SearchOption.AllDirectories).Length);
        Assert.False(File.Exists(P("keluar.txt")));
        Assert.False(Directory.Exists(P("kotak/jahat")));
    }

    [Fact]
    public async Task Decrypt_FileNamedLikeBundleButNotABundle_IsReturnedAsPlainFile()
    {
        // mis. dibuat versi lama yang boleh mengenkripsi file bernama *.ts4bundle: datanya tidak boleh hilang
        var dir = Directory.CreateDirectory(P("lama")).FullName;
        using (var kek = _keys.GetActiveKeyForEncryption())
        using (var output = File.Create(Path.Combine(dir, "x.ts4")))
            EnvelopeCipher.Encrypt(new MemoryStream("isi biasa"u8.ToArray()), output, kek, "catatan.ts4bundle");

        var dec = NewDecrypt();
        dec.AddFiles([Path.Combine(dir, "x.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Succeeded, dec.Files.Single().Status);
        Assert.Equal("isi biasa", File.ReadAllText(Path.Combine(dir, "catatan.ts4bundle")));
    }

    // --- subfolder di dalam bundle -------------------------------------------------------------------------

    [Fact]
    public async Task Bundle_WithFolderTree_RoundTripsWithStructure_AndAuditListsPaths()
    {
        Write("Laporan/a.txt", "isi a");
        Write("Laporan/sub/b.txt", "isi b");
        Write("Laporan/sub/deep/c.txt", "isi c");
        var lepas = Write("lepas/lepas.pdf", "isi lepas");
        var enc = NewEncrypt("misi");
        enc.BundleOutputFolder = Directory.CreateDirectory(P("hasil")).FullName;
        enc.AddFolder(P("Laporan"));
        enc.AddFiles([lepas]);

        await enc.EncryptCommand.ExecuteAsync(null);

        Assert.Null(enc.ErrorMessage);
        Assert.Contains("4 file berhasil dibuat", enc.SuccessMessage);
        var entry = Assert.Single(_audit.ReadAll(), e => e.Action == AuditAction.FileEncrypted);
        Assert.Contains("Laporan/a.txt", entry.Details);
        Assert.Contains("Laporan/sub/deep/c.txt", entry.Details);

        Directory.Delete(P("Laporan"), recursive: true);
        File.Delete(lepas);
        var dec = NewDecrypt();
        dec.AddFiles([P("hasil/misi.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Null(dec.ErrorMessage);
        var root = P("hasil/misi");
        Assert.Equal("isi a", File.ReadAllText(Path.Combine(root, "Laporan", "a.txt")));
        Assert.Equal("isi b", File.ReadAllText(Path.Combine(root, "Laporan", "sub", "b.txt")));
        Assert.Equal("isi c", File.ReadAllText(Path.Combine(root, "Laporan", "sub", "deep", "c.txt")));
        Assert.Equal("isi lepas", File.ReadAllText(Path.Combine(root, "lepas.pdf")));
        Assert.Contains("4 file", _audit.ReadAll()[^1].Details);
        Assert.True(_audit.VerifyChain().IsIntact);
    }

    [Fact]
    public void Bundle_DefaultFolder_IsNextToTheAddedFolder_NotInsideASubfolder()
    {
        Write("Proyek/lampiran/peta/lokasi.jpg", "1"); // file pertama (urut) ada dua tingkat di dalam
        Write("Proyek/zzz.txt", "2");
        var vm = NewEncrypt();

        vm.AddFolder(P("Proyek"));
        Assert.Equal("lokasi.jpg", vm.Files[0].FileName);
        Assert.Equal(P("."), vm.EffectiveBundleFolder); // induk dari "Proyek", yaitu folder kerja

        vm.ClearFilesCommand.Execute(null);
        vm.AddFiles([Write("lepas/x.txt", "x")]);
        Assert.Equal(P("lepas"), vm.EffectiveBundleFolder); // file tunggal: foldernya sendiri
    }

    [Fact]
    public async Task Bundle_TwoFoldersWithSameNameFromDifferentPlaces_StaySeparate()
    {
        Write("x/Data/a.txt", "dari x");
        Write("y/Data/a.txt", "dari y");
        var enc = NewEncrypt("dua");
        enc.BundleOutputFolder = Directory.CreateDirectory(P("hasil")).FullName;
        enc.AddFolder(P("x/Data"));
        enc.AddFolder(P("y/Data"));

        await enc.EncryptCommand.ExecuteAsync(null);
        var dec = NewDecrypt();
        dec.AddFiles([P("hasil/dua.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal("dari x", File.ReadAllText(P("hasil/dua/Data/a.txt")));
        Assert.Equal("dari y", File.ReadAllText(P("hasil/dua/Data (2)/a.txt")));
    }

    [Fact]
    public async Task Bundle_FileAndFolderWithSameName_DoNotClash()
    {
        var lepas = Write("lepas/Laporan", "file tanpa ekstensi");
        Write("Laporan/a.txt", "dalam folder");
        var enc = NewEncrypt("bentrok");
        enc.BundleOutputFolder = Directory.CreateDirectory(P("hasil")).FullName;
        enc.AddFiles([lepas]);
        enc.AddFolder(P("Laporan"));

        await enc.EncryptCommand.ExecuteAsync(null);
        var dec = NewDecrypt();
        dec.AddFiles([P("hasil/bentrok.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal("file tanpa ekstensi", File.ReadAllText(P("hasil/bentrok/Laporan")));
        Assert.Equal("dalam folder", File.ReadAllText(P("hasil/bentrok/Laporan (2)/a.txt")));
    }

    [Fact]
    public async Task Bundle_PathDeeperThanAllowed_IsCaughtBeforeAnythingIsWritten()
    {
        var deep = "Dalam";
        for (var i = 0; i < BundleNames.MaxDepth + 2; i++) deep += "/d";
        Write(deep + "/berkas.txt", "terlalu dalam");
        var enc = NewEncrypt("dalam");
        enc.BundleOutputFolder = Directory.CreateDirectory(P("hasil")).FullName;
        enc.AddFolder(P("Dalam"));

        await enc.EncryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Failed, enc.Files.Single().Status);
        Assert.Contains("Path di dalam bundle tidak valid", enc.Files.Single().Message);
        Assert.Contains("Bundle tidak dibuat", enc.ErrorMessage);
        Assert.Empty(Directory.GetFiles(P("hasil")));
    }

    [Fact]
    public async Task Bundle_WithoutSubfolders_OnlyTopLevelFilesEnterTheBundle()
    {
        Write("Laporan/a.txt", "a");
        Write("Laporan/sub/b.txt", "b");
        var enc = NewEncrypt("datar");
        enc.IncludeSubfolders = false;
        enc.BundleOutputFolder = Directory.CreateDirectory(P("hasil")).FullName;
        enc.AddFolder(P("Laporan"));
        await enc.EncryptCommand.ExecuteAsync(null);

        var dec = NewDecrypt();
        dec.AddFiles([P("hasil/datar.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal(["a.txt"], Directory.GetFileSystemEntries(P("hasil/datar/Laporan")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Bundle_Decrypt_MaliciousNestedPaths_AreRejected_WithNothingWrittenAnywhere()
    {
        foreach (var evilPath in new[] { "a/../../keluar.txt", "/mutlak.txt", "a//b.txt", "dok/CON/x.txt", "a\\..\\..\\keluar.txt" })
        {
            var dir = Directory.CreateDirectory(P("kotak-" + Guid.NewGuid().ToString("N")[..6])).FullName;
            var evil = RawBundle(2, ("aman/ok.txt", "ok"u8.ToArray()), (evilPath, "jahat"u8.ToArray()));
            using (var kek = _keys.GetActiveKeyForEncryption())
            using (var output = File.Create(Path.Combine(dir, "jahat.ts4")))
                EnvelopeCipher.Encrypt(new MemoryStream(evil), output, kek, "jahat" + BundleNames.Extension);

            var dec = NewDecrypt();
            dec.AddFiles([Path.Combine(dir, "jahat.ts4")]);
            await dec.DecryptCommand.ExecuteAsync(null);

            Assert.Equal(BatchStatus.Failed, dec.Files.Single().Status);
            Assert.Contains("tidak aman", dec.Files.Single().Message);
            Assert.Equal(["jahat.ts4"], Directory.GetFileSystemEntries(dir).Select(Path.GetFileName)); // tidak ada folder/staging tersisa
        }
        Assert.False(File.Exists(P("keluar.txt")));
        Assert.False(File.Exists(P("mutlak.txt")));
    }

    private static byte[] RawBundle(byte version, params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        ms.Write("TS4B"u8);
        ms.WriteByte(version);
        foreach (var (name, data) in entries)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            ms.WriteByte(1);
            var len = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)nameBytes.Length);
            ms.Write(len);
            ms.Write(nameBytes);
            var size = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(size, (ulong)data.Length);
            ms.Write(size);
            ms.Write(data);
        }
        ms.WriteByte(0);
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)entries.Length);
        ms.Write(count);
        return ms.ToArray();
    }
}
