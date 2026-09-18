using Triesoft.Core.Identity;
using Xunit;

namespace Triesoft.Core.Tests.Identity;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_CorrectPassword_Succeeds()
    {
        var (hash, salt) = PasswordHasher.Hash("kata-sandi-rahasia");
        Assert.True(PasswordHasher.Verify("kata-sandi-rahasia", hash, salt));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var (hash, salt) = PasswordHasher.Hash("kata-sandi-rahasia");
        Assert.False(PasswordHasher.Verify("salah", hash, salt));
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentSaltAndHash()
    {
        var (hash1, salt1) = PasswordHasher.Hash("sama");
        var (hash2, salt2) = PasswordHasher.Hash("sama");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }
}
