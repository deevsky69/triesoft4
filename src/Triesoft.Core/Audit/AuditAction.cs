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

    FileEncrypted,
    FileEncryptFailed,
    FileDecrypted,
    FileDecryptFailed,
}
