using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Triesoft.App.Services;
using Triesoft.Core.Identity;

namespace Triesoft.App.ViewModels;

/// <summary>
/// Composition root navigasi tingkat atas: First-Run Setup (kalau belum ada user sama sekali) ->
/// Login -> MainShell pasca-login -> kembali ke Login saat logout.
/// </summary>
public partial class AppShellViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly IServiceProvider _services;
    private readonly SessionContext _session;

    [ObservableProperty]
    private object? _currentView;

    public AppShellViewModel(AuthService authService, IServiceProvider services, SessionContext session)
    {
        _authService = authService;
        _services = services;
        _session = session;
        CurrentView = ResolveInitialView();
    }

    private object ResolveInitialView()
    {
        if (!_authService.HasAnyUsers())
        {
            var setup = _services.GetRequiredService<FirstRunSetupViewModel>();
            setup.Completed += (_, _) => NavigateToLogin();
            return setup;
        }

        return CreateLoginViewModel();
    }

    private LoginViewModel CreateLoginViewModel()
    {
        var login = _services.GetRequiredService<LoginViewModel>();
        login.LoginSucceeded += (_, user) => NavigateToShell(user);
        login.SetupAwalRequested += (_, _) =>
        {
            var setup = _services.GetRequiredService<FirstRunSetupViewModel>();
            setup.Completed += (_, _) => NavigateToLogin();
            CurrentView = setup;
        };
        return login;
    }

    private void NavigateToLogin() => CurrentView = CreateLoginViewModel();

    private void NavigateToShell(UserAccount user)
    {
        _session.SignIn(user);
        var shell = _services.GetRequiredService<MainShellViewModel>();
        shell.Initialize(user);
        shell.LoggedOut += (_, _) =>
        {
            _session.SignOut();
            NavigateToLogin();
        };
        CurrentView = shell;
    }
}
