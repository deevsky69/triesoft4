using System.Text.Json;
using Triesoft.Core.Crypto;

namespace Triesoft.Core.KeyManagement;

/// <summary>
/// Implementasi <see cref="IKeyStore"/> berbasis file lokal: <c>index.json</c> menyimpan metadata
/// semua kunci (plaintext -- bukan rahasia inti, hanya identitas &amp; masa berlaku), dan setiap
/// kunci punya file <c>&lt;keyId&gt;.key</c> berisi byte hasil <see cref="IKeyProtector.Protect"/>.
/// </summary>
/// <remarks>
/// Akses dari satu proses dilindungi <c>lock</c>. Akses beberapa proses ke direktori yang sama
/// secara bersamaan belum didukung -- batasan yang diterima untuk MVP aplikasi desktop single-instance.
/// </remarks>
public sealed class FileKeyStore : IKeyStore
{
    private readonly string _rootDirectory;
    private readonly IKeyProtector _protector;
    private readonly string _indexPath;
    private readonly Lock _indexLock = new();

    public FileKeyStore(string rootDirectory, IKeyProtector protector)
    {
        _rootDirectory = rootDirectory;
        _protector = protector;
        _indexPath = Path.Combine(rootDirectory, "index.json");
        Directory.CreateDirectory(rootDirectory);
    }

    public void Save(MonthlyKey key, DateTimeOffset validFrom, DateTimeOffset validUntil)
    {
        lock (_indexLock)
        {
            var index = ReadIndex();
            if (index.Any(m => m.KeyId == key.KeyId))
                throw new ArgumentException($"KeyId '{key.KeyId}' sudah pernah dipakai.", nameof(key));

            var protectedMaterial = _protector.Protect(key.KeyMaterial);
            File.WriteAllBytes(KeyFilePath(key.KeyId), protectedMaterial);

            index.Add(new KeyMetadata(key.KeyId, KeyStatus.Active, DateTimeOffset.UtcNow, validFrom, validUntil));
            WriteIndex(index);
        }
    }

    public MonthlyKey LoadKeyMaterial(string keyId)
    {
        var path = KeyFilePath(keyId);
        if (!File.Exists(path))
            throw new KeyNotFoundException($"Material kunci '{keyId}' tidak ditemukan (mungkin sudah di-revoke/purge).");

        var protectedMaterial = File.ReadAllBytes(path);
        var plaintext = _protector.Unprotect(protectedMaterial);
        try
        {
            return MonthlyKey.FromBytes(keyId, plaintext);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public KeyMetadata? GetMetadata(string keyId)
    {
        lock (_indexLock)
        {
            return ReadIndex().FirstOrDefault(m => m.KeyId == keyId);
        }
    }

    public IReadOnlyList<KeyMetadata> ListMetadata()
    {
        lock (_indexLock)
        {
            return ReadIndex();
        }
    }

    public void UpdateMetadata(KeyMetadata metadata)
    {
        lock (_indexLock)
        {
            var index = ReadIndex();
            var pos = index.FindIndex(m => m.KeyId == metadata.KeyId);
            if (pos < 0)
                throw new KeyNotFoundException($"Kunci '{metadata.KeyId}' tidak ditemukan di index.");

            index[pos] = metadata;
            WriteIndex(index);
        }
    }

    public void DeleteMaterial(string keyId)
    {
        var path = KeyFilePath(keyId);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string KeyFilePath(string keyId) => Path.Combine(_rootDirectory, $"{keyId}.key");

    private List<KeyMetadata> ReadIndex()
    {
        if (!File.Exists(_indexPath))
            return [];

        var json = File.ReadAllText(_indexPath);
        return JsonSerializer.Deserialize<List<KeyMetadata>>(json) ?? [];
    }

    private void WriteIndex(List<KeyMetadata> index)
    {
        var json = JsonSerializer.Serialize(index);
        File.WriteAllText(_indexPath, json);
    }
}
