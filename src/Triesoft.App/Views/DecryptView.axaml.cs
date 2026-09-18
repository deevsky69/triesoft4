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
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not DecryptViewModel vm) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih file .ts4 untuk didekripsi",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("TRIESOFT 4 (*.ts4)") { Patterns = ["*.ts4"] }],
        });

        if (files.Count > 0)
            vm.SetSelectedFile(files[0].Path.LocalPath);
    }
}
