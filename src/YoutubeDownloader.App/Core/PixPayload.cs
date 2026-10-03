using System.Text;

namespace YoutubeDownloader.App.Core;

/// <summary>
/// Código Pix estático "copia e cola" (BR Code, padrão EMV do Banco Central), sem valor definido:
/// quem paga escolhe quanto doar.
/// </summary>
public static class PixPayload
{
    public const string Key = "delsanvfx@gmail.com";
    public const string ReceiverName = "YOUTUBE DOWNLOADER";
    public const string City = "SAO PAULO";

    public static string Donation { get; } = Build(Key, ReceiverName, City);

    public static string Build(string key, string name, string city, string txid = "***")
    {
        var account = Field("00", "br.gov.bcb.pix") + Field("01", key);
        var payload = new StringBuilder()
            .Append(Field("00", "01"))                 // versão do payload
            .Append(Field("26", account))              // conta Pix
            .Append(Field("52", "0000"))               // categoria do comerciante
            .Append(Field("53", "986"))                // moeda: real
            .Append(Field("58", "BR"))
            .Append(Field("59", Truncate(name, 25)))
            .Append(Field("60", Truncate(city, 15)))
            .Append(Field("62", Field("05", txid)))
            .Append("6304")
            .ToString();
        return payload + Crc16(payload).ToString("X4");
    }

    private static string Field(string id, string value) => id + value.Length.ToString("00") + value;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>CRC16-CCITT (polinômio 0x1021, início 0xFFFF), como pede o BR Code.</summary>
    private static ushort Crc16(string text)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc;
    }
}
