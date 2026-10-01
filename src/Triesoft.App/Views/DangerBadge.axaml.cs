using Avalonia.Controls;

namespace Triesoft.App.Views;

/// <summary>Lingkaran merah + "!" -- dipakai di <see cref="MessageBanner"/> untuk pesan error dan di <see cref="Dialogs.ConfirmDialog"/> untuk aksi destruktif.</summary>
public partial class DangerBadge : UserControl
{
    public DangerBadge()
    {
        InitializeComponent();
    }
}
