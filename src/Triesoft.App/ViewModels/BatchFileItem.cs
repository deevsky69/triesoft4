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
    public BatchFileItem(string path, Action<BatchFileItem> remove)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? "";
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }

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
