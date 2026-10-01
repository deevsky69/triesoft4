using Triesoft.App.Services;
using Xunit;

namespace Triesoft.App.Tests.Services;

/// <summary>
/// Memutar env var TRIESOFT4_DATA_DIR proses-lebar. Risiko: kalau ini kebetulan berjalan bersamaan
/// dengan panggilan PERTAMA HeadlessSetup.EnsureInitialized() di ScreenshotTests (yang membaca
/// preferensi tema lewat env var yang sama), tema screenshot proses itu bisa ikut berubah -- kosmetik
/// saja (langsung kelihatan di PNG-nya), bukan silent corruption, jadi diterima sebagai batasan kecil
/// daripada menambah kerumitan isolasi collection xUnit untuk risiko yang sempit ini.
/// </summary>
public class AppPreferencesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-prefs-").FullName;
    private readonly string? _original = Environment.GetEnvironmentVariable("TRIESOFT4_DATA_DIR");

    public AppPreferencesTests() => Environment.SetEnvironmentVariable("TRIESOFT4_DATA_DIR", _dir);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TRIESOFT4_DATA_DIR", _original);
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WithoutFile_ReturnsDefaultLight() =>
        Assert.Equal(ThemePreference.Light, AppPreferences.Load().Theme);

    [Fact]
    public void SaveThenLoad_RoundTripsDarkPreference()
    {
        new AppPreferences(ThemePreference.Dark).Save();

        Assert.Equal(ThemePreference.Dark, AppPreferences.Load().Theme);
    }

    [Fact]
    public void Save_WritesThemeAsReadableText_NotANumber()
    {
        new AppPreferences(ThemePreference.Dark).Save();

        var json = File.ReadAllText(Path.Combine(_dir, "preferences.json"));
        Assert.Contains("\"Dark\"", json);
    }

    [Fact]
    public void Load_WithCorruptFile_FallsBackToDefault_InsteadOfThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "preferences.json"), "{ ini bukan json valid");

        Assert.Equal(ThemePreference.Light, AppPreferences.Load().Theme);
    }
}
