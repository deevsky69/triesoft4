using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Triesoft.App.ViewModels;

namespace Triesoft.App.Converters;

public sealed class BatchStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value is BatchStatus status
            ? status switch
            {
                BatchStatus.Running => "Brush.Accent",
                BatchStatus.Succeeded => "Brush.Success",
                BatchStatus.Failed => "Brush.Danger",
                BatchStatus.Canceled => "Brush.Warning",
                _ => "Brush.Neutral",
            }
            : "Brush.Neutral";

        return Application.Current?.TryGetResource(resourceKey, null, out var brush) == true ? brush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
