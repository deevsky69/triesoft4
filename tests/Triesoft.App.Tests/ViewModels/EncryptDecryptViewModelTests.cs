using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

public class EncryptDecryptViewModelTests : IDisposable
{
    private readonly string _keyStoreDir = Directory.CreateTempSubdirectory("triesoft4-enc-vm-keys-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("triesoft4-enc-vm-audit-").FullName;
    private readonly string _workDir = Directory.CreateTempSubdirectory("triesoft4-enc-vm-work-").FullName;
    private readonly MonthlyKeyManager _keyManager;
    private readonly FileAuditLog _auditLog;
    private readonly SessionContext _session = new();

    public EncryptDecryptViewModelTests()
    {
        var store = new FileKeyStore(_keyStoreDir, new PassthroughKeyProtector());
        _keyManager = new MonthlyKeyManager(store);
        _keyManager.ImportMonthlyKey("2026-09-TEST", new byte[32], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
        _auditLog = new FileAuditLog(_auditDir);

        _session.SignIn(new UserAccount("op1", "Operator Satu", UserRole.Operator, UserStatus.Active,
            [], [], DateTimeOffset.UtcNow, "admin1"));
    }

    private EncryptViewModel NewEncryptViewModel() => new(_keyManager, _auditLog, _session);
    private DecryptViewModel NewDecryptViewModel() => new(_keyManager, _auditLog, _session);

    [Fact]
    public void EncryptCommand_CannotExecute_WithoutSelectedFile()
    {
        var vm = NewEncryptViewModel();
        Assert.False(vm.EncryptCommand.CanExecute(null));
    }

    [Fact]
    public async Task EncryptCommand_ProducesTs4File_AndSuccessMessage_AndRecordsAudit()
    {
        var plaintextPath = Path.Combine(_workDir, "dokumen.txt");
        await File.WriteAllTextAsync(plaintextPath, "isi rahasia untuk diuji");

        var vm = NewEncryptViewModel();
        vm.SetSelectedFile(plaintextPath);
        Assert.True(vm.EncryptCommand.CanExecute(null));

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(vm.SuccessMessage);
        Assert.True(File.Exists(plaintextPath + ".ts4"));
        Assert.False(vm.IsBusy);

        var entry = Assert.Single(_auditLog.ReadAll());
        Assert.Equal(AuditAction.FileEncrypted, entry.Action);
        Assert.Equal("op1", entry.Actor);
    }

    [Fact]
    public async Task EncryptThenDecrypt_RoundTrips_ToOriginalContent_AndRecordsBothAuditEntries()
    {
        var plaintextPath = Path.Combine(_workDir, "laporan.txt");
        const string originalContent = "berita rahasia untuk round-trip test";
        await File.WriteAllTextAsync(plaintextPath, originalContent);

        var encryptVm = NewEncryptViewModel();
        encryptVm.SetSelectedFile(plaintextPath);
        await encryptVm.EncryptCommand.ExecuteAsync(null);

        var encryptedPath = plaintextPath + ".ts4";
        File.Delete(plaintextPath); // pastikan hasil dekripsi memang dari file terenkripsi, bukan sisa file asli

        var decryptVm = NewDecryptViewModel();
        decryptVm.SetSelectedFile(encryptedPath);
        Assert.True(decryptVm.DecryptCommand.CanExecute(null));

        await decryptVm.DecryptCommand.ExecuteAsync(null);

        Assert.Null(decryptVm.ErrorMessage);
        Assert.NotNull(decryptVm.SuccessMessage);
        Assert.True(File.Exists(plaintextPath));
        Assert.Equal(originalContent, await File.ReadAllTextAsync(plaintextPath));

        var entries = _auditLog.ReadAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal(AuditAction.FileEncrypted, entries[0].Action);
        Assert.Equal(AuditAction.FileDecrypted, entries[1].Action);
        Assert.True(_auditLog.VerifyChain().IsIntact);
    }

    [Fact]
    public async Task DecryptCommand_RevokedKey_SetsClearErrorMessage_AndRecordsFailureAudit()
    {
        var plaintextPath = Path.Combine(_workDir, "akan-dicabut.txt");
        await File.WriteAllTextAsync(plaintextPath, "data");

        var encryptVm = NewEncryptViewModel();
        encryptVm.SetSelectedFile(plaintextPath);
        await encryptVm.EncryptCommand.ExecuteAsync(null);

        _keyManager.RevokeKey("2026-09-TEST", "diduga bocor");

        var decryptVm = NewDecryptViewModel();
        decryptVm.SetSelectedFile(plaintextPath + ".ts4");
        await decryptVm.DecryptCommand.ExecuteAsync(null);

        Assert.NotNull(decryptVm.ErrorMessage);
        Assert.Contains("dicabut", decryptVm.ErrorMessage);

        var entries = _auditLog.ReadAll();
        Assert.Equal(AuditAction.FileDecryptFailed, entries[^1].Action);
    }

    public void Dispose()
    {
        if (Directory.Exists(_keyStoreDir)) Directory.Delete(_keyStoreDir, recursive: true);
        if (Directory.Exists(_auditDir)) Directory.Delete(_auditDir, recursive: true);
        if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true);
    }
}
