using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Crypto;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

/// <summary>Enkripsi dan dekripsi massal: daftar file, status per file, kegagalan sebagian, pembatalan, dan penamaan output.</summary>
public class BatchFileViewModelTests : IDisposable
{
    private readonly string _keyDir = Directory.CreateTempSubdirectory("triesoft4-batch-keys-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("triesoft4-batch-audit-").FullName;
    private readonly string _workDir = Directory.CreateTempSubdirectory("triesoft4-batch-work-").FullName;
    private readonly MonthlyKeyManager _keys;
    private readonly FileAuditLog _audit;
    private readonly SessionContext _session = new();

    public BatchFileViewModelTests()
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

    private EncryptViewModel NewEncrypt() => new(_keys, _audit, _session);
    private DecryptViewModel NewDecrypt() => new(_keys, _audit, _session);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_workDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // --- daftar file ------------------------------------------------------------------------------

    [Fact]
    public void AddFiles_SkipsDuplicates_AndRemoveAndClearWork()
    {
        var a = Write("a.txt", "a");
        var b = Write("b.txt", "b");
        var vm = NewEncrypt();

        Assert.Equal(2, vm.AddFiles([a, b]));
        Assert.Equal(0, vm.AddFiles([a.ToUpperInvariant(), b])); // pembanding tidak peka huruf besar/kecil
        Assert.Equal(2, vm.Files.Count);
        Assert.True(vm.HasFiles);
        Assert.Equal("2 file dalam daftar", vm.FileCountText);

        vm.Files[0].RemoveCommand.Execute(null);
        Assert.Single(vm.Files);

        vm.ClearFilesCommand.Execute(null);
        Assert.Empty(vm.Files);
        Assert.False(vm.EncryptCommand.CanExecute(null));
    }

    [Fact]
    public void AddFolder_Encrypt_SkipsTs4Files_AndDecrypt_TakesOnlyTs4()
    {
        Write("folder/a.txt", "a");
        Write("folder/b.pdf", "b");
        Write("folder/c.ts4", "c");
        Write("folder/sub/d.txt", "d"); // subfolder tidak ikut

        var enc = NewEncrypt();
        Assert.Equal(2, enc.AddFolder(Path.Combine(_workDir, "folder")));
        Assert.DoesNotContain(enc.Files, f => f.FileName.EndsWith(".ts4"));

        var dec = NewDecrypt();
        Assert.Equal(1, dec.AddFolder(Path.Combine(_workDir, "folder")));
        Assert.Equal("c.ts4", dec.Files.Single().FileName);
    }

    // --- enkripsi massal ----------------------------------------------------------------------------

    [Fact]
    public async Task EncryptAll_ProducesOneTs4PerFile_MarksEachSucceeded_AndAuditsEach()
    {
        var paths = new[] { Write("x/1.txt", "satu"), Write("x/2.txt", "dua"), Write("y/3.txt", "tiga") };
        var vm = NewEncrypt();
        vm.AddFiles(paths);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("3 file berhasil dienkripsi.", vm.SuccessMessage);
        Assert.All(paths, p => Assert.True(File.Exists(p + ".ts4")));
        Assert.All(vm.Files, f => Assert.Equal(BatchStatus.Succeeded, f.Status));
        Assert.Equal(1.0, vm.Progress);
        Assert.False(vm.IsBusy);
        Assert.Equal(3, _audit.ReadAll().Count(e => e.Action == AuditAction.FileEncrypted));
        Assert.True(_audit.VerifyChain().IsIntact);
    }

    [Fact]
    public async Task EncryptAll_OneFileFails_OthersStillSucceed_AndFailureIsAudited()
    {
        var good1 = Write("g1.txt", "satu");
        var missing = Path.Combine(_workDir, "hilang.txt");
        var good2 = Write("g2.txt", "dua");
        var vm = NewEncrypt();
        vm.AddFiles([good1, missing, good2]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Equal([BatchStatus.Succeeded, BatchStatus.Failed, BatchStatus.Succeeded], vm.Files.Select(f => f.Status));
        Assert.NotEmpty(vm.Files[1].Message);
        Assert.Contains("1 gagal", vm.ErrorMessage);
        Assert.Contains("Dari 3 file", vm.ErrorMessage);
        Assert.True(File.Exists(good1 + ".ts4"));
        Assert.True(File.Exists(good2 + ".ts4"));
        Assert.False(File.Exists(missing + ".ts4"));
        Assert.Contains(_audit.ReadAll(), e => e.Action == AuditAction.FileEncryptFailed && e.Details.Contains("hilang.txt"));
    }

    [Fact]
    public async Task EncryptAll_FailureBeforeCreate_DoesNotDeleteExistingTs4()
    {
        // .ts4 lama milik pengguna tidak boleh hilang hanya karena input baru gagal dibuka
        var missing = Path.Combine(_workDir, "hilang.txt");
        var existing = Write("hilang.txt.ts4", "milik pengguna");
        var vm = NewEncrypt();
        vm.AddFiles([missing]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Failed, vm.Files.Single().Status);
        Assert.Equal("milik pengguna", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task Encrypt_NoActiveKey_FailsEveryFileWithClearMessage()
    {
        _keys.RevokeKey("2026-09-TEST", "uji");
        var vm = NewEncrypt();
        vm.AddFiles([Write("a.txt", "a"), Write("b.txt", "b")]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.All(vm.Files, f =>
        {
            Assert.Equal(BatchStatus.Failed, f.Status);
            Assert.Contains("kunci aktif", f.Message);
        });
        Assert.Equal(2, _audit.ReadAll().Count(e => e.Action == AuditAction.FileEncryptFailed));
    }

    [Fact]
    public async Task RunAgain_OnlyRetriesFilesThatNotSucceeded()
    {
        var good = Write("g.txt", "g");
        var late = Path.Combine(_workDir, "late.txt");
        var vm = NewEncrypt();
        vm.AddFiles([good, late]);
        await vm.EncryptCommand.ExecuteAsync(null);
        Assert.Equal(BatchStatus.Failed, vm.Files[1].Status);
        var encryptedBefore = _audit.ReadAll().Count(e => e.Action == AuditAction.FileEncrypted);

        File.WriteAllText(late, "sekarang ada");
        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.All(vm.Files, f => Assert.Equal(BatchStatus.Succeeded, f.Status));
        Assert.Equal(encryptedBefore + 1, _audit.ReadAll().Count(e => e.Action == AuditAction.FileEncrypted)); // "g.txt" tidak diulang
    }

    [Fact]
    public async Task RunWhenEverythingAlreadyDone_ExplainsInsteadOfReprocessing()
    {
        var vm = NewEncrypt();
        vm.AddFiles([Write("a.txt", "a")]);
        await vm.EncryptCommand.ExecuteAsync(null);
        var count = _audit.ReadAll().Count;

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Contains("sudah diproses", vm.SuccessMessage);
        Assert.Equal(count, _audit.ReadAll().Count);
    }

    [Fact]
    public async Task Cancel_AfterFirstFile_MarksTheRestCanceled()
    {
        var vm = NewEncrypt();
        vm.AddFiles([Write("1.txt", "1"), Write("2.txt", "2"), Write("3.txt", "3")]);
        vm.Files[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchFileItem.Status) && vm.Files[0].Status == BatchStatus.Succeeded)
                vm.CancelCommand.Execute(null);
        };

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Equal([BatchStatus.Succeeded, BatchStatus.Canceled, BatchStatus.Canceled], vm.Files.Select(f => f.Status));
        Assert.False(File.Exists(Path.Combine(_workDir, "2.txt.ts4")));
        Assert.Contains("2 dibatalkan", vm.SuccessMessage);
        Assert.False(vm.IsBusy);
        Assert.Single(_audit.ReadAll(), e => e.Action == AuditAction.FileEncrypted);
    }

    // --- dekripsi massal ----------------------------------------------------------------------------

    [Fact]
    public async Task DecryptAll_RestoresEveryOriginal()
    {
        var originals = new Dictionary<string, string>
        {
            [Write("m/a.txt", "isi a")] = "isi a",
            [Write("m/b.txt", "isi b")] = "isi b",
            [Write("n/c.txt", "isi c")] = "isi c",
        };
        var enc = NewEncrypt();
        enc.AddFiles(originals.Keys);
        await enc.EncryptCommand.ExecuteAsync(null);
        foreach (var p in originals.Keys) File.Delete(p);

        var dec = NewDecrypt();
        dec.AddFiles(originals.Keys.Select(p => p + ".ts4"));
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal("3 file berhasil didekripsi.", dec.SuccessMessage);
        foreach (var (path, content) in originals)
            Assert.Equal(content, await File.ReadAllTextAsync(path));
        Assert.Equal(3, _audit.ReadAll().Count(e => e.Action == AuditAction.FileDecrypted));
    }

    [Fact]
    public async Task DecryptAll_TwoFilesWithSameOriginalName_DoNotOverwriteEachOther()
    {
        var original = Write("a.txt", "isi");
        var enc = NewEncrypt();
        enc.AddFiles([original]);
        await enc.EncryptCommand.ExecuteAsync(null);
        File.Copy(original + ".ts4", Path.Combine(_workDir, "salinan.ts4"));
        File.Delete(original);

        var dec = NewDecrypt();
        dec.AddFiles([original + ".ts4", Path.Combine(_workDir, "salinan.ts4")]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.All(dec.Files, f => Assert.Equal(BatchStatus.Succeeded, f.Status));
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(_workDir, "a (2).txt")));
    }

    [Fact]
    public async Task DecryptAll_TamperedFile_FailsAndLeavesNoPartialPlaintext()
    {
        var good = Write("g.txt", "bagus");
        var bad = Write("b.txt", new string('x', 200_000));
        var enc = NewEncrypt();
        enc.AddFiles([good, bad]);
        await enc.EncryptCommand.ExecuteAsync(null);
        File.Delete(good);
        File.Delete(bad);

        var bytes = File.ReadAllBytes(bad + ".ts4");
        bytes[^20] ^= 0xFF; // rusak di ciphertext/tag chunk terakhir
        File.WriteAllBytes(bad + ".ts4", bytes);
        var before = Directory.GetFiles(_workDir).Select(Path.GetFileName).Order().ToList();

        var dec = NewDecrypt();
        dec.AddFiles([bad + ".ts4", good + ".ts4"]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal([BatchStatus.Failed, BatchStatus.Succeeded], dec.Files.Select(f => f.Status));
        Assert.False(File.Exists(bad)); // plaintext parsial tidak boleh tersisa
        Assert.True(File.Exists(good));
        // tidak ada file sementara acak yang tertinggal: satu-satunya file baru hanyalah g.txt
        var after = Directory.GetFiles(_workDir).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(before.Append("g.txt").Order(), after);
        Assert.Contains(_audit.ReadAll(), e => e.Action == AuditAction.FileDecryptFailed && e.Details.Contains("b.txt.ts4"));
    }

    [Fact]
    public async Task Decrypt_MaliciousOriginalFileName_CannotEscapeTheFolder()
    {
        var sub = Path.Combine(_workDir, "kotak");
        Directory.CreateDirectory(sub);
        var encryptedPath = Path.Combine(sub, "x.ts4");
        using (var kek = _keys.GetActiveKeyForEncryption())
        using (var input = new MemoryStream("payload"u8.ToArray()))
        using (var output = File.Create(encryptedPath))
        {
            EnvelopeCipher.Encrypt(input, output, kek, "../keluar.txt");
        }

        var dec = NewDecrypt();
        dec.AddFiles([encryptedPath]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Equal(BatchStatus.Succeeded, dec.Files.Single().Status);
        Assert.True(File.Exists(Path.Combine(sub, "keluar.txt")));
        Assert.False(File.Exists(Path.Combine(_workDir, "keluar.txt")));
    }

    [Fact]
    public async Task DecryptAll_RevokedKey_FailsWithClearMessage()
    {
        var p = Write("a.txt", "a");
        var enc = NewEncrypt();
        enc.AddFiles([p]);
        await enc.EncryptCommand.ExecuteAsync(null);
        _keys.RevokeKey("2026-09-TEST", "bocor");

        var dec = NewDecrypt();
        dec.AddFiles([p + ".ts4"]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Contains("dicabut", dec.ErrorMessage); // satu file: pesan kegagalannya langsung yang tampil
        Assert.Contains("dicabut", dec.Files.Single().Message);
    }
}
