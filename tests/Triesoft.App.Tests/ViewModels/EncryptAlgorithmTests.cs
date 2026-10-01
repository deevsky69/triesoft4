using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Crypto;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

/// <summary>Pemilihan algoritma di layar Enkripsi (single-file dan bundle) benar-benar sampai ke file .ts4 dan audit log.</summary>
public class EncryptAlgorithmTests : IDisposable
{
    private readonly string _keyDir = Directory.CreateTempSubdirectory("triesoft4-algo-keys-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("triesoft4-algo-audit-").FullName;
    private readonly string _workDir = Directory.CreateTempSubdirectory("triesoft4-algo-work-").FullName;
    private readonly MonthlyKeyManager _keys;
    private readonly FileAuditLog _audit;
    private readonly SessionContext _session = new();

    public EncryptAlgorithmTests()
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

    private string Write(string name, string content)
    {
        var path = Path.Combine(_workDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void DefaultAlgorithm_IsAesGcm256()
    {
        var vm = new EncryptViewModel(_keys, _audit, _session);
        Assert.Equal(AlgorithmProfile.AesGcm256, vm.SelectedAlgorithm.Profile);
        Assert.Equal(2, vm.AvailableAlgorithms.Count);
    }

    [Fact]
    public async Task SelectingChaCha20_ProducesFileWithThatProfile_AndRecordsItInAudit()
    {
        var plaintextPath = Write("a.txt", "rahasia");
        var vm = new EncryptViewModel(_keys, _audit, _session) { SelectedAlgorithm = AlgorithmOption.ChaCha20Poly1305 };
        vm.AddFiles([plaintextPath]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        using var input = File.OpenRead(plaintextPath + ".ts4");
        using var output = new MemoryStream();
        using var kek = _keys.GetActiveKeyForEncryption();
        var result = EnvelopeCipher.Decrypt(input, output, kek);
        Assert.Equal(AlgorithmProfile.ChaCha20Poly1305, result.Profile);

        var entry = Assert.Single(_audit.ReadAll());
        Assert.Contains("algoritma=ChaCha20Poly1305", entry.Details);
    }

    [Fact]
    public async Task Bundle_WithChaCha20_ProducesBundleWithThatProfile_AndRecordsItInAudit()
    {
        var a = Write("a.txt", "satu");
        var vm = new EncryptViewModel(_keys, _audit, _session)
        {
            BundleMode = true,
            BundleName = "paket",
            SelectedAlgorithm = AlgorithmOption.ChaCha20Poly1305,
        };
        vm.AddFiles([a]);

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        var bundlePath = Path.Combine(_workDir, "paket.ts4");
        using var input = File.OpenRead(bundlePath);
        using var output = new MemoryStream();
        using var kek = _keys.GetActiveKeyForEncryption();
        var result = EnvelopeCipher.Decrypt(input, output, kek);
        Assert.Equal(AlgorithmProfile.ChaCha20Poly1305, result.Profile);

        var entry = Assert.Single(_audit.ReadAll());
        Assert.Contains("algoritma=ChaCha20Poly1305", entry.Details);
    }

    [Fact]
    public async Task Decrypt_DoesNotNeedAlgorithmChosen_AutoDetectsFromHeader_AndRecordsItInAudit()
    {
        var plaintextPath = Write("a.txt", "rahasia");
        var enc = new EncryptViewModel(_keys, _audit, _session) { SelectedAlgorithm = AlgorithmOption.ChaCha20Poly1305 };
        enc.AddFiles([plaintextPath]);
        await enc.EncryptCommand.ExecuteAsync(null);
        File.Delete(plaintextPath);

        var dec = new DecryptViewModel(_keys, _audit, _session);
        dec.AddFiles([plaintextPath + ".ts4"]);
        await dec.DecryptCommand.ExecuteAsync(null);

        Assert.Null(dec.ErrorMessage);
        Assert.Equal("rahasia", await File.ReadAllTextAsync(plaintextPath));
        var decryptEntry = Assert.Single(_audit.ReadAll(), e => e.Action == AuditAction.FileDecrypted);
        Assert.Contains("algoritma=ChaCha20Poly1305", decryptEntry.Details);
    }
}
