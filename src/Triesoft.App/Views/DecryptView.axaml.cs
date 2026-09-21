using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Triesoft.App.ViewModels;

namespace Triesoft.App.Views;

public partial class DecryptView : UserControl
{
    public DecryptView()
    {
        InitializeComponent();
        FileDropTarget.Attach(this, this.FindControl<Border>("DropZone")!);
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not DecryptViewModel vm) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih file .ts4 untuk didekripsi (boleh lebih dari satu)",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("TRIESOFT 4 (*.ts4)") { Patterns = ["*.ts4"] }],
        });

        if (files.Count > 0)
            vm.AddFiles(files.Select(f => f.Path.LocalPath));
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not DecryptViewModel vm) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih folder (semua file .ts4 di dalamnya, tanpa subfolder)",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
            vm.AddFolder(folders[0].Path.LocalPath);
    }
}
