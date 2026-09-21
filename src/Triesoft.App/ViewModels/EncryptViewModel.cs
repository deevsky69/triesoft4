using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.ViewModels;

public partial class EncryptViewModel(MonthlyKeyManager keyManager, IAuditLog auditLog, SessionContext session)
    : BatchFileViewModelBase(auditLog, session)
{
    protected override AuditAction SuccessAction => AuditAction.FileEncrypted;
    protected override AuditAction FailureAction => AuditAction.FileEncryptFailed;
    protected override string PastTense => "dienkripsi";

    // Folder: file .ts4 dilewati supaya tidak terenkripsi dua kali tanpa sengaja (file yang dipilih satu per satu tetap dibolehkan).
    protected override bool IncludeFromFolder(string path) =>
        !path.EndsWith(".ts4", StringComparison.OrdinalIgnoreCase);

    protected override void NotifyRunCanExecuteChanged() => EncryptCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Encrypt() => RunBatchAsync();

    protected override BatchFileResult ProcessFile(string inputPath, ISet<string> producedThisRun, IProgress<double> progress)
    {
        var outputPath = inputPath + ".ts4";
        var outputCreated = false;
        try
        {
            // Kunci dimuat per file: satu DPAPI unprotect kecil, dan kunci aktif yang berubah di tengah proses tetap konsisten per file.
            using var kek = keyManager.GetActiveKeyForEncryption();
            using var input = File.OpenRead(inputPath);
            using var output = File.Create(outputPath);
            outputCreated = true;
            EnvelopeCipher.Encrypt(input, output, kek, Path.GetFileName(inputPath), progress: progress);

            producedThisRun.Add(outputPath);
            return new BatchFileResult(outputPath, $"file={Path.GetFileName(inputPath)}, keyId={kek.KeyId}");
        }
        catch
        {
            // Jangan tinggalkan .ts4 setengah jadi. Hanya dihapus kalau memang kita yang membuatnya
            // (kalau gagal sebelum File.Create, .ts4 lama milik pengguna tidak boleh disentuh).
            if (outputCreated) TryDelete(outputPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
