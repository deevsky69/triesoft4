using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = Services.GetRequiredService<AppShellViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = shell };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SessionContext>();
        services.AddSingleton<IKeyProtector>(_ => CreateProtector());
        services.AddSingleton<IKeyStore>(sp => new FileKeyStore(AppPaths.KeyStoreDirectory, sp.GetRequiredService<IKeyProtector>()));
        services.AddSingleton<MonthlyKeyManager>();
        services.AddSingleton<IUserStore>(_ => new FileUserStore(AppPaths.UserStoreDirectory));
        services.AddSingleton<AuthService>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<FirstRunSetupViewModel>();
        services.AddTransient<MainShellViewModel>();
        services.AddTransient<EncryptViewModel>();
        services.AddTransient<DecryptViewModel>();
        services.AddTransient<KeyManagementViewModel>();
        services.AddTransient<UserManagementViewModel>();
        services.AddSingleton<AppShellViewModel>();
    }

    private static IKeyProtector CreateProtector() =>
        OperatingSystem.IsWindows() ? new DpapiKeyProtector() : new DevInsecurePassthroughProtector();
}
