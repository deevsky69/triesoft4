using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.Bundle;
using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.ViewModels;

public partial class EncryptViewModel(MonthlyKeyManager keyManager, IAuditLog auditLog, SessionContext session)
    : BatchFileViewModelBase(auditLog, session)
{
    private const int MaxNamesInAudit = 50;

    protected override AuditAction SuccessAction => AuditAction.FileEncrypted;
    protected override AuditAction FailureAction => AuditAction.FileEncryptFailed;
    protected override string PastTense => "dienkripsi";

    /// <summary>Gabungkan semua file di daftar menjadi SATU file .ts4 (bundle) alih-alih satu .ts4 per file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunButtonText))]
    private bool _bundleMode;

    [ObservableProperty]
    private string _bundleName = $"bundle-{DateTime.Now:yyyyMMdd-HHmmss}";

    /// <summary>Folder tujuan yang dipilih pengguna; kosong berarti memakai folder file pertama di daftar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveBundleFolder))]
    private string? _bundleOutputFolder;

    public string EffectiveBundleFolder => BundleOutputFolder ?? Files.FirstOrDefault()?.DefaultOutputFolder ?? "";

    public string RunButtonText => BundleMode ? "Buat Bundle" : "Enkripsi Semua";

    protected override void OnFilesChanged() => OnPropertyChanged(nameof(EffectiveBundleFolder));

    // Folder: file .ts4 dilewati supaya tidak terenkripsi dua kali tanpa sengaja (file yang dipilih satu per satu tetap dibolehkan).
    protected override bool IncludeFromFolder(string path) =>
        !path.EndsWith(".ts4", StringComparison.OrdinalIgnoreCase);

    protected override void NotifyRunCanExecuteChanged() => EncryptCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Encrypt() => BundleMode ? RunBundleAsync() : RunBatchAsync();

    // --- satu file = satu .ts4 --------------------------------------------------------------------------

    protected override BatchFileResult ProcessFile(string inputPath, ISet<string> producedThisRun, IProgress<double> progress)
    {
        // Nama asli berakhiran .ts4bundle menandai bundle saat dekripsi. Mengenkripsi file biasa dengan nama itu
        // akan membuatnya dianggap bundle, jadi ditolak di sini (ganti nama, atau masukkan ke bundle).
        if (BundleNames.IsBundleFileName(inputPath))
        {
            throw new InvalidOperationException(
                $"Ekstensi {BundleNames.Extension} dicadangkan untuk bundle. Ganti nama file ini, atau masukkan ke bundle.");
        }

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

    // --- semua file = satu .ts4 (bundle) ----------------------------------------------------------------

    /// <summary>
    /// Bundle bersifat atomik: kalau ada satu file yang tidak bisa dibaca, TIDAK ADA bundle yang dibuat. Bundle yang diam-diam
    /// kehilangan sebuah file berbahaya untuk berita rahasia (pengirim mengira semuanya terkirim).
    /// </summary>
    private async Task RunBundleAsync()
    {
        if (IsBusy || Files.Count == 0) return;
        ClearMessages();

        var bundleName = BundleName.Trim();
        if (bundleName.EndsWith(".ts4", StringComparison.OrdinalIgnoreCase)) bundleName = bundleName[..^4];
        if (bundleName.Length == 0)
        {
            ErrorMessage = "Nama bundle wajib diisi.";
            return;
        }
        bundleName = BundleNames.Sanitize(bundleName);

        var folder = EffectiveBundleFolder;
        if (!Directory.Exists(folder))
        {
            ErrorMessage = "Folder tujuan bundle tidak ditemukan.";
            return;
        }

        if (Files.Count > BundleNames.MaxEntries)
        {
            ErrorMessage = $"Terlalu banyak file untuk satu bundle ({Files.Count}; maksimum {BundleNames.MaxEntries}). Bagi menjadi beberapa bundle.";
            return;
        }

        foreach (var f in Files)
        {
            f.Status = BatchStatus.Waiting;
            f.Message = "";
        }

        // Path tiap file di dalam bundle: file tunggal di akar, file dari folder dengan struktur subfoldernya.
        var entryNames = BundleNames.PlanEntryPaths(
            Files.Select(f => new BundlePlanItem(f.GroupId, f.GroupName, f.RelativePath, f.FileName)).ToList());

        // Pemeriksaan awal: setiap file harus bisa dibuka dan path-nya valid sebelum apa pun ditulis.
        var unreadable = new List<BatchFileItem>();
        for (var i = 0; i < Files.Count; i++)
        {
            var item = Files[i];
            try
            {
                if (BundleNames.ValidateEntryPath(entryNames[i]) is { } pathProblem)
                    throw new InvalidOperationException($"Path di dalam bundle tidak valid ({pathProblem}): {entryNames[i]}");
                using var probe = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex)
            {
                var reason = Describe(ex);
                item.Message = reason;
                item.Status = BatchStatus.Failed;
                Record(FailureAction, $"bundle={bundleName}, file={item.FileName}, error={reason}");
                unreadable.Add(item);
            }
        }

        if (unreadable.Count > 0)
        {
            foreach (var item in Files.Where(f => f.Status == BatchStatus.Waiting))
            {
                item.Status = BatchStatus.Canceled;
                item.Message = "Bundle tidak dibuat karena ada file lain yang bermasalah.";
            }
            ErrorMessage = $"Bundle tidak dibuat: {unreadable.Count} file bermasalah. Perbaiki atau hapus dari daftar, lalu ulangi.";
            return;
        }

        var sources = Files.Zip(entryNames, (f, name) => new BundleSource(f.Path, name)).ToList();
        var outputPath = FirstFreeBundlePath(folder, bundleName);

        using var cts = BeginRun(); // sebelum status Running: Batal sudah berlaku sejak proses dimulai
        SetBusy(true);
        Progress = 0;
        foreach (var f in Files) f.Status = BatchStatus.Running;
        var reporter = new Progress<double>(p => Progress = p);

        try
        {
            var keyId = await Task.Run(() => WriteBundle(sources, outputPath, bundleName, reporter, cts.Token));

            Record(SuccessAction, $"bundle={bundleName}, files={sources.Count}, keyId={keyId}, isi={ListForAudit(entryNames)}");
            foreach (var f in Files)
            {
                f.Message = outputPath;
                f.Status = BatchStatus.Succeeded;
            }
            Progress = 1;
            SuccessMessage = $"Bundle berisi {sources.Count} file berhasil dibuat -> {outputPath}";
        }
        catch (OperationCanceledException)
        {
            Record(FailureAction, $"bundle={bundleName}, error=dibatalkan pengguna");
            foreach (var f in Files)
            {
                f.Message = "Dibatalkan.";
                f.Status = BatchStatus.Canceled;
            }
            Progress = 0;
            SuccessMessage = "Pembuatan bundle dibatalkan. Tidak ada file yang dibuat.";
        }
        catch (Exception ex)
        {
            var reason = Describe(ex);
            Record(FailureAction, $"bundle={bundleName}, error={reason}");
            foreach (var f in Files)
            {
                f.Message = reason;
                f.Status = BatchStatus.Failed;
            }
            Progress = 0;
            ErrorMessage = reason;
        }
        finally
        {
            EndRun();
            SetBusy(false);
        }
    }

    /// <summary>Menulis bundle ke file sementara lalu memindahkannya, jadi hasil setengah jadi tidak pernah muncul sebagai .ts4 yang valid.</summary>
    private string WriteBundle(IReadOnlyList<BundleSource> sources, string outputPath, string bundleName, IProgress<double> progress, CancellationToken cancellation)
    {
        var partialPath = outputPath + ".partial";
        try
        {
            using var kek = keyManager.GetActiveKeyForEncryption();
            using (var input = new BundleReadStream(sources, cancellation))
            using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                EnvelopeCipher.Encrypt(input, output, kek, bundleName + BundleNames.Extension, progress: progress);
            }

            File.Move(partialPath, outputPath);
            return kek.KeyId;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    /// <summary>"nama.ts4", lalu "nama (2).ts4", ... -- bundle baru tidak pernah menimpa bundle yang sudah ada.</summary>
    private static string FirstFreeBundlePath(string folder, string name)
    {
        var candidate = Path.Combine(folder, name + ".ts4");
        for (var i = 2; File.Exists(candidate) || File.Exists(candidate + ".partial"); i++)
            candidate = Path.Combine(folder, $"{name} ({i}).ts4");
        return candidate;
    }

    private static string ListForAudit(IReadOnlyList<string> names) =>
        names.Count <= MaxNamesInAudit
            ? string.Join(" | ", names)
            : string.Join(" | ", names.Take(MaxNamesInAudit)) + $" | ...(+{names.Count - MaxNamesInAudit})";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
