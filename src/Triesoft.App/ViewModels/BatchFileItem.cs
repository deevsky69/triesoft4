using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Triesoft.App.ViewModels;

public enum BatchStatus
{
    Waiting,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>Satu baris di daftar enkripsi/dekripsi massal: file input, status, dan hasil atau alasan gagalnya.</summary>
public partial class BatchFileItem : ObservableObject
{
    public BatchFileItem(string path, Action<BatchFileItem> remove, string? groupId = null, string? groupName = null, string? relativePath = null)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? "";
        GroupId = groupId;
        GroupName = groupName;
        RelativePath = relativePath;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }

    /// <summary>Penanda folder asal kalau file ini ditambahkan lewat folder (untuk struktur subfolder di bundle); null kalau file tunggal.</summary>
    public string? GroupId { get; }

    /// <summary>Nama folder yang ditambahkan (komponen pertama path di dalam bundle).</summary>
    public string? GroupName { get; }

    /// <summary>Path relatif terhadap folder yang ditambahkan, dipisah "/" ("sub/data.xlsx"); null untuk file tunggal.</summary>
    public string? RelativePath { get; }

    /// <summary>
    /// Folder tujuan bundle yang wajar untuk file ini: folder file itu sendiri untuk file tunggal; untuk file dari sebuah folder,
    /// induk folder yang ditambahkan (bundle disimpan di samping foldernya, bukan di dalam subfolder yang kebetulan berisi file pertama).
    /// </summary>
    public string DefaultOutputFolder
    {
        get
        {
            if (RelativePath is null) return Directory;
            var folder = Directory;
            var levelsUp = RelativePath.Count(c => c == '/') + 1; // dari folder file ke folder yang ditambahkan, lalu satu lagi ke induknya
            for (var i = 0; i < levelsUp; i++)
                folder = System.IO.Path.GetDirectoryName(folder) ?? folder;
            return folder;
        }
    }

    /// <summary>Nama di daftar: path relatif kalau berasal dari folder ("Laporan/sub/data.xlsx"), selain itu nama file.</summary>
    public string DisplayName => GroupName is null ? FileName : GroupName + "/" + RelativePath;

    /// <summary>Perintah hapus dari daftar. Tombolnya disembunyikan (<see cref="IsRemovable"/>) selama proses berjalan.</summary>
    public IRelayCommand RemoveCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private BatchStatus _status = BatchStatus.Waiting;

    /// <summary>Hasil (path output) kalau berhasil, alasan kalau gagal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    [NotifyPropertyChangedFor(nameof(ShowDirectory))]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    private string _message = "";

    [ObservableProperty]
    private bool _isRemovable = true;

    /// <summary>Folder asal hanya tampil selama belum ada hasil, supaya baris tidak berulang-ulang panjang.</summary>
    public bool ShowDirectory => string.IsNullOrEmpty(Message);

    public bool ShowResult => !ShowDirectory;

    /// <summary>Yang tampil di baris: nama file hasil kalau berhasil, alasan kalau gagal.</summary>
    public string ResultText => Status == BatchStatus.Succeeded && Message.Length > 0
        ? "-> " + System.IO.Path.GetFileName(Message)
        : Message;

    public string StatusText => Status switch
    {
        BatchStatus.Waiting => "Menunggu",
        BatchStatus.Running => "Diproses...",
        BatchStatus.Succeeded => "Berhasil",
        BatchStatus.Failed => "Gagal",
        BatchStatus.Canceled => "Dibatalkan",
        _ => "",
    };
}
