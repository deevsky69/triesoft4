using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Triesoft.App.Views;

namespace Triesoft.App.Converters;

/// <summary>
/// Data path 20x20 untuk tiap <see cref="NavIconKind"/>. Sengaja hanya garis lurus (tanpa busur/arc) --
/// lebih mudah dipastikan benar tanpa alat gambar vektor, dan cukup untuk gaya ikon monoline sederhana ini.
/// </summary>
public sealed class NavIconKindToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NavIconKind kind ? Geometry.Parse(DataFor(kind)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string DataFor(NavIconKind kind) => kind switch
    {
        // Gembok tertutup: badan kotak + sengkang persegi (bukan busur) menyentuh badan di kedua kaki.
        NavIconKind.Lock => "M4,9 L4,17 L16,17 L16,9 Z M7,9 L7,5 L13,5 L13,9",

        // Gembok terbuka: badan sama, tapi sengkang terayun ke atas -- kaki kiri lepas dari badan
        // (bukan cuma dipendekkan) supaya jelas beda dari gembok tertutup di ukuran kecil.
        NavIconKind.Unlock => "M4,9 L4,17 L16,17 L16,9 Z M13,9 L13,5 L5,3",

        // Anak kunci: kepala wajik + batang + dua gigi.
        NavIconKind.Key => "M3,6 L6,3 L9,6 L6,9 Z M9,6 L16,6 M12,6 L12,9 M15,6 L15,9",

        // Panah ke atas (kirim/distribusi).
        NavIconKind.Share => "M10,3 L10,15 M5,8 L10,3 L15,8",

        // Satu figur orang: kepala wajik kecil + badan segitiga.
        NavIconKind.People => "M10,3 L12,5 L10,7 L8,5 Z M5,17 L10,9 L15,17 Z",

        // Tiga garis (daftar/log).
        NavIconKind.List => "M4,6 L16,6 M4,10 L16,10 M4,14 L16,14",

        // Wajik + garis vertikal di tengah ("i"). SENGAJA bukan garis vertikal murni (semua x=10) --
        // itu membuat bounding box geometri selebar nol dan merusak Stretch="Uniform" (icon jadi tak
        // tampak sama sekali). Wajik memberi lebar 2D yang nyata untuk bounding box-nya.
        NavIconKind.Info => "M10,3 L15,8 L10,13 L5,8 Z M10,5 L10,11",

        // Pintu keluar + panah.
        NavIconKind.Exit => "M11,4 L5,4 L5,16 L11,16 M8,10 L16,10 M13,7 L16,10 L13,13",

        _ => "",
    };
}
