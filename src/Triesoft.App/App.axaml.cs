using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Triesoft.App.Converters;
using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyDistribution;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        // Seluruh Resources (warna + converter) dibangun DI SINI lewat kode, bukan lewat elemen
        // Application.Resources di App.axaml -- lihat catatan panjang di App.axaml kenapa: elemen itu
        // akan MENIMPA objek Resources ini kalau ada, dan style di Controls.axaml/Typography.axaml
        // (diparse tepat sesudah ini, di Initialize()) me-resolve StaticResource Brush.* begitu pertama
        // kali dipakai lalu "membekukannya" pada style tsb -- jadi warna yang benar harus sudah ada
        // SEBELUM Initialize() berjalan, dan tidak boleh ada apa pun sesudahnya yang menimpa objek
        // Resources ini. Hanya SATU dictionary warna (Terang atau Gelap) yang pernah dimuat sepanjang
        // proses ini; lihat AppPreferences kenapa ganti tema karena itu perlu restart aplikasi.
        var isDark = AppPreferences.Load().Theme == ThemePreference.Dark;
        var colors = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri($"avares://Triesoft.App/Theme/{(isDark ? "Colors.Dark.axaml" : "Colors.axaml")}"));
        Resources.MergedDictionaries.Add(colors);

        // Terpisah dari warna kustom di atas: ini membuat chrome bawaan FluentTheme sendiri (mis. warna
        // default TextBox/ComboBox yang tidak disentuh Controls.axaml) ikut gelap, bukan cuma elemen yang
        // dipakaikan Brush.* kustom. Aman diset di sini juga -- tidak bergantung urutan yang sama dengan
        // masalah Resources di atas.
        RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        Resources.Add("KeyExpiryWarningConverter", new KeyExpiryWarningConverter());
        Resources.Add("KeyStatusToBrushConverter", new KeyStatusToBrushConverter());
        Resources.Add("UserStatusToBrushConverter", new UserStatusToBrushConverter());
        Resources.Add("BatchStatusToBrushConverter", new BatchStatusToBrushConverter());
    }

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
        services.AddSingleton(sp => new FileDistributionStore(AppPaths.DistributionDirectory, sp.GetRequiredService<IKeyProtector>()));
        services.AddSingleton<KeyDistributionManager>();
        services.AddSingleton<IUserStore>(_ => new FileUserStore(AppPaths.UserStoreDirectory));
        services.AddSingleton<AuthService>();
        services.AddSingleton<IAuditLog>(_ => new FileAuditLog(AppPaths.AuditLogDirectory));

        services.AddTransient<LoginViewModel>();
        services.AddTransient<FirstRunSetupViewModel>();
        services.AddTransient<MainShellViewModel>();
        services.AddTransient<EncryptViewModel>();
        services.AddTransient<DecryptViewModel>();
        services.AddTransient<KeyManagementViewModel>();
        services.AddTransient<KeyDistributionViewModel>();
        services.AddTransient<UserManagementViewModel>();
        services.AddTransient<AuditLogViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddSingleton<AppShellViewModel>();
    }

    private static IKeyProtector CreateProtector() =>
        OperatingSystem.IsWindows() ? new DpapiKeyProtector() : new DevInsecurePassthroughProtector();
}
