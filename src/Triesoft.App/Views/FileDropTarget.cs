using Avalonia.Controls;
using Avalonia.Input;
using Triesoft.App.ViewModels;

namespace Triesoft.App.Views;

/// <summary>
/// Menjadikan sebuah View penerima drag and drop file/folder untuk <see cref="BatchFileViewModelBase"/> (DataContext-nya).
/// Sorotan memakai class <c>drop-active</c> pada kontrol <c>highlight</c>. Drop ditolak selama proses berjalan.
/// </summary>
public static class FileDropTarget
{
    public const string ActiveClass = "drop-active";

    public static void Attach(Control target, Control highlight)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragEnterEvent, (_, e) => OnDragOver(target, highlight, e));
        target.AddHandler(DragDrop.DragOverEvent, (_, e) => OnDragOver(target, highlight, e));
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => highlight.Classes.Set(ActiveClass, false));
        target.AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(target, highlight, e));
    }

    private static void OnDragOver(Control target, Control highlight, DragEventArgs e)
    {
        var accept = CanAccept(target, e);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        highlight.Classes.Set(ActiveClass, accept);
        e.Handled = true;
    }

    private static void OnDrop(Control target, Control highlight, DragEventArgs e)
    {
        highlight.Classes.Set(ActiveClass, false);
        e.Handled = true;
        if (target.DataContext is not BatchFileViewModelBase { IsBusy: false } vm) return;

        var paths = GetPaths(e);
        if (paths.Count > 0)
            vm.AddDropped(paths);
    }

    private static bool CanAccept(Control target, DragEventArgs e) =>
        target.DataContext is BatchFileViewModelBase { IsBusy: false } && GetPaths(e).Count > 0;

    private static List<string> GetPaths(DragEventArgs e) =>
        (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
}
