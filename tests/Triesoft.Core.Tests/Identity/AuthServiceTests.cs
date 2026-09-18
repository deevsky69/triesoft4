using Triesoft.Core.Identity;
using Xunit;

namespace Triesoft.Core.Tests.Identity;

public class AuthServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-authservice-test-").FullName;
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        _auth = new AuthService(new FileUserStore(_dir));
    }

    [Fact]
    public void HasAnyUsers_FalseBeforeRegister_TrueAfter()
    {
        Assert.False(_auth.HasAnyUsers());
        _auth.RegisterUser("admin1", "Admin Satu", "password123", UserRole.Admin, "system");
        Assert.True(_auth.HasAnyUsers());
    }

    [Fact]
    public void RegisterUser_StartsAsPendingVerification_CannotLoginYet()
    {
        _auth.RegisterUser("budi", "Budi Operator", "password123", UserRole.Operator, "admin1");

        var ex = Assert.Throws<InvalidOperationException>(() => _auth.Login("budi", "password123"));
        Assert.Contains("belum diverifikasi", ex.Message);
    }

    [Fact]
    public void VerifyUser_ThenLogin_Succeeds()
    {
        _auth.RegisterUser("budi", "Budi Operator", "password123", UserRole.Operator, "admin1");
        _auth.VerifyUser("budi");

        var account = _auth.Login("budi", "password123");
        Assert.Equal("budi", account.Username);
        Assert.Equal(UserRole.Operator, account.Role);
    }

    [Fact]
    public void RegisterUser_DuplicateUsername_Throws()
    {
        _auth.RegisterUser("dup", "Nama", "password123", UserRole.Operator, "admin1");
        Assert.Throws<ArgumentException>(() =>
            _auth.RegisterUser("dup", "Nama Lain", "password456", UserRole.Operator, "admin1"));
    }

    [Fact]
    public void Login_WrongPassword_FiveTimes_LocksAccount()
    {
        _auth.RegisterUser("terkunci", "Nama", "password-benar", UserRole.Operator, "admin1");
        _auth.VerifyUser("terkunci");

        for (var i = 0; i < 4; i++)
            Assert.Throws<InvalidOperationException>(() => _auth.Login("terkunci", "salah"));

        var lockEx = Assert.Throws<InvalidOperationException>(() => _auth.Login("terkunci", "salah"));
        Assert.Contains("dikunci", lockEx.Message);

        // Percobaan berikutnya dengan password BENAR pun tetap ditolak selama masih terkunci.
        var stillLockedEx = Assert.Throws<InvalidOperationException>(() => _auth.Login("terkunci", "password-benar"));
        Assert.Contains("terkunci", stillLockedEx.Message);
    }

    [Fact]
    public void Login_SuccessfulAttempt_ResetsFailedCount()
    {
        _auth.RegisterUser("reset", "Nama", "password-benar", UserRole.Operator, "admin1");
        _auth.VerifyUser("reset");

        Assert.Throws<InvalidOperationException>(() => _auth.Login("reset", "salah"));
        Assert.Throws<InvalidOperationException>(() => _auth.Login("reset", "salah"));

        var account = _auth.Login("reset", "password-benar");
        Assert.Equal(0, account.FailedLoginCount);
    }

    [Fact]
    public void Login_DisabledAccount_Throws()
    {
        _auth.RegisterUser("nonaktif", "Nama", "password123", UserRole.Operator, "admin1");
        _auth.VerifyUser("nonaktif");
        _auth.DisableUser("nonaktif");

        var ex = Assert.Throws<InvalidOperationException>(() => _auth.Login("nonaktif", "password123"));
        Assert.Contains("dinonaktifkan", ex.Message);
    }

    [Fact]
    public void Login_UnknownUsername_GenericMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _auth.Login("tidak-ada", "apapun"));
        Assert.Equal("Username atau password salah.", ex.Message);
    }

    [Fact]
    public void VerifyUser_AlreadyActive_Throws()
    {
        _auth.RegisterUser("sudah-aktif", "Nama", "password123", UserRole.Operator, "admin1");
        _auth.VerifyUser("sudah-aktif");

        Assert.Throws<InvalidOperationException>(() => _auth.VerifyUser("sudah-aktif"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
