namespace Triesoft.Core.Identity;

public interface IUserStore
{
    /// <summary>Menyimpan akun baru. Melempar exception kalau Username sudah dipakai.</summary>
    void Save(UserAccount account);

    UserAccount? Get(string username);

    IReadOnlyList<UserAccount> ListAll();

    /// <summary>Menimpa akun yang sudah ada. Melempar exception kalau Username belum terdaftar.</summary>
    void Update(UserAccount account);
}
