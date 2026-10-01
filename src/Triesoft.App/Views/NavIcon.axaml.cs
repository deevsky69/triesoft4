using Avalonia;
using Avalonia.Controls;

namespace Triesoft.App.Views;

/// <summary>Ikon sederhana (garis, tanpa isian) untuk item navigasi sidebar. Lihat <c>NavIconKindToGeometryConverter</c> untuk data path tiap kind.</summary>
public enum NavIconKind
{
    Lock,
    Unlock,
    Key,
    Share,
    People,
    List,
    Info,
    Exit,
}

public partial class NavIcon : UserControl
{
    public static readonly StyledProperty<NavIconKind> KindProperty =
        AvaloniaProperty.Register<NavIcon, NavIconKind>(nameof(Kind));

    public NavIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public NavIcon()
    {
        InitializeComponent();
    }
}
