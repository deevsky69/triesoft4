namespace Triesoft.Core.Identity;

/// <summary>Satu akun user TRIESOFT 4. <see cref="PasswordHash"/>/<see cref="PasswordSalt"/> -- bukan password itu sendiri.</summary>
public sealed record UserAccount(
    string Username,
    string DisplayName,
    UserRole Role,
    UserStatus Status,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    int FailedLoginCount = 0,
    DateTimeOffset? LockedUntil = null);
