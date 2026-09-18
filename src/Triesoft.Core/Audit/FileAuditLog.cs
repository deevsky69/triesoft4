using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Triesoft.Core.Audit;

/// <summary>
/// Implementasi <see cref="IAuditLog"/> berbasis file JSON Lines append-only (<c>audit.log</c>,
/// satu <see cref="StoredAuditEntry"/> per baris). Kode ini tidak pernah menimpa/menghapus baris
/// lama -- hanya menambah -- dan tiap baris baru mengikat hash SHA-256 dari isinya sendiri
/// digabung hash baris sebelumnya, membentuk rantai yang bisa diverifikasi lewat
/// <see cref="VerifyChain"/> untuk mendeteksi perubahan pada baris mana pun di masa lalu.
/// </summary>
/// <remarks>
/// Dibuat sebagai singleton per proses: hash entri terakhir di-cache di memori supaya tidak perlu
/// membaca ulang seluruh file setiap kali menulis. Akses dari beberapa proses berbeda ke file yang
/// sama secara bersamaan belum didukung -- batasan yang sama seperti FileKeyStore/FileUserStore.
/// </remarks>
public sealed class FileAuditLog : IAuditLog
{
    private static readonly string GenesisHash = new('0', 64);

    private readonly string _path;
    private readonly Lock _lock = new();
    private string? _cachedLastHash;

    public FileAuditLog(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _path = Path.Combine(rootDirectory, "audit.log");
    }

    public void Record(string actor, AuditAction action, string details)
    {
        lock (_lock)
        {
            var previousHash = GetLastHash();
            var timestamp = DateTimeOffset.UtcNow;
            var hash = ComputeHash(previousHash, timestamp, actor, action, details);
            var entry = new StoredAuditEntry(timestamp, actor, action, details, previousHash, hash);

            using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.WriteLine(JsonSerializer.Serialize(entry));
            }

            _cachedLastHash = hash;
        }
    }

    public IReadOnlyList<StoredAuditEntry> ReadAll()
    {
        lock (_lock)
        {
            return ReadAllFromDisk();
        }
    }

    public ChainVerificationResult VerifyChain()
    {
        var entries = ReadAll();
        var expectedPrevious = GenesisHash;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.PreviousHash != expectedPrevious)
            {
                return new ChainVerificationResult(false, i,
                    "PreviousHash tidak cocok dengan hash entri sebelumnya -- rantai terputus (entri disisipkan/dihapus/diurutkan ulang).");
            }

            var recomputed = ComputeHash(entry.PreviousHash, entry.Timestamp, entry.Actor, entry.Action, entry.Details);
            if (recomputed != entry.Hash)
            {
                return new ChainVerificationResult(false, i,
                    "Hash entri tidak cocok dengan isinya -- kemungkinan entri ini telah diubah.");
            }

            expectedPrevious = entry.Hash;
        }

        return new ChainVerificationResult(true, null, null);
    }

    private string GetLastHash()
    {
        if (_cachedLastHash is not null)
            return _cachedLastHash;

        var entries = ReadAllFromDisk();
        _cachedLastHash = entries.Count > 0 ? entries[^1].Hash : GenesisHash;
        return _cachedLastHash;
    }

    private List<StoredAuditEntry> ReadAllFromDisk()
    {
        if (!File.Exists(_path))
            return [];

        var result = new List<StoredAuditEntry>();
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var entry = JsonSerializer.Deserialize<StoredAuditEntry>(line)
                ?? throw new InvalidDataException("Baris audit log tidak valid (JSON kosong/null).");
            result.Add(entry);
        }

        return result;
    }

    private static string ComputeHash(string previousHash, DateTimeOffset timestamp, string actor, AuditAction action, string details)
    {
        var payload = $"{previousHash}|{timestamp:O}|{actor}|{action}|{details}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(bytes);
    }
}
