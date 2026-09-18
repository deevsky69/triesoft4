using System.Text.Json;

namespace Triesoft.Core.Identity;

/// <summary>
/// Implementasi <see cref="IUserStore"/> berbasis satu file <c>users.json</c>. Hash+salt password
/// aman disimpan plaintext-JSON (satu-arah, sudah di-salt) -- tidak butuh <c>IKeyProtector</c>
/// seperti material kunci di <c>KeyManagement</c>.
/// </summary>
public sealed class FileUserStore : IUserStore
{
    private readonly string _indexPath;
    private readonly Lock _lock = new();

    public FileUserStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _indexPath = Path.Combine(rootDirectory, "users.json");
    }

    public void Save(UserAccount account)
    {
        lock (_lock)
        {
            var all = ReadAll();
            if (all.Any(u => u.Username == account.Username))
                throw new ArgumentException($"Username '{account.Username}' sudah dipakai.", nameof(account));

            all.Add(account);
            WriteAll(all);
        }
    }

    public UserAccount? Get(string username)
    {
        lock (_lock)
        {
            return ReadAll().FirstOrDefault(u => u.Username == username);
        }
    }

    public IReadOnlyList<UserAccount> ListAll()
    {
        lock (_lock)
        {
            return ReadAll();
        }
    }

    public void Update(UserAccount account)
    {
        lock (_lock)
        {
            var all = ReadAll();
            var pos = all.FindIndex(u => u.Username == account.Username);
            if (pos < 0)
                throw new KeyNotFoundException($"User '{account.Username}' tidak ditemukan.");

            all[pos] = account;
            WriteAll(all);
        }
    }

    private List<UserAccount> ReadAll()
    {
        if (!File.Exists(_indexPath))
            return [];

        var json = File.ReadAllText(_indexPath);
        return JsonSerializer.Deserialize<List<UserAccount>>(json) ?? [];
    }

    private void WriteAll(List<UserAccount> all)
    {
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(all));
    }
}
