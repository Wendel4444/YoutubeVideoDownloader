using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace YoutubeDownloader.App.Services;

/// <summary>QR code do Pix, gerado localmente a partir do código "copia e cola".</summary>
public static class PixQrCode
{
    public static byte[] Png(string payload, int pixelsPerModule = 10) =>
        PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.M, pixelsPerModule);

    public static ImageSource Image(string payload)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(Png(payload));
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
