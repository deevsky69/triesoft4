using System.Globalization;
using Avalonia.Data.Converters;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.Converters;

/// <summary>
/// Peringatan singkat kalau kunci Active akan kedaluwarsa dalam waktu dekat (&lt;=7 hari), atau null
/// kalau tidak perlu diperingatkan (bukan Active, atau masih lama). Murni tampilan -- tidak menyimpan
/// apa pun, supaya tidak perlu mengubah KeyMetadata (dan format index.json) di Triesoft.Core.
/// Dipakai untuk DUA target sekaligus dari binding yang sama (Text dan IsVisible): keluarannya
/// menyesuaikan <paramref name="targetType"/> supaya tidak perlu converter/MultiBinding terpisah.
/// </summary>
public sealed class KeyExpiryWarningConverter : IValueConverter
{
    private const int WarnWithinDays = 7;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var message = value is KeyMetadata meta && meta.Status == KeyStatus.Active
            ? Describe((meta.ValidUntil - DateTimeOffset.UtcNow).TotalDays)
            : null;

        if (targetType == typeof(bool)) return message is not null;
        return message;
    }

    private static string? Describe(double daysLeft) => daysLeft switch
    {
        < 0 => "Sudah lewat tanggal berakhir",
        <= WarnWithinDays => $"Berakhir {Math.Ceiling(daysLeft):0} hari lagi",
        _ => null,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
