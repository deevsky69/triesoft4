using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.ViewModels;

public partial class DecryptViewModel(MonthlyKeyManager keyManager, IAuditLog auditLog, SessionContext session)
    : BatchFileViewModelBase(auditLog, session)
{
    protected override AuditAction SuccessAction => AuditAction.FileDecrypted;
    protected override AuditAction FailureAction => AuditAction.FileDecryptFailed;
    protected override string PastTense => "didekripsi";

    protected override bool IncludeFromFolder(string path) =>
        path.EndsWith(".ts4", StringComparison.OrdinalIgnoreCase);

    protected override void NotifyRunCanExecuteChanged() => DecryptCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Decrypt() => RunBatchAsync();

    protected override BatchFileResult ProcessFile(string inputPath, ISet<string> producedThisRun, IProgress<double> progress)
    {
        var directory = Path.GetDirectoryName(inputPath) is { Length: > 0 } dir ? dir : ".";
        var tempPath = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            using var input = File.OpenRead(inputPath);
            var keyId = EnvelopeCipher.PeekKeyId(input);
            using var kek = keyManager.GetKeyForDecryption(keyId);

            DecryptResult result;
            using (var output = File.Create(tempPath))
            {
                result = EnvelopeCipher.Decrypt(input, output, kek, progress);
            }

            // Nama asli datang dari header terdekripsi. Hanya komponen nama file yang dipakai, jadi header yang memuat
            // "..\..\x" atau path absolut tidak bisa menulis keluar dari folder file .ts4 ini.
            var safeName = Path.GetFileName(result.OriginalFileName);
            if (string.IsNullOrWhiteSpace(safeName))
                throw new InvalidOperationException("Nama file asli di dalam file terenkripsi tidak valid.");

            // Dalam satu proses, dua file yang menghasilkan nama sama tidak boleh saling menimpa. File yang sudah ada dari
            // sebelum proses tetap ditimpa seperti perilaku lama (mis. mendekripsi salinan dari file asli yang masih ada).
            var destinationPath = UniquePath(directory, safeName, producedThisRun);
            File.Move(tempPath, destinationPath, overwrite: true);
            return new BatchFileResult(destinationPath, $"file={Path.GetFileName(inputPath)} -> {Path.GetFileName(destinationPath)}");
        }
        finally
        {
            // Kalau gagal di tengah (mis. tamper terdeteksi di chunk ke-N), plaintext parsial di file sementara tidak boleh tersisa.
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
