using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Triesoft.App.Services;
using Triesoft.Core.Crypto;

namespace Triesoft.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    public string DataDirectory => AppPaths.DataRoot;

    public IReadOnlyList<AlgorithmOption> SupportedAlgorithms => AlgorithmOption.All;

    public IReadOnlyList<ThemeOption> ThemeOptions => ThemeOption.All;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    public AboutViewModel()
    {
        var current = AppPreferences.Load().Theme;
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == current) ?? ThemeOptions[0];
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        ClearMessages();
        new AppPreferences(SelectedTheme.Value).Save();
        SuccessMessage = "Tersimpan. Tutup dan buka lagi aplikasi supaya tema baru diterapkan.";
    }
}
