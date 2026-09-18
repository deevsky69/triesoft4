using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.Core.Identity;

namespace Triesoft.App.ViewModels;

/// <summary>
/// Ditampilkan hanya saat user store masih kosong sama sekali -- membuat akun Admin pertama
/// secara langsung (tidak ada Admin lain yang bisa mendaftarkan/memverifikasi, jadi akun ini
/// langsung diaktifkan begitu dibuat).
/// </summary>
public partial class FirstRunSetupViewModel(AuthService authService) : ViewModelBase
{
    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    public event EventHandler? Completed;

    [RelayCommand]
    private void CreateFirstAdmin()
    {
        ClearMessages();
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Konfirmasi password tidak cocok.";
            return;
        }

        try
        {
            var account = authService.RegisterUser(Username.Trim(), DisplayName.Trim(), Password, UserRole.Admin, createdBy: "setup-awal");
            authService.VerifyUser(account.Username);
            Password = "";
            ConfirmPassword = "";
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
