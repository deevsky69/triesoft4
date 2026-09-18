using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.ViewModels;

public partial class EncryptViewModel(MonthlyKeyManager keyManager, IAuditLog auditLog, SessionContext session) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EncryptCommand))]
    private string? _selectedFilePath;

    [ObservableProperty]
    private double _progress;

    public void SetSelectedFile(string path)
    {
        SelectedFilePath = path;
        ClearMessages();
    }

    [RelayCommand(CanExecute = nameof(CanEncrypt))]
    private async Task Encrypt()
    {
        if (IsBusy) return;
        ClearMessages();
        IsBusy = true;
        Progress = 0;
        var actor = session.CurrentUser?.Username ?? "unknown";
        var inputPath = SelectedFilePath!;
        try
        {
            var outputPath = inputPath + ".ts4";
            var reporter = new Progress<double>(p => Progress = p);

            using var kek = keyManager.GetActiveKeyForEncryption();
            await Task.Run(() =>
            {
                using var input = File.OpenRead(inputPath);
                using var output = File.Create(outputPath);
                EnvelopeCipher.Encrypt(input, output, kek, Path.GetFileName(inputPath), progress: reporter);
            });

            auditLog.Record(actor, AuditAction.FileEncrypted, $"file={Path.GetFileName(inputPath)}, keyId={kek.KeyId}");
            SuccessMessage = $"Berhasil dienkripsi -> {outputPath}";
        }
        catch (Exception ex)
        {
            auditLog.Record(actor, AuditAction.FileEncryptFailed, $"file={Path.GetFileName(inputPath)}, error={ex.Message}");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanEncrypt() => !string.IsNullOrWhiteSpace(SelectedFilePath);
}
