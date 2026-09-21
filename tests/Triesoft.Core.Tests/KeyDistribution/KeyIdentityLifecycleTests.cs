using Triesoft.Core.KeyDistribution;
using Triesoft.Core.KeyManagement;
using Triesoft.Core.Tests.KeyManagement;
using Xunit;

namespace Triesoft.Core.Tests.KeyDistribution;

/// <summary>Rotasi dan pencabutan kunci identitas distribusi (penerima Polda dan penerbit Mabes).</summary>
public class KeyIdentityLifecycleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("triesoft4-identity-test-").FullName;
    private readonly Node _mabes;
    private readonly Node _jatim;

    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Until = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public KeyIdentityLifecycleTests()
    {
        _mabes = new Node(Path.Combine(_root, "mabes"));
        _jatim = new Node(Path.Combine(_root, "jatim"));
        _mabes.Store.AddRecipient("POLDA JATIM", _jatim.Store.GetOrCreateRecipientPublicKey());
        _jatim.Store.SetTrustedIssuer("Bidsandi Mabes", _mabes.Store.GetOrCreateIssuerPublicKey());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private KeyPackage IssueToJatim(string keyId) =>
        _mabes.Manager.IssueMonthlyKey(keyId, From, Until,
            [_mabes.Store.ListRecipients().Single().Fingerprint], importLocally: false).Single().Package;

    // --- rotasi kunci penerima (Polda) -----------------------------------------------------------

    [Fact]
    public void RotateRecipientKey_ChangesPublicKey_AndRevokesOldFingerprint()
    {
        var oldPublic = _jatim.Store.GetOrCreateRecipientPublicKey();

        var newPublic = _jatim.Store.RotateRecipientKey("rotasi berkala");

        Assert.NotEqual(oldPublic, newPublic);
        Assert.Equal(newPublic, _jatim.Store.GetOrCreateRecipientPublicKey());
        var revoked = Assert.Single(_jatim.Store.ListRevoked());
        Assert.Equal(KeyFingerprint.Compute(oldPublic), revoked.Fingerprint);
        Assert.Equal(PublicKeyKind.Recipient, revoked.Kind);
        Assert.Equal("rotasi berkala", revoked.Reason);
    }

    [Fact]
    public void RotateRecipientKey_PackageForOldKeyCanNoLongerBeImported()
    {
        var packageForOldKey = IssueToJatim("2026-09-OLD");

        _jatim.Store.RotateRecipientKey("kunci privat hilang");

        var ex = Assert.Throws<KeyDistributionException>(() => _jatim.Manager.ImportPackage(packageForOldKey));
        Assert.Contains("mesin lain", ex.Message);
        Assert.Empty(_jatim.Keys.ListKeys());
    }

    [Fact]
    public void RotateRecipientKey_AfterReRegistration_NewPackagesWork()
    {
        var oldFingerprint = _mabes.Store.ListRecipients().Single().Fingerprint;
        var newPublic = _jatim.Store.RotateRecipientKey("rotasi berkala");

        // Mabes mengganti pendaftaran: cabut yang lama, daftarkan yang baru (setelah sidik jari dicocokkan).
        _mabes.Store.RevokeRecipient(oldFingerprint, "diganti rotasi");
        _mabes.Store.AddRecipient("POLDA JATIM", newPublic);

        _jatim.Manager.ImportPackage(IssueToJatim("2026-10-NEW"));
        Assert.Equal("2026-10-NEW", _jatim.Keys.ListKeys().Single().KeyId);
    }

    [Fact]
    public void RotateRecipientKey_OverwritesOldPrivateKeyFile_AndSurvivesReopen()
    {
        var keyFile = Path.Combine(_root, "jatim", "dist", "recipient.key");
        var before = File.ReadAllBytes(keyFile);

        var newPublic = _jatim.Store.RotateRecipientKey("uji");

        Assert.NotEqual(before, File.ReadAllBytes(keyFile));
        Assert.False(File.Exists(keyFile + ".new"));
        var reopened = new FileDistributionStore(Path.Combine(_root, "jatim", "dist"), new PlaintextKeyProtectorForTests());
        Assert.Equal(newPublic, reopened.GetOrCreateRecipientPublicKey());
        Assert.Single(reopened.ListRevoked());
    }

    // --- rotasi kunci penerbit (Mabes) -------------------------------------------------------------

    [Fact]
    public void RotateIssuerKey_NewPackagesRejectedUntilPoldaTrustsNewKey()
    {
        var oldIssuerPublic = _mabes.Store.GetOrCreateIssuerPublicKey();
        var newIssuerPublic = _mabes.Store.RotateIssuerKey("rotasi berkala");
        Assert.NotEqual(oldIssuerPublic, newIssuerPublic);

        var package = IssueToJatim("2026-10-NEW");
        var ex = Assert.Throws<KeyDistributionException>(() => _jatim.Manager.ImportPackage(package));
        Assert.Contains("tidak dipercaya", ex.Message);

        _jatim.Store.SetTrustedIssuer("Bidsandi Mabes", newIssuerPublic);
        _jatim.Manager.ImportPackage(package);
        Assert.Equal("2026-10-NEW", _jatim.Keys.ListKeys().Single().KeyId);
    }

    [Fact]
    public void RotateIssuerKey_AlreadyImportedKeysAreUnaffected()
    {
        _jatim.Manager.ImportPackage(IssueToJatim("2026-09-OLD"));

        _mabes.Store.RotateIssuerKey("rotasi berkala");

        Assert.Equal(KeyStatus.Active, _jatim.Keys.ListKeys().Single().Status);
    }

    [Fact]
    public void RotateIssuerKey_RequiresIssuerIdentityAndReason()
    {
        Assert.Throws<KeyDistributionException>(() => _jatim.Store.RotateIssuerKey("x"));
        Assert.Throws<ArgumentException>(() => _mabes.Store.RotateIssuerKey("  "));
        Assert.Throws<ArgumentException>(() => _jatim.Store.RotateRecipientKey(""));
    }

    // --- pencabutan penerbit tepercaya (Polda) -------------------------------------------------------

    [Fact]
    public void RevokeTrustedIssuer_RejectsItsPackagesWithClearMessage()
    {
        var package = IssueToJatim("2026-09-X");

        _jatim.Store.RevokeTrustedIssuer("kunci Mabes dicurigai bocor");

        Assert.Null(_jatim.Store.GetTrustedIssuer());
        var ex = Assert.Throws<KeyDistributionException>(() => _jatim.Manager.ImportPackage(package));
        Assert.Contains("dicabut", ex.Message);
        Assert.Contains("dicurigai bocor", ex.Message);
        Assert.Empty(_jatim.Keys.ListKeys());
    }

    [Fact]
    public void RevokeTrustedIssuer_RevokedKeyCannotBeTrustedAgain()
    {
        var issuerPublic = _mabes.Store.GetOrCreateIssuerPublicKey();
        _jatim.Store.RevokeTrustedIssuer("bocor");

        var ex = Assert.Throws<KeyDistributionException>(() => _jatim.Store.SetTrustedIssuer("Bidsandi Mabes", issuerPublic));
        Assert.Contains("sudah dicabut", ex.Message);
    }

    [Fact]
    public void RevokeTrustedIssuer_ThenTrustNewKey_Works()
    {
        _jatim.Store.RevokeTrustedIssuer("bocor");
        var newIssuerPublic = _mabes.Store.RotateIssuerKey("bocor");

        _jatim.Store.SetTrustedIssuer("Bidsandi Mabes", newIssuerPublic);
        _jatim.Manager.ImportPackage(IssueToJatim("2026-10-NEW"));

        Assert.Single(_jatim.Keys.ListKeys());
    }

    [Fact]
    public void RevokeTrustedIssuer_WithoutIssuer_Fails()
    {
        _jatim.Store.RevokeTrustedIssuer("a");
        Assert.Throws<KeyDistributionException>(() => _jatim.Store.RevokeTrustedIssuer("b"));
    }

    // --- pencabutan penerima (Mabes) -------------------------------------------------------------------

    [Fact]
    public void RevokeRecipient_RemovesFromListAndBlocksReRegistration()
    {
        var jatimPublic = _jatim.Store.GetOrCreateRecipientPublicKey();
        var fingerprint = KeyFingerprint.Compute(jatimPublic);

        var revoked = _mabes.Store.RevokeRecipient(fingerprint, "mesin Polda hilang");

        Assert.Equal("POLDA JATIM", revoked.Name);
        Assert.Empty(_mabes.Store.ListRecipients());
        var ex = Assert.Throws<KeyDistributionException>(() => _mabes.Store.AddRecipient("POLDA JATIM", jatimPublic));
        Assert.Contains("sudah dicabut", ex.Message);
        Assert.Throws<KeyNotFoundException>(() => _mabes.Manager.IssueMonthlyKey("2026-10", From, Until, [fingerprint], false));
    }

    [Fact]
    public void RevokeRecipient_DoesNotRecallPackagesAlreadyIssued()
    {
        // Batasan yang disengaja dan terdokumentasi: paket yang sudah keluar tidak bisa ditarik. Karena itu
        // kebocoran kunci privat Polda harus diikuti pencabutan kunci bulanan terkait (Kelola Kunci).
        var package = IssueToJatim("2026-09-ISSUED");
        _mabes.Store.RevokeRecipient(_mabes.Store.ListRecipients().Single().Fingerprint, "bocor");

        _jatim.Manager.ImportPackage(package);

        Assert.Single(_jatim.Keys.ListKeys());
    }

    [Fact]
    public void RevokeRecipient_RequiresReasonAndKnownRecipient()
    {
        var fingerprint = _mabes.Store.ListRecipients().Single().Fingerprint;
        Assert.Throws<ArgumentException>(() => _mabes.Store.RevokeRecipient(fingerprint, ""));
        Assert.Throws<KeyNotFoundException>(() => _mabes.Store.RevokeRecipient("AA:BB", "x"));
        Assert.Single(_mabes.Store.ListRecipients());
    }

    private sealed class Node
    {
        public FileDistributionStore Store { get; }
        public MonthlyKeyManager Keys { get; }
        public KeyDistributionManager Manager { get; }

        public Node(string dir)
        {
            var protector = new PlaintextKeyProtectorForTests();
            Store = new FileDistributionStore(Path.Combine(dir, "dist"), protector);
            Keys = new MonthlyKeyManager(new FileKeyStore(Path.Combine(dir, "keys"), protector));
            Manager = new KeyDistributionManager(Store, Keys);
        }
    }
}
