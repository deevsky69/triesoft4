using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Triesoft.App.Views.Dialogs;

/// <summary>
/// Dialog konfirmasi generik untuk aksi yang tidak reversibel (revoke/purge kunci, nonaktifkan
/// user) -- ditampilkan dari code-behind View sebelum command ViewModel yang sebenarnya dipanggil.
/// </summary>
public partial class ConfirmDialog : Window
{
    private bool _result;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, string message, string confirmText = "Konfirmasi")
    {
        var dialog = new ConfirmDialog();
        dialog.FindControl<TextBlock>("MessageText")!.Text = message;
        dialog.FindControl<Button>("ConfirmButtonElement")!.Content = confirmText;

        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }
}
