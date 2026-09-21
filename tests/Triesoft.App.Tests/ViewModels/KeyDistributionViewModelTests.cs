using Triesoft.App.Services;
using Triesoft.App.ViewModels;
using Triesoft.Core.Audit;
using Triesoft.Core.Identity;
using Triesoft.Core.KeyDistribution;
using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.App.Tests.ViewModels;

public class KeyDistributionViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("triesoft4-vm-distribution-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed record Machine(KeyDistributionViewModel Vm, MonthlyKeyManager Keys, FileAuditLog Audit, string Dir);

    private Machine NewMachine(string name)
    {
        var dir = Path.Combine(_root, name);
        var protector = new PassthroughKeyProtector();
        var keys = new MonthlyKeyManager(new FileKeyStore(Path.Combine(dir, "keys"), protector));
        var store = new FileDistributionStore(Path.Combine(dir, "dist"), protector);
        var audit = new FileAuditLog(Path.Combine(dir, "audit"));
        var session = new SessionContext();
        session.SignIn(new UserAccount("admin1", "Admin", UserRole.Admin, UserStatus.Active, [], [], DateTimeOffset.UtcNow, "system"));
        var vm = new KeyDistributionViewModel(store, new KeyDistributionManager(store, keys), audit, session) { OwnName = name };
        return new Machine(vm, keys, audit, dir);
    }

    [Fact]
    public void FullFlow_MabesIssuesPackage_PoldaImportsIt()
    {
        var mabes = NewMachine("MABES");
        var jatim = NewMachine("POLDA-JATIM");
        var mabesKeyFile = Path.Combine(_root, "mabes.ts4pub");
        var jatimKeyFile = Path.Combine(_root, "jatim.ts4pub");

        mabes.Vm.ActivateIssuerCommand.Execute(null);
        mabes.Vm.ExportIssuerKey(mabesKeyFile);
        jatim.Vm.ExportRecipientKey(jatimKeyFile);
        Assert.Null(mabes.Vm.ErrorMessage);
        Assert.Null(jatim.Vm.ErrorMessage);

        // Polda mem-pin Mabes, Mabes mendaftarkan Polda -- masing-masing lewat pratinjau + konfirmasi.
        Assert.NotNull(jatim.Vm.PreviewPublicKeyFile(mabesKeyFile, PublicKeyKind.Issuer));
        jatim.Vm.CommitPendingIssuer();
        Assert.NotNull(mabes.Vm.PreviewPublicKeyFile(jatimKeyFile, PublicKeyKind.Recipient));
        mabes.Vm.CommitPendingRecipient();
        Assert.Single(mabes.Vm.Recipients);

        var outDir = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        mabes.Vm.Recipients[0].IsSelected = true;
        mabes.Vm.IssueKeyId = "2026-10";
        mabes.Vm.IssuePackages(outDir);
        Assert.Null(mabes.Vm.ErrorMessage);

        var packageFile = Assert.Single(Directory.GetFiles(outDir, "*.ts4kp"));
        jatim.Vm.ImportPackageFile(packageFile);

        Assert.Null(jatim.Vm.ErrorMessage);
        Assert.Equal("2026-10", jatim.Keys.ListKeys().Single().KeyId);
        Assert.Equal("2026-10", mabes.Keys.ListKeys().Single().KeyId); // salinan lokal default

        Assert.Contains(mabes.Audit.ReadAll(), e => e.Action == AuditAction.KeyPackageIssued);
        Assert.Contains(jatim.Audit.ReadAll(), e => e.Action == AuditAction.KeyPackageImported);
    }

    [Fact]
    public void PreviewPublicKeyFile_RejectsWrongKind()
    {
        var mabes = NewMachine("MABES");
        var jatim = NewMachine("POLDA-JATIM");
        var jatimKeyFile = Path.Combine(_root, "jatim.ts4pub");
        jatim.Vm.ExportRecipientKey(jatimKeyFile);

        // file penerima tidak boleh dipercaya sebagai penerbit
        Assert.Null(mabes.Vm.PreviewPublicKeyFile(jatimKeyFile, PublicKeyKind.Issuer));
        Assert.NotNull(mabes.Vm.ErrorMessage);
    }

    [Fact]
    public void ImportPackage_WithoutTrustedIssuer_ShowsErrorAndAuditsFailure()
    {
        var mabes = NewMachine("MABES");
        var jatim = NewMachine("POLDA-JATIM");
        var jatimKeyFile = Path.Combine(_root, "jatim.ts4pub");
        jatim.Vm.ExportRecipientKey(jatimKeyFile);
        mabes.Vm.ActivateIssuerCommand.Execute(null);
        mabes.Vm.PreviewPublicKeyFile(jatimKeyFile, PublicKeyKind.Recipient);
        mabes.Vm.CommitPendingRecipient();
        var outDir = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        mabes.Vm.Recipients[0].IsSelected = true;
        mabes.Vm.IssueKeyId = "2026-10";
        mabes.Vm.IssuePackages(outDir);

        jatim.Vm.ImportPackageFile(Directory.GetFiles(outDir, "*.ts4kp").Single());

        Assert.Contains("penerbit tepercaya", jatim.Vm.ErrorMessage);
        Assert.Empty(jatim.Keys.ListKeys());
        Assert.Contains(jatim.Audit.ReadAll(), e => e.Action == AuditAction.KeyPackageImportFailed);
    }

    [Fact]
    public void IssuePackages_WithoutSelectedRecipient_ShowsError()
    {
        var mabes = NewMachine("MABES");
        mabes.Vm.ActivateIssuerCommand.Execute(null);
        mabes.Vm.IssueKeyId = "2026-10";

        mabes.Vm.IssuePackages(_root);

        Assert.Contains("minimal satu penerima", mabes.Vm.ErrorMessage);
    }
}
