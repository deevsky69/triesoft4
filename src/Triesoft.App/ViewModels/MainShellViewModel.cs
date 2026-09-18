using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Triesoft.Core.Identity;

namespace Triesoft.App.ViewModels;

public partial class MainShellViewModel(IServiceProvider services) : ObservableObject
{
    [ObservableProperty]
    private UserAccount? _currentUser;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string _currentPageTitle = "";

    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;

    public bool IsEncryptPageActive => CurrentPage is EncryptViewModel;
    public bool IsDecryptPageActive => CurrentPage is DecryptViewModel;
    public bool IsKeyManagementPageActive => CurrentPage is KeyManagementViewModel;
    public bool IsUserManagementPageActive => CurrentPage is UserManagementViewModel;

    public event EventHandler? LoggedOut;

    partial void OnCurrentPageChanged(ViewModelBase? value)
    {
        OnPropertyChanged(nameof(IsEncryptPageActive));
        OnPropertyChanged(nameof(IsDecryptPageActive));
        OnPropertyChanged(nameof(IsKeyManagementPageActive));
        OnPropertyChanged(nameof(IsUserManagementPageActive));
    }

    public void Initialize(UserAccount user)
    {
        CurrentUser = user;
        OnPropertyChanged(nameof(IsAdmin));
        NavigateToEncrypt();
    }

    [RelayCommand]
    private void NavigateToEncrypt() =>
        SetPage("Enkripsi File", services.GetRequiredService<EncryptViewModel>());

    [RelayCommand]
    private void NavigateToDecrypt() =>
        SetPage("Dekripsi File", services.GetRequiredService<DecryptViewModel>());

    [RelayCommand]
    private void NavigateToKeyManagement()
    {
        if (!IsAdmin) return;
        SetPage("Kelola Kunci", services.GetRequiredService<KeyManagementViewModel>());
    }

    [RelayCommand]
    private void NavigateToUserManagement()
    {
        if (!IsAdmin) return;
        var vm = services.GetRequiredService<UserManagementViewModel>();
        vm.CurrentAdminUsername = CurrentUser!.Username;
        SetPage("Kelola User", vm);
    }

    [RelayCommand]
    private void Logout() => LoggedOut?.Invoke(this, EventArgs.Empty);

    private void SetPage(string title, ViewModelBase vm)
    {
        CurrentPageTitle = title;
        CurrentPage = vm;
    }
}
