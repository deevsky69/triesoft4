using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.Core.Tests.KeyManagement;

public class MonthlyKeyManagerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-keymanager-test-").FullName;
    private readonly MonthlyKeyManager _manager;

    public MonthlyKeyManagerTests()
    {
        var store = new FileKeyStore(_dir, new PlaintextKeyProtectorForTests());
        _manager = new MonthlyKeyManager(store);
    }

    private static byte[] RandomKey()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        return key;
    }

    [Fact]
    public void ImportMonthlyKey_BecomesActive()
    {
        _manager.ImportMonthlyKey("2026-09-A", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));

        var keys = _manager.ListKeys();
        Assert.Single(keys);
        Assert.Equal(KeyStatus.Active, keys[0].Status);
    }

    [Fact]
    public void ImportSecondKey_ExpiresThePreviousActiveOne()
    {
        _manager.ImportMonthlyKey("2026-08-A", RandomKey(), DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow);
        _manager.ImportMonthlyKey("2026-09-A", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));

        var keys = _manager.ListKeys().ToDictionary(k => k.KeyId);
        Assert.Equal(KeyStatus.Expired, keys["2026-08-A"].Status);
        Assert.Equal(KeyStatus.Active, keys["2026-09-A"].Status);
    }

    [Fact]
    public void ImportMonthlyKey_DuplicateKeyId_Throws()
    {
        _manager.ImportMonthlyKey("dup", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        Assert.Throws<ArgumentException>(() =>
            _manager.ImportMonthlyKey("dup", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void GetActiveKeyForEncryption_NoKeyImported_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _manager.GetActiveKeyForEncryption());
        Assert.Contains("Bidsandi Mabes", ex.Message);
    }

    [Fact]
    public void GetKeyForDecryption_OnExpiredKey_StillWorks()
    {
        _manager.ImportMonthlyKey("2026-08-A", RandomKey(), DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow);
        _manager.ImportMonthlyKey("2026-09-A", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));

        using var oldKey = _manager.GetKeyForDecryption("2026-08-A");
        Assert.Equal("2026-08-A", oldKey.KeyId);
    }

    [Fact]
    public void RevokeKey_MaterialDeleted_AndDecryptionRejectedAfterward()
    {
        _manager.ImportMonthlyKey("compromised", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        _manager.RevokeKey("compromised", "diduga bocor saat pengiriman");

        var status = _manager.ListKeys().Single(k => k.KeyId == "compromised");
        Assert.Equal(KeyStatus.Revoked, status.Status);
        Assert.Equal("diduga bocor saat pengiriman", status.StatusReason);

        var ex = Assert.Throws<InvalidOperationException>(() => _manager.GetKeyForDecryption("compromised"));
        Assert.Contains("dicabut", ex.Message);
    }

    [Fact]
    public void PurgeExpiredKey_OnActiveKey_Throws()
    {
        _manager.ImportMonthlyKey("still-active", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => _manager.PurgeExpiredKey("still-active"));
    }

    [Fact]
    public void PurgeExpiredKey_OnExpiredKey_Succeeds_AndDecryptionRejectedAfterward()
    {
        _manager.ImportMonthlyKey("old", RandomKey(), DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddMonths(-1));
        _manager.ImportMonthlyKey("current", RandomKey(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));

        _manager.PurgeExpiredKey("old");

        Assert.Equal(KeyStatus.Purged, _manager.ListKeys().Single(k => k.KeyId == "old").Status);
        var ex = Assert.Throws<InvalidOperationException>(() => _manager.GetKeyForDecryption("old"));
        Assert.Contains("dihapus permanen", ex.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
