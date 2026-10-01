using Triesoft.App.Converters;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.Converters;

public class KeyExpiryWarningConverterTests
{
    private readonly KeyExpiryWarningConverter _converter = new();

    private static KeyMetadata Key(KeyStatus status, DateTimeOffset validUntil) =>
        new("K", status, DateTimeOffset.UtcNow.AddDays(-20), DateTimeOffset.UtcNow.AddDays(-20), validUntil);

    [Theory]
    [InlineData(8)] // di luar jendela peringatan 7 hari
    [InlineData(30)]
    public void ActiveKey_NotCloseToExpiry_ReturnsNull(int daysLeft) =>
        Assert.Null(_converter.Convert(Key(KeyStatus.Active, DateTimeOffset.UtcNow.AddDays(daysLeft)), typeof(string), null, null!));

    [Theory]
    [InlineData(7)]
    [InlineData(3)]
    [InlineData(1)] // bukan 0: batas persis "0 hari lagi" rapuh terhadap selisih waktu nyata antar dua panggilan UtcNow
    public void ActiveKey_WithinSevenDays_ReturnsWarningText(int daysLeft)
    {
        var text = _converter.Convert(Key(KeyStatus.Active, DateTimeOffset.UtcNow.AddDays(daysLeft)), typeof(string), null, null!);
        Assert.NotNull(text);
        Assert.Contains("hari lagi", (string)text!);
    }

    [Fact]
    public void ActiveKey_AlreadyPastExpiry_ReturnsDistinctMessage()
    {
        var text = _converter.Convert(Key(KeyStatus.Active, DateTimeOffset.UtcNow.AddDays(-1)), typeof(string), null, null!);
        Assert.Equal("Sudah lewat tanggal berakhir", text);
    }

    [Theory]
    [InlineData(KeyStatus.Expired)]
    [InlineData(KeyStatus.Revoked)]
    [InlineData(KeyStatus.Purged)]
    public void NonActiveKey_NeverWarns_EvenIfCloseToExpiry(KeyStatus status) =>
        Assert.Null(_converter.Convert(Key(status, DateTimeOffset.UtcNow.AddDays(1)), typeof(string), null, null!));

    [Fact]
    public void NonKeyMetadataValue_ReturnsNull() =>
        Assert.Null(_converter.Convert("bukan KeyMetadata", typeof(string), null, null!));

    [Fact]
    public void BoolTargetType_ReturnsBooleanInsteadOfText_ForIsVisibleBinding()
    {
        var expiring = Key(KeyStatus.Active, DateTimeOffset.UtcNow.AddDays(2));
        var farAway = Key(KeyStatus.Active, DateTimeOffset.UtcNow.AddDays(60));

        Assert.Equal(true, _converter.Convert(expiring, typeof(bool), null, null!));
        Assert.Equal(false, _converter.Convert(farAway, typeof(bool), null, null!));
    }

    [Fact]
    public void ConvertBack_NotSupported() =>
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(null, typeof(object), null, null!));
}
