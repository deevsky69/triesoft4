using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Triesoft.Core.KeyManagement;

namespace Triesoft.App.Converters;

public sealed class KeyStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value is KeyStatus status
            ? status switch
            {
                KeyStatus.Active => "Brush.Success",
                KeyStatus.Expired => "Brush.Neutral",
                KeyStatus.Revoked => "Brush.Danger",
                KeyStatus.Purged => "Brush.Neutral",
                _ => "Brush.Neutral",
            }
            : "Brush.Neutral";

        return Application.Current?.TryGetResource(resourceKey, null, out var brush) == true ? brush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
