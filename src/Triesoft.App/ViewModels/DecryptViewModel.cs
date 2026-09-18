using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.ViewModels;

public partial class DecryptViewModel(MonthlyKeyManager keyManager, IAuditLog auditLog, SessionContext session) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DecryptCommand))]
    private string? _selectedFilePath;

    [ObservableProperty]
    private double _progress;

    public void SetSelectedFile(string path)
    {
        SelectedFilePath = path;
        ClearMessages();
    }

    [RelayCommand(CanExecute = nameof(CanDecrypt))]
    private async Task Decrypt()
    {
        if (IsBusy) return;
        ClearMessages();
        IsBusy = true;
        Progress = 0;
        var actor = session.CurrentUser?.Username ?? "unknown";
        var inputPath = SelectedFilePath!;
        try
        {
            var reporter = new Progress<double>(p => Progress = p);

            var finalPath = await Task.Run(() =>
            {
                using var input = File.OpenRead(inputPath);
                var keyId = EnvelopeCipher.PeekKeyId(input);
                using var kek = keyManager.GetKeyForDecryption(keyId);

                var directory = Path.GetDirectoryName(inputPath) is { Length: > 0 } dir ? dir : ".";
                var tempPath = Path.Combine(directory, Path.GetRandomFileName());
                DecryptResult result;
                using (var output = File.Create(tempPath))
                {
                    result = EnvelopeCipher.Decrypt(input, output, kek, reporter);
                }

                var destinationPath = Path.Combine(directory, result.OriginalFileName);
                File.Move(tempPath, destinationPath, overwrite: true);
                return destinationPath;
            });

            auditLog.Record(actor, AuditAction.FileDecrypted, $"file={Path.GetFileName(inputPath)} -> {Path.GetFileName(finalPath)}");
            SuccessMessage = $"Berhasil didekripsi -> {finalPath}";
        }
        catch (Exception ex)
        {
            // Dekripsi gagal (termasuk deteksi tamper AEAD) sengaja tetap diaudit -- ini justru
            // salah satu sinyal paling penting untuk investigasi.
            auditLog.Record(actor, AuditAction.FileDecryptFailed, $"file={Path.GetFileName(inputPath)}, error={ex.Message}");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDecrypt() => !string.IsNullOrWhiteSpace(SelectedFilePath);
}
