using Avalonia.Controls;
using Avalonia.Interactivity;
using Triesoft.App.ViewModels;
using Triesoft.App.Views.Dialogs;

namespace Triesoft.App.Views;

public partial class KeyManagementView : UserControl
{
    public KeyManagementView()
    {
        InitializeComponent();
    }

    private async void OnRevokeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyManagementViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            $"Cabut kunci '{vm.SelectedKey?.KeyId}'? Material kunci akan dihapus permanen dan tidak bisa dipakai lagi " +
            "untuk enkripsi maupun dekripsi arsip lama.",
            "Cabut Kunci");

        if (confirmed)
            vm.RevokeSelectedCommand.Execute(null);
    }

    private async void OnPurgeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyManagementViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            $"Hapus permanen kunci '{vm.SelectedKey?.KeyId}'? Tindakan ini tidak bisa dibatalkan.",
            "Hapus Permanen");

        if (confirmed)
            vm.PurgeSelectedCommand.Execute(null);
    }
}
