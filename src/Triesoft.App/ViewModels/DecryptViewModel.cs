using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.Bundle;
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

    // Sama dengan filter pemilih file: hanya .ts4 yang bisa didekripsi.
    protected override bool AcceptsFile(string path) => IncludeFromFolder(path);

    protected override string AcceptedDescription => "file .ts4";

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

            // Sampai di sini SELURUH file sudah lolos autentikasi. Bundle baru dibuka setelah itu, tidak pernah selagi mengalir.
            if (BundleNames.IsBundleFileName(result.OriginalFileName) && LooksLikeBundle(tempPath))
                return ExtractBundle(tempPath, directory, inputPath, result.OriginalFileName, producedThisRun);

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
            // Kalau gagal di tengah (mis. tamper terdeteksi di chunk ke-N), atau isinya bundle yang sudah diekstrak,
            // plaintext di file sementara tidak boleh tersisa.
            DeleteQuietly(tempPath);
        }
    }

    /// <summary>
    /// Mengekstrak bundle ke folder staging dulu, baru dipindah ke folder bernama bundle. Kalau bundle ditolak atau gagal
    /// di tengah, staging dibuang seluruhnya: tidak ada hasil setengah jadi. Folder tujuan yang sudah ada tidak pernah digabung.
    /// </summary>
    private static BatchFileResult ExtractBundle(string plaintextPath, string directory, string inputPath, string bundleFileName, ISet<string> producedThisRun)
    {
        var target = UniqueDirectory(directory, BundleNames.FolderNameFor(bundleFileName), producedThisRun);
        var staging = Path.Combine(directory, Path.GetRandomFileName());
        Directory.CreateDirectory(staging);
        try
        {
            IReadOnlyList<string> names;
            using (var plain = File.OpenRead(plaintextPath))
                names = BundleExtractor.Extract(plain, staging);

            Directory.Move(staging, target);
            return new BatchFileResult(target,
                $"file={Path.GetFileName(inputPath)} -> folder {Path.GetFileName(target)} ({names.Count} file: {string.Join(" | ", names.Take(50))})");
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static bool LooksLikeBundle(string path)
    {
        using var stream = File.OpenRead(path);
        return BundleExtractor.LooksLikeBundle(stream);
    }

    private static void DeleteQuietly(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
