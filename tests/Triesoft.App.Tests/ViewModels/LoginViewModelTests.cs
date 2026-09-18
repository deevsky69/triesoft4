using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Identity;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

public class LoginViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-login-vm-test-").FullName;
    private readonly AuthService _authService;
    private readonly FileAuditLog _auditLog;

    public LoginViewModelTests()
    {
        _authService = new AuthService(new FileUserStore(Path.Combine(_dir, "users")));
        _auditLog = new FileAuditLog(Path.Combine(_dir, "audit"));
    }

    private LoginViewModel NewViewModel() => new(_authService, _auditLog);

    [Fact]
    public void Login_CorrectCredentials_RaisesLoginSucceeded_AndRecordsAudit()
    {
        _authService.RegisterUser("budi", "Budi", "password123", UserRole.Operator, "admin1");
        _authService.VerifyUser("budi");

        var vm = NewViewModel();
        vm.Username = "budi";
        vm.Password = "password123";

        UserAccount? loggedInUser = null;
        vm.LoginSucceeded += (_, user) => loggedInUser = user;

        vm.LoginCommand.Execute(null);

        Assert.NotNull(loggedInUser);
        Assert.Equal("budi", loggedInUser!.Username);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("", vm.Password); // password dibersihkan dari VM setelah login sukses

        var entry = Assert.Single(_auditLog.ReadAll());
        Assert.Equal(AuditAction.LoginSucceeded, entry.Action);
        Assert.Equal("budi", entry.Actor);
    }

    [Fact]
    public void Login_WrongPassword_SetsErrorMessage_DoesNotRaiseSucceeded_AndRecordsAudit()
    {
        _authService.RegisterUser("budi", "Budi", "password-benar", UserRole.Operator, "admin1");
        _authService.VerifyUser("budi");

        var vm = NewViewModel();
        vm.Username = "budi";
        vm.Password = "salah";

        var raised = false;
        vm.LoginSucceeded += (_, _) => raised = true;

        vm.LoginCommand.Execute(null);

        Assert.False(raised);
        Assert.NotNull(vm.ErrorMessage);

        var entry = Assert.Single(_auditLog.ReadAll());
        Assert.Equal(AuditAction.LoginFailed, entry.Action);
        Assert.Equal("budi", entry.Actor);
    }

    [Fact]
    public void ShowSetupAwalLink_TrueWhenNoUsersExist()
    {
        var vm = NewViewModel();
        Assert.True(vm.ShowSetupAwalLink);
    }

    [Fact]
    public void ShowSetupAwalLink_FalseWhenUsersExist()
    {
        _authService.RegisterUser("admin1", "Admin", "password123", UserRole.Admin, "system");
        var vm = NewViewModel();
        Assert.False(vm.ShowSetupAwalLink);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
