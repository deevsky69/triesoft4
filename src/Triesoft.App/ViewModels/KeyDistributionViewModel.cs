using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.KeyDistribution;

namespace Triesoft.App.ViewModels;

/// <summary>Satu penerima terdaftar di daftar penerbitan, dengan kotak centang pilihan.</summary>
public partial class RecipientItem(TrustedParty party) : ObservableObject
{
    public TrustedParty Party { get; } = party;
    public string Name => Party.Name;
    public string Fingerprint => Party.Fingerprint;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Distribusi kunci bulanan Mabes -> Polda. Satu mesin bisa berperan sebagai penerima (semua mesin) dan/atau
/// penerbit (Mabes, setelah "Aktifkan sebagai Penerbit"). Operasi berkas dipicu dari code-behind View
/// (file picker) dan masuk sebagai path ke method di sini.
/// </summary>
public partial class KeyDistributionViewModel : ViewModelBase
{
    private readonly FileDistributionStore _store;
    private readonly KeyDistributionManager _manager;
    private readonly IAuditLog _auditLog;
    private readonly SessionContext _session;

    private PublicKeyFile? _pending;

    private string Actor => _session.CurrentUser?.Username ?? "unknown";

    public ObservableCollection<RecipientItem> Recipients { get; } = [];

    public ObservableCollection<RevokedKey> Revoked { get; } = [];

    /// <summary>Alasan untuk rotasi/pencabutan berikutnya. Wajib, dan tercatat di audit log.</summary>
    [ObservableProperty]
    private string _identityChangeReason = "";

    [ObservableProperty]
    private bool _hasTrustedIssuer;

    /// <summary>Nama yang ditulis ke file kunci publik yang diekspor mesin ini (mis. "POLDA JATIM").</summary>
    [ObservableProperty]
    private string _ownName = Environment.MachineName;

    [ObservableProperty]
    private string _recipientFingerprint = "";

    [ObservableProperty]
    private bool _hasIssuerIdentity;

    [ObservableProperty]
    private string _issuerFingerprint = "";

    [ObservableProperty]
    private string _trustedIssuerText = "";

    [ObservableProperty]
    private string _issueKeyId = "";

    [ObservableProperty]
    private DateTime? _issueValidFrom = DateTime.UtcNow.Date;

    [ObservableProperty]
    private DateTime? _issueValidUntil = DateTime.UtcNow.Date.AddMonths(1);

    [ObservableProperty]
    private bool _importLocally = true;

    public bool HasNoRecipients => Recipients.Count == 0;
    public bool HasRevoked => Revoked.Count > 0;

    public string IssuerFingerprintText => HasIssuerIdentity ? IssuerFingerprint : "Mesin ini bukan penerbit";

    public KeyDistributionViewModel(
        FileDistributionStore store, KeyDistributionManager manager, IAuditLog auditLog, SessionContext session)
    {
        _store = store;
        _manager = manager;
        _auditLog = auditLog;
        _session = session;
        Refresh();
    }

    private void Refresh()
    {
        RecipientFingerprint = KeyFingerprint.Compute(_store.GetOrCreateRecipientPublicKey());

        HasIssuerIdentity = _store.HasIssuerIdentity;
        IssuerFingerprint = HasIssuerIdentity ? KeyFingerprint.Compute(_store.GetOrCreateIssuerPublicKey()) : "";
        OnPropertyChanged(nameof(IssuerFingerprintText));

        var trusted = _store.GetTrustedIssuer();
        HasTrustedIssuer = trusted is not null;
        TrustedIssuerText = trusted is null
            ? "Belum ada penerbit tepercaya -- paket kunci belum bisa diimpor."
            : $"{trusted.Name}\n{trusted.Fingerprint}";

        Recipients.Clear();
        foreach (var r in _store.ListRecipients())
            Recipients.Add(new RecipientItem(r));
        OnPropertyChanged(nameof(HasNoRecipients));

        Revoked.Clear();
        foreach (var r in _store.ListRevoked().OrderByDescending(r => r.RevokedAt))
            Revoked.Add(r);
        OnPropertyChanged(nameof(HasRevoked));
    }

    // --- identitas mesin ini ------------------------------------------------------------------

    public void ExportRecipientKey(string path) => Run(() =>
    {
        WritePublicKey(path, PublicKeyKind.Recipient, _store.GetOrCreateRecipientPublicKey());
        SuccessMessage = $"Kunci publik penerima diekspor ke {path}. Serahkan ke Bidsandi Mabes bersama sidik jari di atas.";
    });

    [RelayCommand]
    private void ActivateIssuer() => Run(() =>
    {
        if (_store.HasIssuerIdentity) return;
        _store.GetOrCreateIssuerPublicKey();
        _auditLog.Record(Actor, AuditAction.IssuerIdentityCreated, "");
        Refresh();
        SuccessMessage = "Mesin ini sekarang menjadi penerbit kunci. Ekspor kunci publik penerbit dan bagikan ke tiap Polda.";
    });

    public void ExportIssuerKey(string path) => Run(() =>
    {
        WritePublicKey(path, PublicKeyKind.Issuer, _store.GetOrCreateIssuerPublicKey());
        SuccessMessage = $"Kunci publik penerbit diekspor ke {path}. Polda wajib memverifikasi sidik jarinya lewat jalur terpisah.";
    });

    private void WritePublicKey(string path, PublicKeyKind kind, byte[] publicKey)
    {
        if (string.IsNullOrWhiteSpace(OwnName))
            throw new InvalidOperationException("Nama mesin/satuan wajib diisi sebelum mengekspor.");
        File.WriteAllText(path, new PublicKeyFile(kind, OwnName.Trim(), publicKey).ToJson());
    }

    // --- mendaftarkan pihak lain (perlu konfirmasi sidik jari) -----------------------------------

    /// <summary>Membaca file kunci publik dan menahannya sampai dikonfirmasi. Mengembalikan null (dengan ErrorMessage) kalau ditolak.</summary>
    public PublicKeyFile? PreviewPublicKeyFile(string path, PublicKeyKind expectedKind)
    {
        ClearMessages();
        _pending = null;
        try
        {
            var file = PublicKeyFile.Parse(File.ReadAllText(path));
            if (file.Kind != expectedKind)
            {
                ErrorMessage = expectedKind == PublicKeyKind.Issuer
                    ? "File ini kunci publik penerima, bukan penerbit (Mabes)."
                    : "File ini kunci publik penerbit, bukan penerima (Polda).";
                return null;
            }
            _pending = file;
            return file;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    /// <summary>Dipanggil dari code-behind SETELAH Admin mengonfirmasi sidik jari.</summary>
    public void CommitPendingIssuer() => Run(() =>
    {
        var file = _pending ?? throw new InvalidOperationException("Tidak ada kunci publik yang menunggu konfirmasi.");
        _store.SetTrustedIssuer(file.Name, file.PublicKey);
        _auditLog.Record(Actor, AuditAction.IssuerTrusted, $"nama={file.Name}, sidikJari={file.Fingerprint}");
        _pending = null;
        Refresh();
        SuccessMessage = $"Penerbit '{file.Name}' dipercaya.";
    });

    /// <summary>Dipanggil dari code-behind SETELAH Admin mengonfirmasi sidik jari.</summary>
    public void CommitPendingRecipient() => Run(() =>
    {
        var file = _pending ?? throw new InvalidOperationException("Tidak ada kunci publik yang menunggu konfirmasi.");
        _store.AddRecipient(file.Name, file.PublicKey);
        _auditLog.Record(Actor, AuditAction.RecipientRegistered, $"nama={file.Name}, sidikJari={file.Fingerprint}");
        _pending = null;
        Refresh();
        SuccessMessage = $"Penerima '{file.Name}' terdaftar.";
    });

    // --- rotasi dan pencabutan (perlu konfirmasi di View; alasan wajib) ------------------------------

    /// <summary>Dipanggil View SEBELUM dialog konfirmasi, supaya Admin tidak mengonfirmasi aksi yang pasti ditolak.</summary>
    public bool RequireReasonForChange()
    {
        ClearMessages();
        if (!string.IsNullOrWhiteSpace(IdentityChangeReason)) return true;
        ErrorMessage = "Alasan wajib diisi untuk rotasi atau pencabutan.";
        return false;
    }

    /// <summary>Mabes: mencabut penerima. Dipanggil dari code-behind SETELAH konfirmasi.</summary>
    public void RevokeRecipient(RecipientItem item) => Run(() =>
    {
        var reason = IdentityChangeReason;
        _store.RevokeRecipient(item.Fingerprint, reason);
        _auditLog.Record(Actor, AuditAction.RecipientRevoked, $"nama={item.Name}, sidikJari={item.Fingerprint}, alasan={reason.Trim()}");
        IdentityChangeReason = "";
        Refresh();
        SuccessMessage = $"Penerima '{item.Name}' dicabut. Paket yang sudah terlanjur terbit untuknya tidak bisa ditarik -- " +
                         "kalau kunci privatnya bocor, cabut juga kunci bulanan terkait di Kelola Kunci.";
    });

    /// <summary>Polda: mencabut kepercayaan pada penerbit saat ini. Dipanggil dari code-behind SETELAH konfirmasi.</summary>
    [RelayCommand]
    private void RevokeTrustedIssuer() => Run(() =>
    {
        var reason = IdentityChangeReason;
        var issuer = _store.RevokeTrustedIssuer(reason);
        _auditLog.Record(Actor, AuditAction.IssuerRevoked, $"nama={issuer.Name}, sidikJari={issuer.Fingerprint}, alasan={reason.Trim()}");
        IdentityChangeReason = "";
        Refresh();
        SuccessMessage = $"Penerbit '{issuer.Name}' dicabut. Paket darinya kini ditolak sampai Anda mempercayai kunci publik penerbit yang baru.";
    });

    /// <summary>Mengganti kunci penerima mesin ini. Dipanggil dari code-behind SETELAH konfirmasi.</summary>
    [RelayCommand]
    private void RotateRecipientKey() => Run(() =>
    {
        var reason = IdentityChangeReason;
        var oldFingerprint = RecipientFingerprint;
        _store.RotateRecipientKey(reason);
        _auditLog.Record(Actor, AuditAction.RecipientKeyRotated, $"sidikJariLama={oldFingerprint}, alasan={reason.Trim()}");
        IdentityChangeReason = "";
        Refresh();
        SuccessMessage = "Kunci penerima diganti dan kunci lama dihancurkan. Ekspor ulang kunci publik penerima, serahkan ke Mabes, " +
                         "lalu cocokkan sidik jari baru. Paket yang dibuat untuk kunci lama tidak bisa diimpor lagi.";
    });

    /// <summary>Mabes: mengganti kunci penerbit. Dipanggil dari code-behind SETELAH konfirmasi.</summary>
    [RelayCommand]
    private void RotateIssuerKey() => Run(() =>
    {
        var reason = IdentityChangeReason;
        var oldFingerprint = IssuerFingerprint;
        _store.RotateIssuerKey(reason);
        _auditLog.Record(Actor, AuditAction.IssuerKeyRotated, $"sidikJariLama={oldFingerprint}, alasan={reason.Trim()}");
        IdentityChangeReason = "";
        Refresh();
        SuccessMessage = "Kunci penerbit diganti dan kunci lama dihancurkan. Ekspor kunci publik penerbit yang baru dan kirim ke tiap Polda; " +
                         "paket baru ditolak sampai Polda mempercayai kunci itu. Kunci bulanan yang sudah diimpor tidak terpengaruh.";
    });

    // --- sisi penerbit (Mabes) ----------------------------------------------------------------------

    public void IssuePackages(string outputDirectory) => Run(() =>
    {
        if (IssueValidFrom is null || IssueValidUntil is null)
            throw new InvalidOperationException("Tanggal berlaku wajib diisi.");

        var keyId = IssueKeyId.Trim();
        var selected = Recipients.Where(r => r.IsSelected).ToList();
        var validFrom = new DateTimeOffset(DateTime.SpecifyKind(IssueValidFrom.Value, DateTimeKind.Utc));
        var validUntil = new DateTimeOffset(DateTime.SpecifyKind(IssueValidUntil.Value, DateTimeKind.Utc));

        var issued = _manager.IssueMonthlyKey(keyId, validFrom, validUntil,
            selected.Select(r => r.Fingerprint).ToList(), ImportLocally);

        foreach (var (recipient, package) in issued)
        {
            var path = Path.Combine(outputDirectory, $"{keyId}_{SafeFileName(recipient.Name)}.ts4kp");
            File.WriteAllText(path, package.ToJson());
            _auditLog.Record(Actor, AuditAction.KeyPackageIssued,
                $"keyId={keyId}, penerima={recipient.Name}, sidikJari={recipient.Fingerprint}, salinanLokal={ImportLocally}");
        }

        IssueKeyId = "";
        foreach (var r in Recipients) r.IsSelected = false;
        SuccessMessage = $"{issued.Count} paket kunci dibuat di {outputDirectory}.";
    });

    // --- sisi penerima (Polda) ----------------------------------------------------------------------

    public void ImportPackageFile(string path)
    {
        ClearMessages();
        KeyPackage? package = null;
        try
        {
            package = KeyPackage.Parse(File.ReadAllText(path));
            _manager.ImportPackage(package);
            _auditLog.Record(Actor, AuditAction.KeyPackageImported, $"keyId={package.KeyId}, penerbit={package.IssuerFingerprint}");
            SuccessMessage = $"Kunci '{package.KeyId}' berhasil diimpor sebagai Active.";
        }
        catch (Exception ex)
        {
            _auditLog.Record(Actor, AuditAction.KeyPackageImportFailed, $"file={Path.GetFileName(path)}, error={ex.Message}");
            ErrorMessage = ex.Message;
        }
    }

    private void Run(Action action)
    {
        ClearMessages();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
