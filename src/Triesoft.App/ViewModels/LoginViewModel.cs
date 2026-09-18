using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.Core.Identity;

namespace Triesoft.App.ViewModels;

public partial class LoginViewModel(AuthService authService) : ViewModelBase
{
    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    public event EventHandler<UserAccount>? LoginSucceeded;
    public event EventHandler? SetupAwalRequested;

    public bool ShowSetupAwalLink => !authService.HasAnyUsers();

    [RelayCommand]
    private void Login()
    {
        ClearMessages();
        try
        {
            var user = authService.Login(Username, Password);
            Password = "";
            LoginSucceeded?.Invoke(this, user);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void GoToSetupAwal() => SetupAwalRequested?.Invoke(this, EventArgs.Empty);
}
