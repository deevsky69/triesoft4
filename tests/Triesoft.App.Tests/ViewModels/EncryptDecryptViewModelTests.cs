using Triesoft.App.ViewModels;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

public class EncryptDecryptViewModelTests : IDisposable
{
    private readonly string _keyStoreDir = Directory.CreateTempSubdirectory("triesoft4-enc-vm-keys-").FullName;
    private readonly string _workDir = Directory.CreateTempSubdirectory("triesoft4-enc-vm-work-").FullName;
    private readonly MonthlyKeyManager _keyManager;

    public EncryptDecryptViewModelTests()
    {
        var store = new FileKeyStore(_keyStoreDir, new PassthroughKeyProtector());
        _keyManager = new MonthlyKeyManager(store);
        _keyManager.ImportMonthlyKey("2026-09-TEST", new byte[32], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
    }

    [Fact]
    public void EncryptCommand_CannotExecute_WithoutSelectedFile()
    {
        var vm = new EncryptViewModel(_keyManager);
        Assert.False(vm.EncryptCommand.CanExecute(null));
    }

    [Fact]
    public async Task EncryptCommand_ProducesTs4File_AndSuccessMessage()
    {
        var plaintextPath = Path.Combine(_workDir, "dokumen.txt");
        await File.WriteAllTextAsync(plaintextPath, "isi rahasia untuk diuji");

        var vm = new EncryptViewModel(_keyManager);
        vm.SetSelectedFile(plaintextPath);
        Assert.True(vm.EncryptCommand.CanExecute(null));

        await vm.EncryptCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(vm.SuccessMessage);
        Assert.True(File.Exists(plaintextPath + ".ts4"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task EncryptThenDecrypt_RoundTrips_ToOriginalContent()
    {
        var plaintextPath = Path.Combine(_workDir, "laporan.txt");
        const string originalContent = "berita rahasia untuk round-trip test";
        await File.WriteAllTextAsync(plaintextPath, originalContent);

        var encryptVm = new EncryptViewModel(_keyManager);
        encryptVm.SetSelectedFile(plaintextPath);
        await encryptVm.EncryptCommand.ExecuteAsync(null);

        var encryptedPath = plaintextPath + ".ts4";
        File.Delete(plaintextPath); // pastikan hasil dekripsi memang dari file terenkripsi, bukan sisa file asli

        var decryptVm = new DecryptViewModel(_keyManager);
        decryptVm.SetSelectedFile(encryptedPath);
        Assert.True(decryptVm.DecryptCommand.CanExecute(null));

        await decryptVm.DecryptCommand.ExecuteAsync(null);

        Assert.Null(decryptVm.ErrorMessage);
        Assert.NotNull(decryptVm.SuccessMessage);
        Assert.True(File.Exists(plaintextPath));
        Assert.Equal(originalContent, await File.ReadAllTextAsync(plaintextPath));
    }

    [Fact]
    public async Task DecryptCommand_RevokedKey_SetsClearErrorMessage()
    {
        var plaintextPath = Path.Combine(_workDir, "akan-dicabut.txt");
        await File.WriteAllTextAsync(plaintextPath, "data");

        var encryptVm = new EncryptViewModel(_keyManager);
        encryptVm.SetSelectedFile(plaintextPath);
        await encryptVm.EncryptCommand.ExecuteAsync(null);

        _keyManager.RevokeKey("2026-09-TEST", "diduga bocor");

        var decryptVm = new DecryptViewModel(_keyManager);
        decryptVm.SetSelectedFile(plaintextPath + ".ts4");
        await decryptVm.DecryptCommand.ExecuteAsync(null);

        Assert.NotNull(decryptVm.ErrorMessage);
        Assert.Contains("dicabut", decryptVm.ErrorMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_keyStoreDir)) Directory.Delete(_keyStoreDir, recursive: true);
        if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true);
    }
}
