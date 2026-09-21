using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Triesoft.App.ViewModels;
using Triesoft.App.Views.Dialogs;
using Triesoft.Core.KeyDistribution;

namespace Triesoft.App.Views;

public partial class KeyDistributionView : UserControl
{
    private static readonly FilePickerFileType PublicKeyType = new("Kunci publik TRIESOFT") { Patterns = ["*.ts4pub"] };
    private static readonly FilePickerFileType PackageType = new("Paket kunci TRIESOFT") { Patterns = ["*.ts4kp"] };

    public KeyDistributionView()
    {
        InitializeComponent();
    }

    private async void OnExportRecipientClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm) return;
        var path = await PickSavePathAsync("Simpan kunci publik penerima", $"{SafeName(vm.OwnName)}-penerima.ts4pub");
        if (path is not null) vm.ExportRecipientKey(path);
    }

    private async void OnExportIssuerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm) return;
        var path = await PickSavePathAsync("Simpan kunci publik penerbit", $"{SafeName(vm.OwnName)}-penerbit.ts4pub");
        if (path is not null) vm.ExportIssuerKey(path);
    }

    private async void OnAddRecipientClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm) return;
        var path = await PickOpenPathAsync("Pilih kunci publik Polda", PublicKeyType);
        if (path is null) return;

        var file = vm.PreviewPublicKeyFile(path, PublicKeyKind.Recipient);
        if (file is null || TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(owner,
            $"Daftarkan '{file.Name}' sebagai penerima kunci?\n\nSidik jari:\n{file.Fingerprint}\n\n" +
            "Cocokkan dengan sidik jari yang dibacakan pihak Polda lewat jalur terpisah. Jangan lanjut kalau berbeda.",
            "Sidik Jari Cocok, Daftarkan");
        if (confirmed) vm.CommitPendingRecipient();
    }

    private async void OnRevokeRecipientClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm || !vm.RequireReasonForChange()) return;
        if (sender is not Control { DataContext: RecipientItem item }) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(owner,
            $"Cabut penerima '{item.Name}'?\n\nSidik jari:\n{item.Fingerprint}\n\n" +
            "Kunci publik ini tidak bisa didaftarkan lagi. Paket yang sudah terbit untuknya tidak bisa ditarik. " +
            "Kalau Polda ini tetap dipakai, ia harus merotasi kunci dan mendaftar ulang dengan sidik jari baru.",
            "Cabut Penerima");
        if (confirmed) vm.RevokeRecipient(item);
    }

    private async void OnRevokeIssuerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm || !vm.RequireReasonForChange()) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(owner,
            "Cabut penerbit tepercaya?\n\nSemua paket kunci dari penerbit ini langsung ditolak, dan kunci publiknya " +
            "tidak bisa dipercaya lagi. Kunci bulanan yang sudah diimpor tetap ada.",
            "Cabut Penerbit");
        if (confirmed) vm.RevokeTrustedIssuerCommand.Execute(null);
    }

    private async void OnRotateRecipientClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm || !vm.RequireReasonForChange()) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(owner,
            "Rotasi kunci penerima mesin ini?\n\nKunci privat lama dihancurkan permanen. Paket yang sudah dibuat Mabes untuk kunci lama " +
            "dan belum diimpor tidak bisa dibuka lagi. Anda harus mengekspor kunci publik baru dan mendaftarkannya ulang ke Mabes.",
            "Rotasi Kunci Penerima");
        if (confirmed) vm.RotateRecipientKeyCommand.Execute(null);
    }

    private async void OnRotateIssuerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm || !vm.RequireReasonForChange()) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(owner,
            "Rotasi kunci penerbit mesin ini?\n\nKunci privat lama dihancurkan permanen. Paket baru ditolak setiap Polda " +
            "sampai mereka mempercayai kunci publik penerbit yang baru (dengan verifikasi sidik jari). Kunci bulanan yang sudah diimpor tidak terpengaruh.",
            "Rotasi Kunci Penerbit");
        if (confirmed) vm.RotateIssuerKeyCommand.Execute(null);
    }

    private async void OnTrustIssuerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm) return;
        var path = await PickOpenPathAsync("Pilih kunci publik Mabes", PublicKeyType);
        if (path is null) return;

        var file = vm.PreviewPublicKeyFile(path, PublicKeyKind.Issuer);
        if (file is null || TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(owner,
            $"Percayai '{file.Name}' sebagai penerbit kunci?\n\nSidik jari:\n{file.Fingerprint}\n\n" +
            "Cocokkan dengan sidik jari yang dibacakan Bidsandi Mabes lewat jalur terpisah. Semua paket kunci dari penerbit ini akan diterima.",
            "Sidik Jari Cocok, Percayai");
        if (confirmed) vm.CommitPendingIssuer();
    }

    private async void OnImportPackageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm) return;
        var path = await PickOpenPathAsync("Pilih paket kunci", PackageType);
        if (path is not null) vm.ImportPackageFile(path);
    }

    private async void OnIssueClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyDistributionViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih folder untuk menyimpan paket kunci",
            AllowMultiple = false,
        });
        if (folders.Count > 0) vm.IssuePackages(folders[0].Path.LocalPath);
    }

    private async Task<string?> PickOpenPathAsync(string title, FilePickerFileType type)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type],
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSavePathAsync(string title, string suggestedName)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return null;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "ts4pub",
            FileTypeChoices = [PublicKeyType],
        });
        return file?.Path.LocalPath;
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
