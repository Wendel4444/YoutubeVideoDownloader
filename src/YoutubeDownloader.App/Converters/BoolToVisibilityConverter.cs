using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YoutubeDownloader.App.Converters;

/// <summary>
/// bool → Visibility. Também aceita números (0 = falso), strings (vazia = falso) e null,
/// para evitar propriedades "HasX" só para a view.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            string s => s.Length > 0,
            _ => true,
        };
        return truthy != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
