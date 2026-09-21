namespace Triesoft.Core.Audit;

/// <summary>
/// Nilai numerik SENGAJA ditulis eksplisit: <see cref="StoredAuditEntry"/> menyimpan aksi sebagai angka
/// di <c>audit.log</c>, dan hash rantai dihitung dari NAMA enum. Menyisipkan atau mengurutkan ulang anggota
/// akan membuat entri lama terbaca sebagai aksi lain dan <c>VerifyChain()</c> melaporkan tamper palsu.
/// Anggota baru selalu ditambah di akhir dengan angka baru; jangan pernah mengubah atau memakai ulang angka.
/// </summary>
public enum AuditAction
{
    LoginSucceeded = 0,
    LoginFailed = 1,

    UserRegistered = 2,
    UserVerified = 3,
    UserDisabled = 4,

    KeyImported = 5,
    KeyRevoked = 6,
    KeyPurged = 7,

    FileEncrypted = 8,
    FileEncryptFailed = 9,
    FileDecrypted = 10,
    FileDecryptFailed = 11,

    IssuerIdentityCreated = 12,
    IssuerTrusted = 13,
    RecipientRegistered = 14,
    RecipientRemoved = 15,
    KeyPackageIssued = 16,
    KeyPackageImported = 17,
    KeyPackageImportFailed = 18,

    IssuerRevoked = 19,
    RecipientRevoked = 20,
    RecipientKeyRotated = 21,
    IssuerKeyRotated = 22,
}
