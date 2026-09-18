namespace Triesoft.Core.Identity;

/// <summary>
/// Business logic autentikasi &amp; siklus hidup akun di atas <see cref="IUserStore"/>: hanya Admin
/// yang boleh mendaftarkan &amp; memverifikasi user (ditegakkan di layer pemanggil lewat RBAC, bukan
/// di sini), lockout setelah percobaan gagal beruntun, dan status akun (Pending/Disabled) baru
/// diungkap SETELAH password terbukti benar -- supaya penebak password acak tidak bisa
/// mempelajari status akun tanpa tahu password-nya.
/// </summary>
public sealed class AuthService(IUserStore userStore)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public bool HasAnyUsers() => userStore.ListAll().Count > 0;

    public IReadOnlyList<UserAccount> ListUsers() => userStore.ListAll();

    public UserAccount RegisterUser(string username, string displayName, string password, UserRole role, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username tidak boleh kosong.", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password tidak boleh kosong.", nameof(password));
        if (userStore.Get(username) is not null)
            throw new ArgumentException($"Username '{username}' sudah dipakai.", nameof(username));

        var (hash, salt) = PasswordHasher.Hash(password);
        var account = new UserAccount(username, displayName, role, UserStatus.PendingVerification, hash, salt, DateTimeOffset.UtcNow, createdBy);
        userStore.Save(account);
        return account;
    }

    public void VerifyUser(string username)
    {
        var account = userStore.Get(username)
            ?? throw new KeyNotFoundException($"User '{username}' tidak ditemukan.");
        if (account.Status != UserStatus.PendingVerification)
        {
            throw new InvalidOperationException(
                $"User '{username}' berstatus {account.Status}, hanya user PendingVerification yang bisa diverifikasi.");
        }

        userStore.Update(account with { Status = UserStatus.Active });
    }

    public void DisableUser(string username)
    {
        var account = userStore.Get(username)
            ?? throw new KeyNotFoundException($"User '{username}' tidak ditemukan.");

        userStore.Update(account with { Status = UserStatus.Disabled });
    }

    public UserAccount Login(string username, string password)
    {
        const string GenericFailureMessage = "Username atau password salah.";

        var account = userStore.Get(username)
            ?? throw new InvalidOperationException(GenericFailureMessage);

        if (account.LockedUntil is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException(
                $"Akun terkunci sampai {lockedUntil:yyyy-MM-dd HH:mm} UTC karena terlalu banyak percobaan gagal.");
        }

        if (!PasswordHasher.Verify(password, account.PasswordHash, account.PasswordSalt))
        {
            var failedCount = account.FailedLoginCount + 1;
            var lockedUntilNew = failedCount >= MaxFailedAttempts
                ? DateTimeOffset.UtcNow.Add(LockoutDuration)
                : (DateTimeOffset?)null;

            userStore.Update(account with { FailedLoginCount = failedCount, LockedUntil = lockedUntilNew });

            throw new InvalidOperationException(
                lockedUntilNew is not null
                    ? $"Password salah. Akun dikunci selama {LockoutDuration.TotalMinutes:0} menit karena terlalu banyak percobaan gagal."
                    : GenericFailureMessage);
        }

        // Password sudah terbukti benar -- baru sekarang ungkap status akun kalau bukan Active.
        if (account.Status == UserStatus.PendingVerification)
            throw new InvalidOperationException("Akun belum diverifikasi oleh Admin.");
        if (account.Status == UserStatus.Disabled)
            throw new InvalidOperationException("Akun ini telah dinonaktifkan.");

        if (account.FailedLoginCount > 0 || account.LockedUntil is not null)
        {
            account = account with { FailedLoginCount = 0, LockedUntil = null };
            userStore.Update(account);
        }

        return account;
    }
}
