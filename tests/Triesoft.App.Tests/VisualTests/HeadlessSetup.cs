using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;

namespace Triesoft.App.Tests.VisualTests;

/// <summary>
/// Inisialisasi platform Avalonia headless sekali per proses test, supaya View bisa di-render
/// ke bitmap tanpa perlu display server sungguhan -- dipakai untuk verifikasi visual, bukan
/// bagian dari aplikasi produksi.
/// </summary>
internal static class HeadlessSetup
{
    private static readonly object Lock = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (Lock)
        {
            if (_initialized) return;

            // UseHeadlessDrawing=false supaya benar-benar merender lewat Skia (menghasilkan bitmap
            // sungguhan untuk diperiksa) -- default-nya true cuma mensimulasikan layout tanpa rasterisasi.
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            _initialized = true;
        }
    }
}
