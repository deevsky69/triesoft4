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

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not EncryptViewModel vm) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih file untuk dienkripsi",
            AllowMultiple = false,
        });

        if (files.Count > 0)
            vm.SetSelectedFile(files[0].Path.LocalPath);
    }
}
