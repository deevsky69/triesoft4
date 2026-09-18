using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Triesoft.Core.Identity;

namespace Triesoft.App.Converters;

public sealed class UserStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value is UserStatus status
            ? status switch
            {
                UserStatus.Active => "Brush.Success",
                UserStatus.PendingVerification => "Brush.Warning",
                UserStatus.Disabled => "Brush.Neutral",
                _ => "Brush.Neutral",
            }
            : "Brush.Neutral";

        return Application.Current?.TryGetResource(resourceKey, null, out var brush) == true ? brush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
