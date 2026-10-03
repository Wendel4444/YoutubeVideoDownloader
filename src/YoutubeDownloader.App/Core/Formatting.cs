using System.Globalization;

namespace YoutubeDownloader.App.Core;

public static class Formatting
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:0} B" : bytes.ToString(bytes >= 100 ? "0" : "0.0", PtBr) + " " + units[unit];
    }

    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } d)
            return "";
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{d.Minutes}:{d.Seconds:00}";
    }

    public static string Eta(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1)
            return $"{t.Minutes} min {t.Seconds:00} s";
        return $"{t.Seconds} s";
    }

    /// <summary>"12,4 MB de 80 MB · 5,2 MB/s · 13 s restantes"</summary>
    public static string Progress(ProgressUpdate p)
    {
        var parts = new List<string>
        {
            p.Total is { } total ? $"{Bytes(p.Downloaded)} de {Bytes(total)}" : Bytes(p.Downloaded),
        };
        if (p.Speed is > 0)
            parts.Add(Bytes(p.Speed.Value) + "/s");
        if (p.Eta is > 0)
            parts.Add(Eta(p.Eta.Value) + " restantes");
        return string.Join(" · ", parts);
    }
}
