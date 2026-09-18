using CommunityToolkit.Mvvm.ComponentModel;

namespace Triesoft.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    protected void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }
}
