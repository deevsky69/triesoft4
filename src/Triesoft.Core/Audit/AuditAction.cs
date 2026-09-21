namespace Triesoft.Core.Audit;

public enum AuditAction
{
    LoginSucceeded,
    LoginFailed,

    UserRegistered,
    UserVerified,
    UserDisabled,

    KeyImported,
    KeyRevoked,
    KeyPurged,

    IssuerIdentityCreated,
    IssuerTrusted,
    RecipientRegistered,
    RecipientRemoved,
    KeyPackageIssued,
    KeyPackageImported,
    KeyPackageImportFailed,

    FileEncrypted,
    FileEncryptFailed,
    FileDecrypted,
    FileDecryptFailed,
}
