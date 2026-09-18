using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Audit;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.ViewModels;

public partial class KeyManagementViewModel : ViewModelBase
{
    private readonly MonthlyKeyManager _keyManager;
    private readonly IAuditLog _auditLog;
    private readonly SessionContext _session;

    private string Actor => _session.CurrentUser?.Username ?? "unknown";

    public ObservableCollection<KeyMetadata> Keys { get; } = [];

    [ObservableProperty]
    private string _newKeyId = "";

    [ObservableProperty]
    private string _newKeyHex = "";

    // CalendarDatePicker.SelectedDate bertipe DateTime? (bukan DateTimeOffset?) -- dikonversi
    // saat dipakai memanggil MonthlyKeyManager.ImportMonthlyKey di bawah.
    [ObservableProperty]
    private DateTime? _newValidFrom = DateTime.UtcNow.Date;

    [ObservableProperty]
    private DateTime? _newValidUntil = DateTime.UtcNow.Date.AddMonths(1);

    [ObservableProperty]
    private KeyMetadata? _selectedKey;

    [ObservableProperty]
    private string _revokeReason = "";

    public KeyManagementViewModel(MonthlyKeyManager keyManager, IAuditLog auditLog, SessionContext session)
    {
        _keyManager = keyManager;
        _auditLog = auditLog;
        _session = session;
        Refresh();
    }

    private void Refresh()
    {
        Keys.Clear();
        foreach (var k in _keyManager.ListKeys().OrderByDescending(k => k.ImportedAt))
            Keys.Add(k);
    }

    [RelayCommand]
    private void Import()
    {
        ClearMessages();
        if (NewValidFrom is null || NewValidUntil is null)
        {
            ErrorMessage = "Tanggal berlaku wajib diisi.";
            return;
        }

        try
        {
            var bytes = Convert.FromHexString(NewKeyHex.Trim());
            var validFrom = new DateTimeOffset(DateTime.SpecifyKind(NewValidFrom.Value, DateTimeKind.Utc));
            var validUntil = new DateTimeOffset(DateTime.SpecifyKind(NewValidUntil.Value, DateTimeKind.Utc));
            var keyId = NewKeyId.Trim();
            _keyManager.ImportMonthlyKey(keyId, bytes, validFrom, validUntil);
            _auditLog.Record(Actor, AuditAction.KeyImported, $"keyId={keyId}, validFrom={validFrom:yyyy-MM-dd}, validUntil={validUntil:yyyy-MM-dd}");
            NewKeyId = "";
            NewKeyHex = "";
            SuccessMessage = "Kunci berhasil diimpor sebagai Active.";
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Dipanggil dari code-behind SETELAH dialog konfirmasi (aksi tidak reversibel).</summary>
    [RelayCommand]
    private void RevokeSelected()
    {
        ClearMessages();
        if (SelectedKey is null)
        {
            ErrorMessage = "Pilih kunci yang akan dicabut.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RevokeReason))
        {
            ErrorMessage = "Alasan pencabutan wajib diisi.";
            return;
        }

        try
        {
            var reason = RevokeReason.Trim();
            _keyManager.RevokeKey(SelectedKey.KeyId, reason);
            _auditLog.Record(Actor, AuditAction.KeyRevoked, $"keyId={SelectedKey.KeyId}, alasan={reason}");
            SuccessMessage = $"Kunci '{SelectedKey.KeyId}' dicabut. Material sudah dihapus permanen.";
            RevokeReason = "";
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Dipanggil dari code-behind SETELAH dialog konfirmasi (aksi tidak reversibel).</summary>
    [RelayCommand]
    private void PurgeSelected()
    {
        ClearMessages();
        if (SelectedKey is null)
        {
            ErrorMessage = "Pilih kunci yang akan dihapus permanen.";
            return;
        }

        try
        {
            _keyManager.PurgeExpiredKey(SelectedKey.KeyId);
            _auditLog.Record(Actor, AuditAction.KeyPurged, $"keyId={SelectedKey.KeyId}");
            SuccessMessage = $"Kunci '{SelectedKey.KeyId}' dihapus permanen.";
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
