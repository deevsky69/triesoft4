using Triesoft.Core.Audit;
using Xunit;

namespace Triesoft.Core.Tests.Audit;

/// <summary>
/// Mengunci nilai numerik <see cref="AuditAction"/>. audit.log menyimpan angka, jadi mengubahnya
/// merusak pembacaan dan verifikasi rantai entri lama (lihat komentar di enum).
/// </summary>
public class AuditActionStabilityTests
{
    [Theory]
    [InlineData(AuditAction.LoginSucceeded, 0)]
    [InlineData(AuditAction.LoginFailed, 1)]
    [InlineData(AuditAction.UserRegistered, 2)]
    [InlineData(AuditAction.UserVerified, 3)]
    [InlineData(AuditAction.UserDisabled, 4)]
    [InlineData(AuditAction.KeyImported, 5)]
    [InlineData(AuditAction.KeyRevoked, 6)]
    [InlineData(AuditAction.KeyPurged, 7)]
    [InlineData(AuditAction.FileEncrypted, 8)]
    [InlineData(AuditAction.FileEncryptFailed, 9)]
    [InlineData(AuditAction.FileDecrypted, 10)]
    [InlineData(AuditAction.FileDecryptFailed, 11)]
    [InlineData(AuditAction.IssuerIdentityCreated, 12)]
    [InlineData(AuditAction.IssuerTrusted, 13)]
    [InlineData(AuditAction.RecipientRegistered, 14)]
    [InlineData(AuditAction.RecipientRemoved, 15)]
    [InlineData(AuditAction.KeyPackageIssued, 16)]
    [InlineData(AuditAction.KeyPackageImported, 17)]
    [InlineData(AuditAction.KeyPackageImportFailed, 18)]
    [InlineData(AuditAction.IssuerRevoked, 19)]
    [InlineData(AuditAction.RecipientRevoked, 20)]
    [InlineData(AuditAction.RecipientKeyRotated, 21)]
    [InlineData(AuditAction.IssuerKeyRotated, 22)]
    public void NumericValue_IsPinned(AuditAction action, int expected) => Assert.Equal(expected, (int)action);

    [Fact]
    public void AllMembers_ArePinnedAbove()
    {
        // kalau ada anggota baru, tambahkan juga ke Theory di atas
        Assert.Equal(23, Enum.GetValues<AuditAction>().Length);
    }
}
