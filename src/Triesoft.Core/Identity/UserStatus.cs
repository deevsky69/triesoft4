namespace Triesoft.Core.Identity;

public enum UserStatus
{
    /// <summary>Sudah didaftarkan Admin, menunggu verifikasi Admin sebelum bisa login.</summary>
    PendingVerification,

    /// <summary>Terverifikasi, bisa login.</summary>
    Active,

    /// <summary>Dinonaktifkan -- tidak bisa login sampai diaktifkan kembali.</summary>
    Disabled,
}
