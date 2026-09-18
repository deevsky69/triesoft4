using Triesoft.Core.Identity;
using Xunit;

namespace Triesoft.Core.Tests.Identity;

public class FileUserStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-userstore-test-").FullName;

    private FileUserStore NewStore() => new(_dir);

    private static UserAccount NewAccount(string username) =>
        new(username, "Nama Contoh", UserRole.Operator, UserStatus.PendingVerification,
            [1, 2, 3], [4, 5, 6], DateTimeOffset.UtcNow, "admin1");

    [Fact]
    public void Save_ThenGet_ReturnsSameAccount()
    {
        var store = NewStore();
        store.Save(NewAccount("budi"));

        var loaded = store.Get("budi");
        Assert.NotNull(loaded);
        Assert.Equal(UserRole.Operator, loaded!.Role);
    }

    [Fact]
    public void Save_DuplicateUsername_Throws()
    {
        var store = NewStore();
        store.Save(NewAccount("dup"));
        Assert.Throws<ArgumentException>(() => store.Save(NewAccount("dup")));
    }

    [Fact]
    public void Index_PersistsAcrossNewInstance()
    {
        var store1 = NewStore();
        store1.Save(NewAccount("persist"));

        var store2 = NewStore();
        Assert.NotNull(store2.Get("persist"));
    }

    [Fact]
    public void Update_ChangesStatus()
    {
        var store = NewStore();
        store.Save(NewAccount("upd"));

        var account = store.Get("upd")!;
        store.Update(account with { Status = UserStatus.Active });

        Assert.Equal(UserStatus.Active, store.Get("upd")!.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
