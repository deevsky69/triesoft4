using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.Core.Audit;

namespace Triesoft.App.ViewModels;

public partial class AuditLogViewModel : ViewModelBase
{
    private readonly IAuditLog _auditLog;

    public ObservableCollection<StoredAuditEntry> Entries { get; } = [];

    public AuditLogViewModel(IAuditLog auditLog)
    {
        _auditLog = auditLog;
        Load();
    }

    [RelayCommand]
    private void Reload()
    {
        ClearMessages();
        Load();
    }

    [RelayCommand]
    private void VerifyIntegrity()
    {
        var result = _auditLog.VerifyChain();
        if (result.IsIntact)
        {
            ErrorMessage = null;
            SuccessMessage = $"Log utuh -- {Entries.Count} entri, tidak ada perubahan terdeteksi.";
        }
        else
        {
            SuccessMessage = null;
            ErrorMessage = $"LOG RUSAK pada entri #{result.FailureIndex}: {result.FailureReason}";
        }
    }

    private void Load()
    {
        Entries.Clear();
        foreach (var e in _auditLog.ReadAll().OrderByDescending(e => e.Timestamp))
            Entries.Add(e);
    }
}
