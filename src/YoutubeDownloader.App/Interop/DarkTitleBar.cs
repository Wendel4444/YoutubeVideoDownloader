using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using YoutubeDownloader.App.Services;

namespace YoutubeDownloader.App.Interop;

/// <summary>Deixa a barra de título nativa do Windows 11 na cor do tema do app.</summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dark = ThemeManager.IsDark ? 1 : 0;
        var caption = ThemeManager.CaptionColor(Application.Current);
        // Falhas são ignoradas: em versões antigas do Windows a barra apenas fica no padrão do sistema.
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
    }
}
