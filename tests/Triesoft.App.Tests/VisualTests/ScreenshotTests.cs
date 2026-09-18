using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Triesoft.App.ViewModels;
using Triesoft.App.Views;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.VisualTests;

/// <summary>
/// Merender tiap View ke file PNG lewat platform Avalonia headless (tanpa display server),
/// supaya hasilnya bisa diperiksa visual secara langsung -- bukan cuma dipercaya dari kode.
/// Output disimpan di <see cref="OutputDirectory"/>.
/// </summary>
public class ScreenshotTests
{
    public static readonly string OutputDirectory = Path.Combine(Path.GetTempPath(), "triesoft4-screenshots");

    public ScreenshotTests()
    {
        HeadlessSetup.EnsureInitialized();
        Directory.CreateDirectory(OutputDirectory);
    }

    [Fact]
    public void Login_Screenshot()
    {
        var dir = Directory.CreateTempSubdirectory("triesoft4-shot-login-").FullName;
        var authService = new AuthService(new FileUserStore(dir));
        authService.RegisterUser("admin1", "Admin Satu", "password123", UserRole.Admin, "system");
        authService.VerifyUser("admin1");

        Render(new LoginView { DataContext = new LoginViewModel(authService) }, "login.png");
    }

    [Fact]
    public void Login_FirstRun_Screenshot()
    {
        var dir = Directory.CreateTempSubdirectory("triesoft4-shot-login-firstrun-").FullName;
        var authService = new AuthService(new FileUserStore(dir));

        Render(new LoginView { DataContext = new LoginViewModel(authService) }, "login-first-run.png");
    }

    [Fact]
    public void FirstRunSetup_Screenshot()
    {
        var dir = Directory.CreateTempSubdirectory("triesoft4-shot-setup-").FullName;
        var authService = new AuthService(new FileUserStore(dir));

        Render(new FirstRunSetupView { DataContext = new FirstRunSetupViewModel(authService) }, "first-run-setup.png");
    }

    [Fact]
    public void MainShell_Encrypt_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        Render(new MainShellView { DataContext = shell }, "main-shell-encrypt.png");
    }

    [Fact]
    public void MainShell_KeyManagement_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        shell.NavigateToKeyManagementCommand.Execute(null);
        Render(new MainShellView { DataContext = shell }, "main-shell-key-management.png");
    }

    [Fact]
    public void MainShell_UserManagement_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        shell.NavigateToUserManagementCommand.Execute(null);
        Render(new MainShellView { DataContext = shell }, "main-shell-user-management.png");
    }

    [Fact]
    public void MainShell_Operator_HidesAdminMenus_Screenshot()
    {
        var userDir = Directory.CreateTempSubdirectory("triesoft4-shot-op-users-").FullName;
        var keyDir = Directory.CreateTempSubdirectory("triesoft4-shot-op-keys-").FullName;

        var authService = new AuthService(new FileUserStore(userDir));
        var operatorUser = authService.RegisterUser("op1", "Operator Satu", "password123", UserRole.Operator, "admin1");
        authService.VerifyUser("op1");

        var keyManager = new MonthlyKeyManager(new FileKeyStore(keyDir, new PassthroughKeyProtector()));
        var provider = BuildServiceProvider(authService, keyManager);

        var shell = new MainShellViewModel(provider);
        shell.Initialize(operatorUser);

        Render(new MainShellView { DataContext = shell }, "main-shell-operator.png");
    }

    private static MainShellViewModel BuildShell(out UserAccount user)
    {
        var userDir = Directory.CreateTempSubdirectory("triesoft4-shot-users-").FullName;
        var keyDir = Directory.CreateTempSubdirectory("triesoft4-shot-keys-").FullName;

        var authService = new AuthService(new FileUserStore(userDir));
        user = authService.RegisterUser("admin1", "Admin Satu", "password123", UserRole.Admin, "system");
        authService.VerifyUser("admin1");

        var keyManager = new MonthlyKeyManager(new FileKeyStore(keyDir, new PassthroughKeyProtector()));
        keyManager.ImportMonthlyKey("2026-08-POLDA-JATIM", new byte[32], DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow);
        keyManager.ImportMonthlyKey("2026-09-POLDA-JATIM", new byte[32], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));

        // beri satu user Pending lagi supaya tampilan daftar user tidak kosong
        authService.RegisterUser("op1", "Operator Satu", "password123", UserRole.Operator, "admin1");

        var provider = BuildServiceProvider(authService, keyManager);
        return new MainShellViewModel(provider);
    }

    private static ServiceProvider BuildServiceProvider(AuthService authService, MonthlyKeyManager keyManager)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        services.AddSingleton(keyManager);
        services.AddTransient<EncryptViewModel>();
        services.AddTransient<DecryptViewModel>();
        services.AddTransient<KeyManagementViewModel>();
        services.AddTransient<UserManagementViewModel>();
        return services.BuildServiceProvider();
    }

    private static void Render(Control view, string fileName)
    {
        var window = new Window
        {
            Width = 1140,
            Height = 740,
            Content = view,
        };
        window.Show();

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); // gagal jelas kalau rendering headless tidak menghasilkan apa-apa,
                                // daripada diam-diam melewati penyimpanan file (lihat frame?.Save di bawah)
        frame.Save(Path.Combine(OutputDirectory, fileName));

        window.Close();
    }
}
