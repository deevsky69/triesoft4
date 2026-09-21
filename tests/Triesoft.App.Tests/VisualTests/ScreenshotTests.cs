using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.App.Views;
using Triesoft.Core.Audit;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyDistribution;
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
        var authService = new AuthService(new FileUserStore(Path.Combine(dir, "users")));
        authService.RegisterUser("admin1", "Admin Satu", "password123", UserRole.Admin, "system");
        authService.VerifyUser("admin1");
        var auditLog = new FileAuditLog(Path.Combine(dir, "audit"));

        Render(new LoginView { DataContext = new LoginViewModel(authService, auditLog) }, "login.png");
    }

    [Fact]
    public void Login_FirstRun_Screenshot()
    {
        var dir = Directory.CreateTempSubdirectory("triesoft4-shot-login-firstrun-").FullName;
        var authService = new AuthService(new FileUserStore(Path.Combine(dir, "users")));
        var auditLog = new FileAuditLog(Path.Combine(dir, "audit"));

        Render(new LoginView { DataContext = new LoginViewModel(authService, auditLog) }, "login-first-run.png");
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
    public void MainShell_EncryptBatch_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        var work = Directory.CreateTempSubdirectory("triesoft4-shot-batch-").FullName;
        var files = new[] { "laporan-intel.pdf", "briefing.pptx", "hilang.docx", "foto-lokasi.jpg" }
            .Select(n => Path.Combine(work, n)).ToList();
        foreach (var f in files.Where(f => !f.EndsWith("hilang.docx")))
            File.WriteAllText(f, "isi " + f);

        var vm = (EncryptViewModel)shell.CurrentPage!;
        vm.AddFiles(files);
        // Dijalankan dari thread pool: memblokir thread UI headless dengan GetResult() membuat deadlock,
        // karena kelanjutan await menunggu thread yang sama.
        Task.Run(() => vm.EncryptCommand.ExecuteAsync(null)).GetAwaiter().GetResult();
        vm.AddFiles([Path.Combine(work, "baru.xlsx")]); // satu lagi yang masih Menunggu

        Render(new MainShellView { DataContext = shell }, "main-shell-encrypt-batch.png", height: 900);
    }

    [Fact]
    public void MainShell_EncryptBundle_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        var work = Directory.CreateTempSubdirectory("triesoft4-shot-bundle-").FullName;
        var files = new[] { "laporan-intel.pdf", "briefing.pptx", "foto-lokasi.jpg" }.Select(n => Path.Combine(work, n)).ToList();
        foreach (var f in files) File.WriteAllText(f, "isi " + f);

        var vm = (EncryptViewModel)shell.CurrentPage!;
        vm.BundleMode = true;
        vm.BundleName = "operasi-melati";
        vm.AddFiles(files);
        // Dijalankan dari thread pool: lihat catatan di MainShell_EncryptBatch_Screenshot.
        Task.Run(() => vm.EncryptCommand.ExecuteAsync(null)).GetAwaiter().GetResult();

        Render(new MainShellView { DataContext = shell }, "main-shell-encrypt-bundle.png", height: 1000);
    }

    [Fact]
    public void DecryptView_DropActive_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        shell.NavigateToDecryptCommand.Execute(null);
        var view = new DecryptView { DataContext = shell.CurrentPage };

        // meniru keadaan saat file sedang diseret di atas kartu (class ditambah oleh FileDropTarget)
        view.FindControl<Border>("DropZone")!.Classes.Add(FileDropTarget.ActiveClass);

        Render(view, "decrypt-drop-active.png", height: 420);
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
    public void MainShell_KeyDistribution_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        shell.NavigateToKeyDistributionCommand.Execute(null);
        Render(new MainShellView { DataContext = shell }, "main-shell-key-distribution.png");
    }

    [Fact]
    public void MainShell_KeyDistribution_Issuer_Screenshot()
    {
        var shell = BuildShell(out var user);
        shell.Initialize(user);
        shell.NavigateToKeyDistributionCommand.Execute(null);
        var vm = (KeyDistributionViewModel)shell.CurrentPage!;
        vm.ActivateIssuerCommand.Execute(null);
        Render(new MainShellView { DataContext = shell }, "main-shell-key-distribution-issuer.png", height: 1500);
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
    public void MainShell_AuditLog_Screenshot()
    {
        var shell = BuildShell(out var user, out var auditLog);
        shell.Initialize(user);

        // isi log dengan beberapa entri nyata supaya tampilan tidak kosong
        auditLog.Record("admin1", AuditAction.LoginSucceeded, "");
        auditLog.Record("admin1", AuditAction.KeyImported, "keyId=2026-09-POLDA-JATIM, validFrom=2026-09-01, validUntil=2026-09-30");
        auditLog.Record("op1", AuditAction.FileEncrypted, "file=laporan-intel.pdf, keyId=2026-09-POLDA-JATIM");
        auditLog.Record("op1", AuditAction.FileDecryptFailed, "file=arsip-lama.ts4, error=tag autentikasi tidak valid");

        shell.NavigateToAuditLogCommand.Execute(null);
        if (shell.CurrentPage is AuditLogViewModel auditVm)
            auditVm.VerifyIntegrityCommand.Execute(null);

        Render(new MainShellView { DataContext = shell }, "main-shell-audit-log.png");
    }

    [Fact]
    public void MainShell_Operator_HidesAdminMenus_Screenshot()
    {
        var userDir = Directory.CreateTempSubdirectory("triesoft4-shot-op-users-").FullName;
        var keyDir = Directory.CreateTempSubdirectory("triesoft4-shot-op-keys-").FullName;
        var auditDir = Directory.CreateTempSubdirectory("triesoft4-shot-op-audit-").FullName;

        var authService = new AuthService(new FileUserStore(userDir));
        var operatorUser = authService.RegisterUser("op1", "Operator Satu", "password123", UserRole.Operator, "admin1");
        authService.VerifyUser("op1");

        var keyManager = new MonthlyKeyManager(new FileKeyStore(keyDir, new PassthroughKeyProtector()));
        var auditLog = new FileAuditLog(auditDir);
        var provider = BuildServiceProvider(authService, keyManager, auditLog);

        var shell = new MainShellViewModel(provider);
        shell.Initialize(operatorUser);

        Render(new MainShellView { DataContext = shell }, "main-shell-operator.png");
    }

    private static MainShellViewModel BuildShell(out UserAccount user) => BuildShell(out user, out _);

    private static MainShellViewModel BuildShell(out UserAccount user, out FileAuditLog auditLog)
    {
        var userDir = Directory.CreateTempSubdirectory("triesoft4-shot-users-").FullName;
        var keyDir = Directory.CreateTempSubdirectory("triesoft4-shot-keys-").FullName;
        var auditDir = Directory.CreateTempSubdirectory("triesoft4-shot-audit-").FullName;

        var authService = new AuthService(new FileUserStore(userDir));
        user = authService.RegisterUser("admin1", "Admin Satu", "password123", UserRole.Admin, "system");
        authService.VerifyUser("admin1");

        var keyManager = new MonthlyKeyManager(new FileKeyStore(keyDir, new PassthroughKeyProtector()));
        keyManager.ImportMonthlyKey("2026-08-POLDA-JATIM", new byte[32], DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow);
        keyManager.ImportMonthlyKey("2026-09-POLDA-JATIM", new byte[32], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));

        // beri satu user Pending lagi supaya tampilan daftar user tidak kosong
        authService.RegisterUser("op1", "Operator Satu", "password123", UserRole.Operator, "admin1");

        auditLog = new FileAuditLog(auditDir);
        var provider = BuildServiceProvider(authService, keyManager, auditLog);
        return new MainShellViewModel(provider);
    }

    private static ServiceProvider BuildServiceProvider(AuthService authService, MonthlyKeyManager keyManager, FileAuditLog auditLog)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        services.AddSingleton(keyManager);
        services.AddSingleton(new FileDistributionStore(
            Directory.CreateTempSubdirectory("triesoft4-shot-dist-").FullName, new PassthroughKeyProtector()));
        services.AddSingleton<KeyDistributionManager>();
        services.AddTransient<KeyDistributionViewModel>();
        services.AddSingleton<IAuditLog>(auditLog);
        services.AddSingleton<SessionContext>();
        services.AddTransient<EncryptViewModel>();
        services.AddTransient<DecryptViewModel>();
        services.AddTransient<KeyManagementViewModel>();
        services.AddTransient<UserManagementViewModel>();
        services.AddTransient<AuditLogViewModel>();
        return services.BuildServiceProvider();
    }

    private static void Render(Control view, string fileName, int height = 740)
    {
        var window = new Window
        {
            Width = 1140,
            Height = height,
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
