using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;

namespace Triesoft.App.ViewModels;

/// <summary>Hasil satu file: path output dan detail untuk audit log.</summary>
public sealed record BatchFileResult(string OutputPath, string AuditDetails);

/// <summary>
/// Dasar layar enkripsi/dekripsi massal: daftar file, proses satu per satu, status per file, pembatalan.
/// Satu file gagal tidak menghentikan yang lain, dan tiap file diaudit sendiri-sendiri (termasuk yang gagal).
/// </summary>
public abstract partial class BatchFileViewModelBase(IAuditLog auditLog, SessionContext session) : ViewModelBase
{
    private CancellationTokenSource? _cts;

    public ObservableCollection<BatchFileItem> Files { get; } = [];

    [ObservableProperty]
    private double _progress;

    public bool HasFiles => Files.Count > 0;

    public string FileCountText => Files.Count switch
    {
        0 => "Belum ada file dipilih",
        var n => $"{n} file dalam daftar",
    };

    protected abstract AuditAction SuccessAction { get; }
    protected abstract AuditAction FailureAction { get; }

    /// <summary>Kata kerja lampau untuk pesan ringkasan, mis. "dienkripsi".</summary>
    protected abstract string PastTense { get; }

    /// <summary>Memproses satu file. Melempar exception kalau gagal. <paramref name="producedThisRun"/> berisi output yang sudah dibuat di proses ini.</summary>
    protected abstract BatchFileResult ProcessFile(string inputPath, ISet<string> producedThisRun, IProgress<double> progress);

    /// <summary>Apakah file ini ikut saat "Tambah Folder". Bawaan: semua file.</summary>
    protected virtual bool IncludeFromFolder(string path) => true;

    /// <summary>Apakah file yang dijatuhkan (drag and drop) atau dipilih langsung boleh masuk daftar. Bawaan: semua file.</summary>
    protected virtual bool AcceptsFile(string path) => true;

    /// <summary>Uraian jenis file yang diterima, untuk pesan kalau ada yang dilewati (mis. "file .ts4").</summary>
    protected virtual string AcceptedDescription => "file";

    /// <summary>
    /// Drag and drop dari File Explorer: file diterima kalau <see cref="AcceptsFile"/> setuju, folder ditambahkan isinya
    /// (tanpa subfolder, sama seperti "Tambah Folder"). Yang tidak diterima dilewati dan dilaporkan, bukan diam-diam dibuang.
    /// Mengembalikan jumlah yang ditambahkan.
    /// </summary>
    public int AddDropped(IEnumerable<string> paths)
    {
        if (IsBusy) return 0;
        ClearMessages();

        var accepted = new List<string>();
        var skipped = 0;
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (Directory.Exists(path))
            {
                try
                {
                    var inside = Directory.EnumerateFiles(path).Where(IncludeFromFolder).Order(StringComparer.OrdinalIgnoreCase).ToList();
                    accepted.AddRange(inside);
                }
                catch (Exception ex)
                {
                    ErrorMessage = ex.Message;
                }
            }
            else if (File.Exists(path) && AcceptsFile(path))
            {
                accepted.Add(path);
            }
            else
            {
                skipped++;
            }
        }

        var added = AddFiles(accepted);
        if (skipped > 0)
            ErrorMessage = $"{skipped} item dilewati karena bukan {AcceptedDescription}.";
        return added;
    }

    /// <summary>Dipanggil saat daftar atau status sibuk berubah, supaya turunan memperbarui CanExecute perintah jalannya.</summary>
    protected abstract void NotifyRunCanExecuteChanged();

    protected bool CanRun() => HasFiles && !IsBusy;

    /// <summary>Menambah file ke daftar. File yang sudah ada di daftar dilewati. Mengembalikan jumlah yang benar-benar ditambahkan.</summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return 0;
        ClearMessages();
        var added = 0;
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var full = Path.GetFullPath(path);
            if (Files.Any(f => string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase))) continue;
            Files.Add(new BatchFileItem(full, RemoveFile));
            added++;
        }
        NotifyListChanged();
        return added;
    }

    /// <summary>Menambah semua file di satu folder (tanpa subfolder).</summary>
    public int AddFolder(string folderPath)
    {
        try
        {
            return AddFiles(Directory.EnumerateFiles(folderPath).Where(IncludeFromFolder).Order(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return 0;
        }
    }

    private void RemoveFile(BatchFileItem item)
    {
        if (IsBusy) return;
        Files.Remove(item);
        NotifyListChanged();
    }

    [RelayCommand]
    private void ClearFiles()
    {
        if (IsBusy) return;
        Files.Clear();
        Progress = 0;
        ClearMessages();
        NotifyListChanged();
    }

    /// <summary>Menghentikan setelah file yang sedang diproses selesai. Sisanya ditandai Dibatalkan.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Dipanggil setiap isi daftar berubah, untuk turunan yang punya properti turunan dari daftar.</summary>
    protected virtual void OnFilesChanged() { }

    protected string Actor => session.CurrentUser?.Username ?? "unknown";

    protected void Record(AuditAction action, string details) => auditLog.Record(Actor, action, details);

    /// <summary>Memulai satu proses yang bisa dibatalkan lewat tombol Batal. Pasangkan dengan <see cref="EndRun"/> di finally.</summary>
    protected CancellationTokenSource BeginRun()
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        return cts;
    }

    protected void EndRun() => _cts = null;

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(FileCountText));
        OnFilesChanged();
        NotifyRunCanExecuteChanged();
    }

    protected void SetBusy(bool busy)
    {
        IsBusy = busy;
        foreach (var f in Files) f.IsRemovable = !busy;
        NotifyRunCanExecuteChanged();
    }

    protected async Task RunBatchAsync()
    {
        if (IsBusy || Files.Count == 0) return;
        ClearMessages();

        // File yang sudah berhasil tidak diproses ulang: menekan tombol lagi = ulangi yang gagal/dibatalkan/baru.
        var pending = Files.Where(f => f.Status != BatchStatus.Succeeded).ToList();
        if (pending.Count == 0)
        {
            SuccessMessage = "Semua file di daftar sudah diproses. Tambah file baru atau kosongkan daftar.";
            return;
        }

        SetBusy(true);
        Progress = 0;
        foreach (var f in pending)
        {
            f.Status = BatchStatus.Waiting;
            f.Message = "";
        }

        var produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cts = BeginRun();
        var done = 0;

        try
        {
            foreach (var item in pending)
            {
                if (cts.IsCancellationRequested)
                {
                    item.Status = BatchStatus.Canceled;
                    item.Message = "Dibatalkan sebelum diproses.";
                    continue;
                }

                item.Status = BatchStatus.Running;
                var completed = done;
                var reporter = new Progress<double>(p => Progress = (completed + p) / pending.Count);
                try
                {
                    var result = await Task.Run(() => ProcessFile(item.Path, produced, reporter));
                    Record(SuccessAction, result.AuditDetails);
                    item.Message = result.OutputPath;
                    item.Status = BatchStatus.Succeeded;
                }
                catch (Exception ex)
                {
                    // Kegagalan (termasuk deteksi tamper AEAD) sengaja tetap diaudit -- salah satu sinyal terpenting untuk investigasi.
                    var reason = Describe(ex);
                    Record(FailureAction, $"file={item.FileName}, error={reason}");
                    item.Message = reason;
                    item.Status = BatchStatus.Failed;
                }

                done++;
                Progress = (double)done / pending.Count;
            }
        }
        finally
        {
            EndRun();
            SetBusy(false);
        }

        Summarize(pending);
    }

    private void Summarize(List<BatchFileItem> processed)
    {
        var ok = processed.Count(f => f.Status == BatchStatus.Succeeded);
        var failed = processed.Count(f => f.Status == BatchStatus.Failed);
        var canceled = processed.Count(f => f.Status == BatchStatus.Canceled);

        if (processed.Count == 1)
        {
            var only = processed[0];
            if (only.Status == BatchStatus.Succeeded) SuccessMessage = $"Berhasil {PastTense} -> {only.Message}";
            else if (only.Status == BatchStatus.Failed) ErrorMessage = only.Message;
            else SuccessMessage = "Dibatalkan.";
            return;
        }

        if (failed == 0 && canceled == 0)
        {
            SuccessMessage = $"{ok} file berhasil {PastTense}.";
            return;
        }

        var parts = new List<string> { $"{ok} berhasil" };
        if (failed > 0) parts.Add($"{failed} gagal");
        if (canceled > 0) parts.Add($"{canceled} dibatalkan");
        var text = $"Dari {processed.Count} file: {string.Join(", ", parts)}. Alasan tiap file ada di daftar.";
        if (failed > 0) ErrorMessage = text;
        else SuccessMessage = text;
    }

    /// <summary>Pesan kegagalan berbahasa Indonesia untuk kasus berkas yang umum; sisanya memakai pesan aslinya.</summary>
    protected static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => "File tidak ditemukan.",
        UnauthorizedAccessException => "Akses ke file atau folder ditolak.",
        _ => ex.Message,
    };

    /// <summary>Folder baru yang belum ada di disk dan belum dipakai di proses ini: "nama", "nama (2)", "nama (3)", ... Tidak pernah menggabung ke folder yang sudah ada.</summary>
    protected static string UniqueDirectory(string parent, string name, ISet<string> producedThisRun)
    {
        var candidate = Path.Combine(parent, name);
        for (var i = 2; Directory.Exists(candidate) || File.Exists(candidate) || !producedThisRun.Add(candidate); i++)
            candidate = Path.Combine(parent, $"{name} ({i})");
        return candidate;
    }

    /// <summary>Nama output yang belum dipakai di proses ini: "nama.ext" lalu "nama (2).ext", "nama (3).ext", ...</summary>
    protected static string UniquePath(string directory, string fileName, ISet<string> producedThisRun)
    {
        var candidate = Path.Combine(directory, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; !producedThisRun.Add(candidate); i++)
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
        return candidate;
    }
}
