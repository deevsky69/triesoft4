using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Triesoft.App.ViewModels;

namespace Triesoft.App.Views;

public partial class EncryptView : UserControl
{
    public EncryptView()
    {
        InitializeComponent();
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not EncryptViewModel vm) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih file untuk dienkripsi (boleh lebih dari satu)",
            AllowMultiple = true,
        });

        if (files.Count > 0)
            vm.AddFiles(files.Select(f => f.Path.LocalPath));
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not EncryptViewModel vm) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih folder (semua file di dalamnya, tanpa subfolder)",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
            vm.AddFolder(folders[0].Path.LocalPath);
    }
}
