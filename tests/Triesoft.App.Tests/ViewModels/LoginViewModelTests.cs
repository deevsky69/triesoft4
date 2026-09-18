using Triesoft.App.ViewModels;
using Triesoft.Core.Identity;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

public class LoginViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-login-vm-test-").FullName;
    private readonly AuthService _authService;

    public LoginViewModelTests()
    {
        _authService = new AuthService(new FileUserStore(_dir));
    }

    [Fact]
    public void Login_CorrectCredentials_RaisesLoginSucceeded()
    {
        _authService.RegisterUser("budi", "Budi", "password123", UserRole.Operator, "admin1");
        _authService.VerifyUser("budi");

        var vm = new LoginViewModel(_authService) { Username = "budi", Password = "password123" };

        UserAccount? loggedInUser = null;
        vm.LoginSucceeded += (_, user) => loggedInUser = user;

        vm.LoginCommand.Execute(null);

        Assert.NotNull(loggedInUser);
        Assert.Equal("budi", loggedInUser!.Username);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("", vm.Password); // password dibersihkan dari VM setelah login sukses
    }

    [Fact]
    public void Login_WrongPassword_SetsErrorMessage_DoesNotRaiseSucceeded()
    {
        _authService.RegisterUser("budi", "Budi", "password-benar", UserRole.Operator, "admin1");
        _authService.VerifyUser("budi");

        var vm = new LoginViewModel(_authService) { Username = "budi", Password = "salah" };

        var raised = false;
        vm.LoginSucceeded += (_, _) => raised = true;

        vm.LoginCommand.Execute(null);

        Assert.False(raised);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void ShowSetupAwalLink_TrueWhenNoUsersExist()
    {
        var vm = new LoginViewModel(_authService);
        Assert.True(vm.ShowSetupAwalLink);
    }

    [Fact]
    public void ShowSetupAwalLink_FalseWhenUsersExist()
    {
        _authService.RegisterUser("admin1", "Admin", "password123", UserRole.Admin, "system");
        var vm = new LoginViewModel(_authService);
        Assert.False(vm.ShowSetupAwalLink);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
