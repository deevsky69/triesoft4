using System.Text.Json;
using System.Text.Json.Serialization;

namespace Triesoft.App.Services;

public enum ThemePreference
{
    Light,
    Dark,
}

/// <summary>
/// Preferensi tampilan per mesin (bukan per user -- disimpan di <see cref="AppPaths.DataRoot"/>, dibaca sekali
/// saat start). Sengaja minimal: cuma tema, dan cuma dua pilihan (tidak ada "ikut sistem" -- mendeteksi tema
/// OS secara benar di titik paling awal startup tidak cukup pasti untuk fitur ini). Perubahan tema baru
/// berlaku setelah aplikasi dibuka ulang: <c>Theme/Colors.Dark.axaml</c> digabung menimpa <c>Colors.axaml</c>
/// di <c>App.axaml.cs</c> saat start, bukan berpindah langsung saat aplikasi berjalan. Lihat catatan yang
/// sama di HANDOFF.md.
/// </summary>
public sealed record AppPreferences(ThemePreference Theme)
{
    public static readonly AppPreferences Default = new(ThemePreference.Light);

    // Enum ditulis sebagai teks ("Light"/"Dark"), bukan angka bawaan System.Text.Json -- lebih tahan
    // kalau file ini suatu saat dibaca/diedit manual, dan tidak diam-diam salah baca kalau urutan
    // anggota enum berubah nanti.
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Default;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions) ?? Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preferensi tampilan tidak kritis -- kalau file rusak/tidak terbaca, jatuh ke bawaan daripada gagal start.
            return Default;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
