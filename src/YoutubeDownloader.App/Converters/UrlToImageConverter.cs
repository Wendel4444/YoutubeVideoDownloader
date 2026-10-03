using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace YoutubeDownloader.App.Converters;

/// <summary>Endereço de miniatura → imagem baixada em segundo plano, decodificada já no tamanho de exibição.</summary>
public sealed class UrlToImageConverter : IValueConverter
{
    public int DecodeWidth { get; set; } = 320;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        image.DecodePixelWidth = DecodeWidth;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
