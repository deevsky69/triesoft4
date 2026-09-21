using System.Security.Cryptography;
using Triesoft.Core.KeyDistribution;
using Triesoft.Core.KeyManagement;
using Triesoft.Core.Tests.KeyManagement;
using Xunit;

namespace Triesoft.Core.Tests.KeyDistribution;

public class KeyDistributionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("triesoft4-distribution-test-").FullName;

    // Satu "mesin" Mabes (penerbit) dan dua mesin Polda, masing-masing dengan store sendiri.
    private readonly Node _mabes;
    private readonly Node _jatim;
    private readonly Node _jabar;

    public KeyDistributionTests()
    {
        _mabes = new Node(Path.Combine(_root, "mabes"));
        _jatim = new Node(Path.Combine(_root, "jatim"));
        _jabar = new Node(Path.Combine(_root, "jabar"));

        // Bootstrap kepercayaan: Mabes mendaftarkan public key Polda, Polda mem-pin public key Mabes.
        _mabes.Distribution.AddRecipient("POLDA JATIM", _jatim.Distribution.GetOrCreateRecipientPublicKey());
        _mabes.Distribution.AddRecipient("POLDA JABAR", _jabar.Distribution.GetOrCreateRecipientPublicKey());
        var mabesPublic = _mabes.Distribution.GetOrCreateIssuerPublicKey();
        _jatim.Distribution.SetTrustedIssuer("Bidsandi Mabes", mabesPublic);
        _jabar.Distribution.SetTrustedIssuer("Bidsandi Mabes", mabesPublic);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Until = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private KeyPackage Issue(Node recipient, string keyId = "2026-09-TEST", bool importLocally = false) =>
        _mabes.Manager.IssueMonthlyKey(
            keyId, From, Until,
            [KeyFingerprint.Compute(recipient.Distribution.GetOrCreateRecipientPublicKey())],
            importLocally).Single().Package;

    [Fact]
    public void RoundTrip_PackageImportsAndYieldsSameKeyOnMabesAndPolda()
    {
        var package = Issue(_jatim, importLocally: true);

        _jatim.Manager.ImportPackage(KeyPackage.Parse(package.ToJson()));

        using var jatimKey = _jatim.Keys.GetActiveKeyForEncryption();
        using var mabesKey = _mabes.Keys.GetActiveKeyForEncryption();
        Assert.Equal("2026-09-TEST", jatimKey.KeyId);

        // Bukti kunci identik: file yang dienkripsi Mabes bisa didekripsi Polda.
        var plaintext = "berita rahasia"u8.ToArray();
        var result = EncryptThenDecrypt(plaintext, mabesKey, jatimKey, out var decrypted);
        Assert.Equal("a.txt", result.OriginalFileName);
        Assert.Equal(plaintext, decrypted);
    }

    private static Crypto.DecryptResult EncryptThenDecrypt(byte[] plaintext, Crypto.MonthlyKey encryptKey, Crypto.MonthlyKey decryptKey, out byte[] decrypted)
    {
        using var encrypted = new MemoryStream();
        Crypto.EnvelopeCipher.Encrypt(new MemoryStream(plaintext), encrypted, encryptKey, "a.txt");
        encrypted.Position = 0;
        using var output = new MemoryStream();
        var result = Crypto.EnvelopeCipher.Decrypt(encrypted, output, decryptKey);
        decrypted = output.ToArray();
        return result;
    }

    [Fact]
    public void ImportedKey_KeepsValidityFromPackage()
    {
        _jatim.Manager.ImportPackage(Issue(_jatim));

        var meta = _jatim.Keys.ListKeys().Single();
        Assert.Equal(KeyStatus.Active, meta.Status);
        Assert.Equal(From, meta.ValidFrom);
        Assert.Equal(Until, meta.ValidUntil);
    }

    [Fact]
    public void Package_ForAnotherPolda_IsRejected()
    {
        var forJatim = Issue(_jatim);

        var ex = Assert.Throws<KeyDistributionException>(() => _jabar.Manager.ImportPackage(forJatim));
        Assert.Contains("mesin lain", ex.Message);
        Assert.Empty(_jabar.Keys.ListKeys());
    }

    [Fact]
    public void Package_FromUntrustedIssuer_IsRejected()
    {
        using var rogueDir = new TempDir();
        var rogue = new Node(rogueDir.Path);
        rogue.Distribution.AddRecipient("POLDA JATIM", _jatim.Distribution.GetOrCreateRecipientPublicKey());
        rogue.Distribution.GetOrCreateIssuerPublicKey();
        var forged = rogue.Manager.IssueMonthlyKey(
            "2026-09-FORGED", From, Until,
            [KeyFingerprint.Compute(_jatim.Distribution.GetOrCreateRecipientPublicKey())], false).Single().Package;

        var ex = Assert.Throws<KeyDistributionException>(() => _jatim.Manager.ImportPackage(forged));
        Assert.Contains("tidak dipercaya", ex.Message);
        Assert.Empty(_jatim.Keys.ListKeys());
    }

    [Theory]
    [InlineData("keyId")]
    [InlineData("validUntil")]
    [InlineData("wrappedKey")]
    [InlineData("tag")]
    [InlineData("signature")]
    public void TamperedPackage_IsRejected(string field)
    {
        var package = Issue(_jatim);
        var tampered = field switch
        {
            "keyId" => package with { KeyId = "2026-09-EVIL" },
            "validUntil" => package with { ValidUntil = Until.AddYears(10) },
            "wrappedKey" => package with { WrappedKey = Flip(package.WrappedKey) },
            "tag" => package with { Tag = Flip(package.Tag) },
            _ => package with { Signature = Flip(package.Signature) },
        };

        Assert.Throws<KeyDistributionException>(() => _jatim.Manager.ImportPackage(tampered));
        Assert.Empty(_jatim.Keys.ListKeys());
    }

    [Fact]
    public void ImportWithoutTrustedIssuer_IsRejected()
    {
        using var freshDir = new TempDir();
        var fresh = new Node(freshDir.Path);
        _mabes.Distribution.AddRecipient("POLDA BARU", fresh.Distribution.GetOrCreateRecipientPublicKey());
        var package = _mabes.Manager.IssueMonthlyKey(
            "2026-09-NEW", From, Until,
            [KeyFingerprint.Compute(fresh.Distribution.GetOrCreateRecipientPublicKey())], false).Single().Package;

        var ex = Assert.Throws<KeyDistributionException>(() => fresh.Manager.ImportPackage(package));
        Assert.Contains("penerbit tepercaya", ex.Message);
    }

    [Fact]
    public void IssueToMultipleRecipients_ProducesDistinctPackagesForSameKey()
    {
        var packages = _mabes.Manager.IssueMonthlyKey(
            "2026-09-ALL", From, Until,
            _mabes.Distribution.ListRecipients().Select(r => r.Fingerprint).ToList(), false);

        Assert.Equal(2, packages.Count);
        Assert.NotEqual(packages[0].Package.WrappedKey, packages[1].Package.WrappedKey);

        _jatim.Manager.ImportPackage(packages.Single(p => p.Recipient.Name == "POLDA JATIM").Package);
        _jabar.Manager.ImportPackage(packages.Single(p => p.Recipient.Name == "POLDA JABAR").Package);

        using var a = _jatim.Keys.GetActiveKeyForEncryption();
        using var b = _jabar.Keys.GetActiveKeyForEncryption();
        EncryptThenDecrypt("x"u8.ToArray(), a, b, out var decrypted);
        Assert.Equal("x"u8.ToArray(), decrypted);
    }

    [Fact]
    public void Issue_RejectsReusedKeyIdAndUnknownRecipient()
    {
        _mabes.Manager.IssueMonthlyKey("2026-09-DUP", From, Until,
            [_mabes.Distribution.ListRecipients()[0].Fingerprint], importLocally: true);

        Assert.Throws<ArgumentException>(() => _mabes.Manager.IssueMonthlyKey("2026-09-DUP", From, Until,
            [_mabes.Distribution.ListRecipients()[0].Fingerprint], false));
        Assert.Throws<KeyNotFoundException>(() => _mabes.Manager.IssueMonthlyKey("2026-09-X", From, Until,
            ["AA:BB"], false));
    }

    [Fact]
    public void Issue_WithoutIssuerIdentity_Fails()
    {
        // Polda biasa tidak punya identitas penerbit sehingga tidak bisa menerbitkan paket.
        _jatim.Distribution.AddRecipient("X", _jabar.Distribution.GetOrCreateRecipientPublicKey());
        Assert.Throws<KeyDistributionException>(() => _jatim.Manager.IssueMonthlyKey("2026-09-X", From, Until,
            [_jatim.Distribution.ListRecipients()[0].Fingerprint], false));
    }

    [Fact]
    public void PrivateKeys_AreReloadedFromDisk_AndStablePublicKey()
    {
        var first = _jatim.Distribution.GetOrCreateRecipientPublicKey();
        var reopened = new FileDistributionStore(Path.Combine(_root, "jatim", "dist"), new PlaintextKeyProtectorForTests());
        Assert.Equal(first, reopened.GetOrCreateRecipientPublicKey());
    }

    [Fact]
    public void AddRecipient_RejectsDuplicatesAndInvalidKeys()
    {
        var jatimKey = _jatim.Distribution.GetOrCreateRecipientPublicKey();
        Assert.Throws<ArgumentException>(() => _mabes.Distribution.AddRecipient("LAIN", jatimKey));
        Assert.Throws<KeyDistributionException>(() => _mabes.Distribution.AddRecipient("RUSAK", [1, 2, 3]));

        // kunci P-256 ditolak: harus P-384
        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<KeyDistributionException>(() => _mabes.Distribution.AddRecipient("P256", p256.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void PublicKeyFile_RoundTripsAndRecomputesFingerprint()
    {
        var publicKey = _jatim.Distribution.GetOrCreateRecipientPublicKey();
        var json = new PublicKeyFile(PublicKeyKind.Recipient, "POLDA JATIM", publicKey).ToJson();

        var parsed = PublicKeyFile.Parse(json);

        Assert.Equal(PublicKeyKind.Recipient, parsed.Kind);
        Assert.Equal("POLDA JATIM", parsed.Name);
        Assert.Equal(KeyFingerprint.Compute(publicKey), parsed.Fingerprint);
        Assert.Throws<KeyDistributionException>(() => PublicKeyFile.Parse("bukan json"));
    }

    [Fact]
    public void KeyPackage_ParseRejectsGarbage()
    {
        Assert.Throws<KeyDistributionException>(() => KeyPackage.Parse("{}"));
        Assert.Throws<KeyDistributionException>(() => KeyPackage.Parse("bukan json"));
    }

    private static byte[] Flip(byte[] data)
    {
        var copy = (byte[])data.Clone();
        copy[0] ^= 0x01;
        return copy;
    }

    private sealed class Node
    {
        public FileDistributionStore Distribution { get; }
        public MonthlyKeyManager Keys { get; }
        public KeyDistributionManager Manager { get; }

        public Node(string dir)
        {
            var protector = new PlaintextKeyProtectorForTests();
            Distribution = new FileDistributionStore(Path.Combine(dir, "dist"), protector);
            Keys = new MonthlyKeyManager(new FileKeyStore(Path.Combine(dir, "keys"), protector));
            Manager = new KeyDistributionManager(Distribution, Keys);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("triesoft4-distribution-extra-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
