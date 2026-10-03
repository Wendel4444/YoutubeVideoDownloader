namespace YoutubeDownloader.App.Core;

public enum MediaKind
{
    Video,
    Audio,
}

public enum AppTheme
{
    System,
    Dark,
    Light,
}

/// <summary>Formato de saída que o usuário escolhe; o resto (codecs, conversões) é decidido em <see cref="YtDlpArguments"/>.</summary>
public sealed record OutputFormat(string Id, string Name, MediaKind Kind, string Hint, bool SupportsThumbnail, bool HasQuality)
{
    public override string ToString() => Name;
}

public static class OutputFormats
{
    public static readonly OutputFormat Mp4 = new("mp4", "MP4", MediaKind.Video,
        "Toca em qualquer aparelho. Até 1080p usa H.264; acima disso o YouTube só oferece VP9/AV1.", true, true);
    public static readonly OutputFormat Mkv = new("mkv", "MKV", MediaKind.Video,
        "Melhor qualidade disponível, sem conversão. Ideal para assistir no PC ou editar.", true, true);
    public static readonly OutputFormat WebM = new("webm", "WebM", MediaKind.Video,
        "Formato aberto (VP9/AV1 + Opus), sem conversão.", false, true);
    public static readonly OutputFormat Mp3 = new("mp3", "MP3", MediaKind.Audio,
        "Compatível com qualquer player, carro ou celular.", true, true);
    public static readonly OutputFormat M4a = new("m4a", "M4A (AAC)", MediaKind.Audio,
        "AAC direto do YouTube, sem perda por conversão. Ótimo para iPhone e Apple Music.", true, true);
    public static readonly OutputFormat Opus = new("opus", "Opus", MediaKind.Audio,
        "Áudio original do YouTube, sem conversão e com o menor arquivo.", true, false);
    public static readonly OutputFormat Flac = new("flac", "FLAC", MediaKind.Audio,
        "Sem perdas a partir do original (não aumenta a qualidade da fonte).", true, false);
    public static readonly OutputFormat Wav = new("wav", "WAV", MediaKind.Audio,
        "Sem compressão, para edição de áudio e vídeo.", false, false);

    public static IReadOnlyList<OutputFormat> All { get; } = [Mp4, Mkv, WebM, Mp3, M4a, Opus, Flac, Wav];

    public static IReadOnlyList<OutputFormat> ForKind(MediaKind kind) => All.Where(f => f.Kind == kind).ToList();

    public static OutputFormat Find(string? id) => All.FirstOrDefault(f => f.Id == id) ?? Mp4;
}

public sealed record QualityOption(int? MaxHeight, string Name)
{
    public override string ToString() => Name;
}

public sealed record AudioQualityOption(string Value, string Name)
{
    public override string ToString() => Name;
}

public static class Qualities
{
    public static readonly int[] StandardHeights = [2160, 1440, 1080, 720, 480, 360];

    public static IReadOnlyList<AudioQualityOption> Audio { get; } =
    [
        new("0", "Melhor (VBR)"),
        new("320K", "320 kbps"),
        new("256K", "256 kbps"),
        new("192K", "192 kbps"),
        new("128K", "128 kbps"),
    ];

    /// <summary>
    /// Qualidades oferecidas para um vídeo. <paramref name="resolutions"/> é a menor dimensão de cada formato
    /// (é como o yt-dlp mede "res"; vale também para vídeos verticais). Vazio = desconhecido (ex.: playlist).
    /// </summary>
    public static IReadOnlyList<QualityOption> For(IReadOnlyCollection<int> resolutions)
    {
        var best = resolutions.Count > 0 ? resolutions.Max() : 0;
        var options = new List<QualityOption> { new(null, best > 0 ? $"Melhor disponível ({best}p)" : "Melhor disponível") };
        // Tolerância para resoluções "quase padrão" (ex.: 1072p conta como 1080p).
        options.AddRange(StandardHeights
            .Where(h => best == 0 || best >= h * 0.95)
            .Select(h => new QualityOption(h, HeightName(h))));
        return options;
    }

    public static string HeightName(int height) => height switch
    {
        2160 => "2160p (4K)",
        1440 => "1440p (2K)",
        _ => $"{height}p",
    };

    public static string AudioName(string value) =>
        Audio.FirstOrDefault(a => a.Value == value)?.Name ?? value;
}
