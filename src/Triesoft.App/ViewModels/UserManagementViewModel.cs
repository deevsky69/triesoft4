using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.Core.Audit;
using Triesoft.Core.Identity;

namespace Triesoft.App.ViewModels;

public partial class UserManagementViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly IAuditLog _auditLog;

    public ObservableCollection<UserAccount> Users { get; } = [];

    public IReadOnlyList<UserRole> AvailableRoles { get; } = Enum.GetValues<UserRole>();

    /// <summary>Diisi MainShellViewModel dari user yang sedang login, dipakai sebagai CreatedBy.</summary>
    public string CurrentAdminUsername { get; set; } = "";

    [ObservableProperty]
    private string _newUsername = "";

    [ObservableProperty]
    private string _newDisplayName = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private UserRole _newRole = UserRole.Operator;

    [ObservableProperty]
    private UserAccount? _selectedUser;

    public UserManagementViewModel(AuthService authService, IAuditLog auditLog)
    {
        _authService = authService;
        _auditLog = auditLog;
        Refresh();
    }

    private void Refresh()
    {
        Users.Clear();
        foreach (var u in _authService.ListUsers().OrderByDescending(u => u.CreatedAt))
            Users.Add(u);
    }

    [RelayCommand]
    private void CreateUser()
    {
        ClearMessages();
        try
        {
            var username = NewUsername.Trim();
            _authService.RegisterUser(username, NewDisplayName.Trim(), NewPassword, NewRole, CurrentAdminUsername);
            _auditLog.Record(CurrentAdminUsername, AuditAction.UserRegistered, $"username={username}, role={NewRole}");
            NewUsername = "";
            NewDisplayName = "";
            NewPassword = "";
            SuccessMessage = "User berhasil didaftarkan, menunggu verifikasi.";
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void VerifySelected()
    {
        ClearMessages();
        if (SelectedUser is null)
        {
            ErrorMessage = "Pilih user yang akan diverifikasi.";
            return;
        }

        try
        {
            _authService.VerifyUser(SelectedUser.Username);
            _auditLog.Record(CurrentAdminUsername, AuditAction.UserVerified, $"username={SelectedUser.Username}");
            SuccessMessage = $"User '{SelectedUser.Username}' terverifikasi dan bisa login.";
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Dipanggil dari code-behind SETELAH dialog konfirmasi (aksi tidak reversibel).</summary>
    [RelayCommand]
    private void DisableSelected()
    {
        ClearMessages();
        if (SelectedUser is null)
        {
            ErrorMessage = "Pilih user yang akan dinonaktifkan.";
            return;
        }

        try
        {
            _authService.DisableUser(SelectedUser.Username);
            _auditLog.Record(CurrentAdminUsername, AuditAction.UserDisabled, $"username={SelectedUser.Username}");
            SuccessMessage = $"User '{SelectedUser.Username}' dinonaktifkan.";
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
