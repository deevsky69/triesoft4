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
/// Kunci publik yang dinyatakan tidak boleh dipercaya lagi (dicabut atau diganti lewat rotasi).
/// Pernyataan ini final: kunci yang sudah tercatat di sini ditolak oleh <see cref="FileDistributionStore.AddRecipient"/>
/// dan <see cref="FileDistributionStore.SetTrustedIssuer"/>. Jejaknya juga sengaja dipertahankan untuk audit.
/// </summary>
public sealed record RevokedKey(string Fingerprint, PublicKeyKind Kind, string Name, string Reason, DateTimeOffset RevokedAt);

/// <summary>
/// Penyimpanan identitas distribusi kunci mesin ini, dalam satu direktori:
/// <list type="bullet">
/// <item><c>recipient.key</c> -- kunci privat ECDH (penerima; dipakai Polda membuka paket), terproteksi <see cref="IKeyProtector"/>.</item>
/// <item><c>issuer.key</c> -- kunci privat ECDSA (penerbit; hanya ada di mesin Mabes), terproteksi <see cref="IKeyProtector"/>.</item>
/// <item><c>trusted-issuer.json</c> -- kunci publik Mabes yang dipercaya (pinned) oleh Polda ini.</item>
/// <item><c>recipients.json</c> -- daftar kunci publik Polda yang terdaftar (dipakai Mabes).</item>
/// <item><c>revoked.json</c> -- kunci publik yang dicabut/diganti; tidak bisa didaftarkan atau dipercaya lagi.</item>
/// </list>
/// Kunci privat dibuat di mesin ini dan tidak pernah diekspor. Seperti store lainnya, hanya satu proses per direktori.
/// Rotasi menimpa file kunci privat lama dengan byte acak sebelum diganti (upaya terbaik -- pada SSD/OneDrive
/// salinan lama bisa masih tersisa, jadi kebocoran sungguhan tetap harus diikuti pencabutan kunci bulanan terkait).
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
    private string RevokedPath => Path.Combine(_directory, "revoked.json");

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
            ThrowIfRevoked(KeyFingerprint.Compute(publicKey));
            File.WriteAllText(TrustedIssuerPath, JsonSerializer.Serialize(new TrustedParty(name, publicKey)));
        }
    }

    /// <summary>
    /// Mencabut kepercayaan pada penerbit saat ini (mis. kunci Mabes dicurigai bocor). Semua paket dari penerbit itu
    /// langsung ditolak, dan kunci publiknya tidak bisa dipercaya lagi. Kepercayaan baru butuh kunci publik penerbit yang baru.
    /// </summary>
    public TrustedParty RevokeTrustedIssuer(string reason)
    {
        RequireReason(reason);
        lock (_lock)
        {
            var issuer = GetTrustedIssuer()
                ?? throw new KeyDistributionException("Belum ada penerbit tepercaya untuk dicabut.");
            AddRevoked(new RevokedKey(issuer.Fingerprint, PublicKeyKind.Issuer, issuer.Name, reason.Trim(), DateTimeOffset.UtcNow));
            File.Delete(TrustedIssuerPath);
            return issuer;
        }
    }

    /// <summary>
    /// Mengganti kunci penerima mesin ini dengan pasangan baru dan menghancurkan kunci privat lama. Kunci publik baru
    /// harus diekspor ulang dan didaftarkan ke Mabes; paket yang dibuat untuk kunci lama tidak bisa dibuka lagi.
    /// Mengembalikan kunci publik yang baru.
    /// </summary>
    public byte[] RotateRecipientKey(string reason)
    {
        RequireReason(reason);
        lock (_lock)
        {
            if (!File.Exists(RecipientKeyPath))
                throw new KeyDistributionException("Belum ada kunci penerima untuk dirotasi.");

            byte[] oldPublic;
            using (var old = LoadOrCreateEcdh())
                oldPublic = old.ExportSubjectPublicKeyInfo();

            using var fresh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
            ReplaceKeyFile(RecipientKeyPath, fresh.ExportPkcs8PrivateKey());
            AddRevoked(new RevokedKey(KeyFingerprint.Compute(oldPublic), PublicKeyKind.Recipient,
                "kunci penerima mesin ini (lama)", reason.Trim(), DateTimeOffset.UtcNow));
            return fresh.ExportSubjectPublicKeyInfo();
        }
    }

    /// <summary>
    /// Mengganti kunci penerbit mesin ini (Mabes) dengan pasangan baru dan menghancurkan kunci privat lama. Setiap Polda harus
    /// mempercayai kunci publik baru; sampai itu dilakukan, paket baru ditolak. Paket yang sudah diimpor tidak terpengaruh.
    /// Mengembalikan kunci publik yang baru.
    /// </summary>
    public byte[] RotateIssuerKey(string reason)
    {
        RequireReason(reason);
        lock (_lock)
        {
            if (!HasIssuerIdentity)
                throw new KeyDistributionException("Mesin ini belum diaktifkan sebagai penerbit kunci.");

            byte[] oldPublic;
            using (var old = LoadOrCreateEcdsa())
                oldPublic = old.ExportSubjectPublicKeyInfo();

            using var fresh = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            ReplaceKeyFile(IssuerKeyPath, fresh.ExportPkcs8PrivateKey());
            AddRevoked(new RevokedKey(KeyFingerprint.Compute(oldPublic), PublicKeyKind.Issuer,
                "kunci penerbit mesin ini (lama)", reason.Trim(), DateTimeOffset.UtcNow));
            return fresh.ExportSubjectPublicKeyInfo();
        }
    }

    public IReadOnlyList<RevokedKey> ListRevoked()
    {
        lock (_lock)
        {
            return ReadRevoked();
        }
    }

    public bool IsRevoked(string fingerprint)
    {
        lock (_lock)
        {
            return ReadRevoked().Any(r => r.Fingerprint == fingerprint);
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
            ThrowIfRevoked(fingerprint);
            if (list.Any(r => r.Fingerprint == fingerprint))
                throw new ArgumentException($"Kunci publik ini sudah terdaftar sebagai '{list.First(r => r.Fingerprint == fingerprint).Name}'.");
            if (list.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Nama penerima '{name}' sudah dipakai.");

            list.Add(new TrustedParty(name, publicKey));
            File.WriteAllText(RecipientsPath, JsonSerializer.Serialize(list));
        }
    }

    /// <summary>
    /// Mencabut penerima (mis. kunci privat Polda bocor atau mesinnya hilang): dikeluarkan dari daftar dan kunci publiknya
    /// tidak bisa didaftarkan lagi. Paket yang sudah terbit untuk penerima itu tidak bisa ditarik kembali.
    /// </summary>
    public TrustedParty RevokeRecipient(string fingerprint, string reason)
    {
        RequireReason(reason);
        lock (_lock)
        {
            var list = ReadRecipients();
            var recipient = list.FirstOrDefault(r => r.Fingerprint == fingerprint)
                ?? throw new KeyNotFoundException("Penerima tidak ditemukan.");
            list.Remove(recipient);
            AddRevoked(new RevokedKey(fingerprint, PublicKeyKind.Recipient, recipient.Name, reason.Trim(), DateTimeOffset.UtcNow));
            File.WriteAllText(RecipientsPath, JsonSerializer.Serialize(list));
            return recipient;
        }
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Alasan wajib diisi.");
    }

    private void ThrowIfRevoked(string fingerprint)
    {
        var revoked = ReadRevoked().FirstOrDefault(r => r.Fingerprint == fingerprint);
        if (revoked is not null)
            throw new KeyDistributionException($"Kunci publik ini sudah dicabut ({revoked.Reason}) dan tidak bisa dipercaya lagi.");
    }

    private void AddRevoked(RevokedKey entry)
    {
        var list = ReadRevoked();
        if (list.Any(r => r.Fingerprint == entry.Fingerprint)) return;
        list.Add(entry);
        File.WriteAllText(RevokedPath, JsonSerializer.Serialize(list));
    }

    private List<RevokedKey> ReadRevoked() =>
        File.Exists(RevokedPath)
            ? JsonSerializer.Deserialize<List<RevokedKey>>(File.ReadAllText(RevokedPath)) ?? []
            : [];

    /// <summary>Menulis kunci baru ke file sementara, menimpa file lama dengan byte acak, lalu menukar. Kunci lama tidak lagi bisa dibaca.</summary>
    private void ReplaceKeyFile(string path, byte[] pkcs8)
    {
        try
        {
            var temp = path + ".new";
            File.WriteAllBytes(temp, _protector.Protect(pkcs8));

            var length = (int)new FileInfo(path).Length;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Write(RandomNumberGenerator.GetBytes(length));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
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
