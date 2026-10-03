using System.Globalization;

namespace YoutubeDownloader.App.Core;

/// <summary>Uma linha de progresso de download emitida pelo template do yt-dlp.</summary>
public sealed record ProgressUpdate(string Status, long Downloaded, long? Total, double? Speed, double? Eta, bool IsAudioOnlyStream)
{
    public double? Fraction => Total is > 0 ? Math.Clamp(Downloaded / (double)Total.Value, 0, 1) : null;
}

public static class ProgressParser
{
    public static bool TryParse(string line, out ProgressUpdate update)
    {
        update = null!;
        if (!line.StartsWith(YtDlpArguments.ProgressPrefix, StringComparison.Ordinal))
            return false;

        var parts = line[YtDlpArguments.ProgressPrefix.Length..].Split('|');
        if (parts.Length < 7)
            return false;

        var downloaded = Number(parts[1]) ?? 0;
        var total = Number(parts[2]) ?? Number(parts[3]);
        update = new ProgressUpdate(
            parts[0],
            (long)downloaded,
            total is > 0 ? (long)total.Value : null,
            Number(parts[4]),
            Number(parts[5]),
            parts[6] == "none");
        return true;
    }

    /// <summary>Etapa de pós-processamento ("[pp]started|Merger") em texto para a interface.</summary>
    public static bool TryParseStage(string line, out string stage)
    {
        stage = "";
        if (!line.StartsWith(YtDlpArguments.PostprocessPrefix, StringComparison.Ordinal))
            return false;

        var parts = line[YtDlpArguments.PostprocessPrefix.Length..].Split('|');
        if (parts.Length < 2 || parts[0] != "started")
            return false;

        stage = StageName(parts[1]);
        return true;
    }

    public static string StageName(string postprocessor) => postprocessor switch
    {
        "Merger" => "Juntando vídeo e áudio…",
        "ExtractAudio" => "Convertendo áudio…",
        "EmbedThumbnail" => "Adicionando capa…",
        "ThumbnailsConvertor" => "Preparando capa…",
        "Metadata" or "FFmpegMetadata" => "Gravando informações…",
        "EmbedSubtitle" or "FFmpegEmbedSubtitle" => "Adicionando legendas…",
        "VideoRemuxer" or "FFmpegVideoRemuxer" => "Ajustando o arquivo…",
        "MoveFiles" or "MoveFilesAfterDownload" => "Salvando…",
        _ => "Finalizando…",
    };

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;
}
