using System.Security.Cryptography;
using System.Text.Json;
using Triesoft.Core.KeyManagement;

namespace Triesoft.Core.KeyDistribution;

/// <summary>Pihak yang kunci publiknya sudah didaftarkan dan diverifikasi sidik jarinya oleh Admin.</summary>
public sealed record TrustedParty(string Name, byte[] PublicKey)
{
    public string Fingerprint => KeyFingerprint.Compute(PublicKey);
}

/// <summary>
/// Penyimpanan identitas distribusi kunci mesin ini, dalam satu direktori:
/// <list type="bullet">
/// <item><c>recipient.key</c> -- kunci privat ECDH (penerima; dipakai Polda membuka paket), terproteksi <see cref="IKeyProtector"/>.</item>
/// <item><c>issuer.key</c> -- kunci privat ECDSA (penerbit; hanya ada di mesin Mabes), terproteksi <see cref="IKeyProtector"/>.</item>
/// <item><c>trusted-issuer.json</c> -- kunci publik Mabes yang dipercaya (pinned) oleh Polda ini.</item>
/// <item><c>recipients.json</c> -- daftar kunci publik Polda yang terdaftar (dipakai Mabes).</item>
/// </list>
/// Kunci privat dibuat di mesin ini dan tidak pernah diekspor. Seperti store lainnya, hanya satu proses per direktori.
/// </summary>
public sealed class FileDistributionStore
{
    private readonly string _directory;
    private readonly IKeyProtector _protector;
    private readonly Lock _lock = new();

    public FileDistributionStore(string directory, IKeyProtector protector)
    {
        _directory = directory;
        _protector = protector;
        Directory.CreateDirectory(directory);
    }

    private string RecipientKeyPath => Path.Combine(_directory, "recipient.key");
    private string IssuerKeyPath => Path.Combine(_directory, "issuer.key");
    private string TrustedIssuerPath => Path.Combine(_directory, "trusted-issuer.json");
    private string RecipientsPath => Path.Combine(_directory, "recipients.json");

    public bool HasIssuerIdentity => File.Exists(IssuerKeyPath);

    /// <summary>Kunci publik penerima mesin ini (dibuat pada pemanggilan pertama).</summary>
    public byte[] GetOrCreateRecipientPublicKey()
    {
        lock (_lock)
        {
            using var key = LoadOrCreateEcdh();
            return key.ExportSubjectPublicKeyInfo();
        }
    }

    /// <summary>Kunci publik penerbit mesin ini (dibuat pada pemanggilan pertama -- menjadikan mesin ini penerbit/Mabes).</summary>
    public byte[] GetOrCreateIssuerPublicKey()
    {
        lock (_lock)
        {
            using var key = LoadOrCreateEcdsa();
            return key.ExportSubjectPublicKeyInfo();
        }
    }

    public ECDiffieHellman LoadRecipientKey()
    {
        lock (_lock)
        {
            return LoadOrCreateEcdh();
        }
    }

    public ECDsa LoadIssuerKey()
    {
        lock (_lock)
        {
            if (!HasIssuerIdentity)
                throw new KeyDistributionException("Mesin ini belum diaktifkan sebagai penerbit kunci.");
            return LoadOrCreateEcdsa();
        }
    }

    public TrustedParty? GetTrustedIssuer()
    {
        lock (_lock)
        {
            return File.Exists(TrustedIssuerPath)
                ? JsonSerializer.Deserialize<TrustedParty>(File.ReadAllText(TrustedIssuerPath))
                : null;
        }
    }

    /// <summary>Menetapkan (atau mengganti) kunci publik Mabes yang dipercaya. Pemanggil bertanggung jawab memastikan sidik jari sudah diverifikasi.</summary>
    public void SetTrustedIssuer(string name, byte[] publicKey)
    {
        KeyPackageCrypto.ImportEcdsaPublicKey(publicKey).Dispose();
        lock (_lock)
        {
            File.WriteAllText(TrustedIssuerPath, JsonSerializer.Serialize(new TrustedParty(name, publicKey)));
        }
    }

    public IReadOnlyList<TrustedParty> ListRecipients()
    {
        lock (_lock)
        {
            return ReadRecipients();
        }
    }

    public void AddRecipient(string name, byte[] publicKey)
    {
        KeyPackageCrypto.ImportEcdhPublicKey(publicKey).Dispose();
        lock (_lock)
        {
            var list = ReadRecipients();
            var fingerprint = KeyFingerprint.Compute(publicKey);
            if (list.Any(r => r.Fingerprint == fingerprint))
                throw new ArgumentException($"Kunci publik ini sudah terdaftar sebagai '{list.First(r => r.Fingerprint == fingerprint).Name}'.");
            if (list.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Nama penerima '{name}' sudah dipakai.");

            list.Add(new TrustedParty(name, publicKey));
            File.WriteAllText(RecipientsPath, JsonSerializer.Serialize(list));
        }
    }

    public void RemoveRecipient(string fingerprint)
    {
        lock (_lock)
        {
            var list = ReadRecipients();
            if (list.RemoveAll(r => r.Fingerprint == fingerprint) == 0)
                throw new KeyNotFoundException("Penerima tidak ditemukan.");
            File.WriteAllText(RecipientsPath, JsonSerializer.Serialize(list));
        }
    }

    private List<TrustedParty> ReadRecipients() =>
        File.Exists(RecipientsPath)
            ? JsonSerializer.Deserialize<List<TrustedParty>>(File.ReadAllText(RecipientsPath)) ?? []
            : [];

    private ECDiffieHellman LoadOrCreateEcdh()
    {
        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        try
        {
            LoadOrCreate(RecipientKeyPath, key.ExportPkcs8PrivateKey, bytes => key.ImportPkcs8PrivateKey(bytes, out _));
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private ECDsa LoadOrCreateEcdsa()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        try
        {
            LoadOrCreate(IssuerKeyPath, key.ExportPkcs8PrivateKey, bytes => key.ImportPkcs8PrivateKey(bytes, out _));
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Kalau file ada, impor isinya ke kunci; kalau belum, simpan kunci yang baru dibuat.</summary>
    private void LoadOrCreate(string path, Func<byte[]> export, Action<byte[]> import)
    {
        if (File.Exists(path))
        {
            var plain = _protector.Unprotect(File.ReadAllBytes(path));
            try { import(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            return;
        }

        var pkcs8 = export();
        try { File.WriteAllBytes(path, _protector.Protect(pkcs8)); }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }
}
