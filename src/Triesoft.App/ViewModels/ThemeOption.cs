using Triesoft.App.Services;

namespace Triesoft.App.ViewModels;

/// <summary>Satu pilihan tema di layar Tentang: nilai preferensi + label Indonesia untuk ComboBox.</summary>
public sealed record ThemeOption(ThemePreference Value, string Name)
{
    public static readonly IReadOnlyList<ThemeOption> All =
    [
        new(ThemePreference.Light, "Terang"),
        new(ThemePreference.Dark, "Gelap"),
    ];

    public override string ToString() => Name;
}
