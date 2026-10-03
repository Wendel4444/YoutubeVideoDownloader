using System.Windows;
using System.Windows.Media;
using YoutubeDownloader.App.Core;
using Microsoft.Win32;

namespace YoutubeDownloader.App.Services;

/// <summary>
/// Aplica a paleta no início do app. Os estilos usam DynamicResource, mas as views leem as cores
/// uma vez ao abrir; por isso trocar de tema pede reinício, o que mantém o resto do código simples.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(Application app, AppTheme theme)
    {
        IsDark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.System => !WindowsUsesLightApps(),
            _ => true,
        };

        var palette = new ResourceDictionary
        {
            Source = new Uri(IsDark ? "Themes/Colors.Dark.xaml" : "Themes/Colors.Light.xaml", UriKind.Relative),
        };
        app.Resources.MergedDictionaries[0] = palette;
    }

    /// <summary>Cor da barra de título no formato COLORREF (0x00BBGGRR), a partir da cor da sidebar.</summary>
    public static int CaptionColor(Application app) =>
        app.TryFindResource("Color.Sidebar") is Color c ? c.R | (c.G << 8) | (c.B << 16) : 0x001A1512;

    private static bool WindowsUsesLightApps()
    {
        // Leitura simples do registro do usuário (sem rede, sem permissões elevadas).
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
    }
}
