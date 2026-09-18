using Avalonia.Controls;
using Avalonia.Interactivity;
using Triesoft.App.ViewModels;
using Triesoft.App.Views.Dialogs;

namespace Triesoft.App.Views;

public partial class UserManagementView : UserControl
{
    public UserManagementView()
    {
        InitializeComponent();
    }

    private async void OnDisableClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UserManagementViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            $"Nonaktifkan user '{vm.SelectedUser?.Username}'? User ini tidak akan bisa login sampai diaktifkan kembali.",
            "Nonaktifkan");

        if (confirmed)
            vm.DisableSelectedCommand.Execute(null);
    }
}
