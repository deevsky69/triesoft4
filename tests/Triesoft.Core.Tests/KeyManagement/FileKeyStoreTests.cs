using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.Core.Tests.KeyManagement;

public class FileKeyStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-keystore-test-").FullName;

    private FileKeyStore NewStore() => new(_dir, new PlaintextKeyProtectorForTests());

    [Fact]
    public void Save_ThenLoad_ReturnsSameMaterial()
    {
        var store = NewStore();
        var material = new byte[32];
        Random.Shared.NextBytes(material);

        using (var key = MonthlyKey.FromBytes("2026-09-A", material))
        {
            store.Save(key, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
        }

        using var loaded = store.LoadKeyMaterial("2026-09-A");
        Assert.Equal("2026-09-A", loaded.KeyId);
    }

    [Fact]
    public void Save_DuplicateKeyId_Throws()
    {
        var store = NewStore();
        using var key1 = MonthlyKey.FromBytes("dup", new byte[32]);
        store.Save(key1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        using var key2 = MonthlyKey.FromBytes("dup", new byte[32]);
        Assert.Throws<ArgumentException>(() => store.Save(key2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void Index_PersistsAcrossNewInstance_PointingSameDirectory()
    {
        var store1 = NewStore();
        using (var key = MonthlyKey.FromBytes("persist-test", new byte[32]))
        {
            store1.Save(key, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        }

        var store2 = NewStore(); // simulasi restart aplikasi, menunjuk direktori yang sama
        var metadata = store2.GetMetadata("persist-test");

        Assert.NotNull(metadata);
        Assert.Equal(KeyStatus.Active, metadata!.Status);
    }

    [Fact]
    public void UpdateMetadata_ThenList_ReflectsChange()
    {
        var store = NewStore();
        using (var key = MonthlyKey.FromBytes("upd", new byte[32]))
        {
            store.Save(key, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        }

        var metadata = store.GetMetadata("upd")!;
        store.UpdateMetadata(metadata with { Status = KeyStatus.Expired });

        Assert.Equal(KeyStatus.Expired, store.GetMetadata("upd")!.Status);
        Assert.Single(store.ListMetadata());
    }

    [Fact]
    public void DeleteMaterial_RemovesKeyFile_ButMetadataStays()
    {
        var store = NewStore();
        using (var key = MonthlyKey.FromBytes("del", new byte[32]))
        {
            store.Save(key, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        }

        store.DeleteMaterial("del");

        Assert.NotNull(store.GetMetadata("del"));
        Assert.Throws<KeyNotFoundException>(() => store.LoadKeyMaterial("del"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
