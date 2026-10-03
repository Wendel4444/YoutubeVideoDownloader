namespace YoutubeDownloader.App.Core;

/// <summary>O que baixar e como salvar.</summary>
public sealed record DownloadRequest(
    string Url,
    OutputFormat Format,
    int? MaxHeight,
    string AudioQuality,
    bool EmbedSubtitles,
    string SubtitleLanguages,
    string OutputFolder,
    string FileNameTemplate);

/// <summary>Runtime JavaScript que o yt-dlp usa para resolver os desafios do YouTube (sem ele, formatos somem).</summary>
public sealed record JsRuntime(string Name, string Path);

public sealed record ToolPaths(string YtDlp, string? FfmpegDirectory, JsRuntime? JsRuntime, string? CookiesBrowser);

/// <summary>Monta a linha de comando do yt-dlp. Puro: sem IO, testável.</summary>
public static class YtDlpArguments
{
    public const string ProgressPrefix = "[dl]";
    public const string PostprocessPrefix = "[pp]";
    public const string FilePrefix = "[file]";

    private const string ProgressTemplate =
        "download:" + ProgressPrefix +
        "%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s" +
        "|%(progress.speed)s|%(progress.eta)s|%(info.vcodec)s";

    private const string PostprocessTemplate =
        "postprocess:" + PostprocessPrefix + "%(progress.status)s|%(progress.postprocessor)s";

    public static List<string> Probe(string url, ToolPaths tools) =>
    [
        .. Common(tools),
        "--dump-single-json",
        "--flat-playlist",
        "--",
        url,
    ];

    public static List<string> Download(DownloadRequest request, ToolPaths tools, string tempFolder)
    {
        var args = new List<string>(Common(tools))
        {
            "--newline",
            "--progress",
            "--progress-template", ProgressTemplate,
            "--progress-template", PostprocessTemplate,
            "--print", "after_move:" + FilePrefix + "%(filepath)s",
            "--windows-filenames",
            "--no-mtime",
            "--retries", "10",
            "--fragment-retries", "10",
            "--concurrent-fragments", "4",
            "-P", request.OutputFolder,
            "-P", "temp:" + tempFolder,
            "-o", request.FileNameTemplate,
            "--embed-metadata",
        };

        var format = request.Format;
        if (format.Kind == MediaKind.Video)
            AddVideo(args, request);
        else
            AddAudio(args, request);

        if (format.SupportsThumbnail)
            args.AddRange(["--embed-thumbnail", "--convert-thumbnails", "jpg"]);

        args.Add("--");
        args.Add(request.Url);
        return args;
    }

    private static IEnumerable<string> Common(ToolPaths tools)
    {
        // Ignora qualquer yt-dlp.conf do usuário: o app precisa saber exatamente o que vai sair.
        yield return "--ignore-config";
        yield return "--no-colors";
        yield return "--encoding";
        yield return "utf-8";
        // Link de vídeo dentro de playlist baixa só o vídeo; links de playlist continuam funcionando.
        yield return "--no-playlist";

        if (!string.IsNullOrWhiteSpace(tools.FfmpegDirectory))
        {
            yield return "--ffmpeg-location";
            yield return tools.FfmpegDirectory;
        }

        if (tools.JsRuntime is { } js)
        {
            yield return "--js-runtimes";
            yield return $"{js.Name}:{js.Path}";
        }

        if (!string.IsNullOrWhiteSpace(tools.CookiesBrowser))
        {
            yield return "--cookies-from-browser";
            yield return tools.CookiesBrowser;
        }
    }

    private static void AddVideo(List<string> args, DownloadRequest request)
    {
        var res = request.MaxHeight is int h ? $"res:{h}" : "res";
        switch (request.Format.Id)
        {
            case "mp4":
                // Resolução primeiro; no empate, H.264 + AAC para tocar em TVs, celulares e no Windows sem codecs extras.
                args.AddRange(["-f", "bv*+ba/b", "-S", $"{res},vcodec:h264,acodec:aac",
                    "--merge-output-format", "mp4", "--remux-video", "mp4"]);
                break;
            case "webm":
                args.AddRange(["-f", "bv*[ext=webm]+ba[ext=webm]/b[ext=webm]", "-S", res,
                    "--merge-output-format", "webm"]);
                break;
            default:
                args.AddRange(["-f", "bv*+ba/b", "-S", res, "--merge-output-format", "mkv", "--remux-video", "mkv"]);
                break;
        }

        args.Add("--embed-chapters");

        if (request.EmbedSubtitles && !string.IsNullOrWhiteSpace(request.SubtitleLanguages))
            args.AddRange(["--write-subs", "--embed-subs", "--sub-langs", request.SubtitleLanguages]);
    }

    private static void AddAudio(List<string> args, DownloadRequest request)
    {
        // Pega na origem o áudio que já está no formato pedido, quando existe: evita converter à toa.
        var selector = request.Format.Id switch
        {
            "m4a" => "ba[ext=m4a]/ba/b",
            "opus" => "ba[acodec^=opus]/ba/b",
            _ => "ba/b",
        };
        args.AddRange(["-f", selector, "-x", "--audio-format", request.Format.Id]);

        if (request.Format.HasQuality)
            args.AddRange(["--audio-quality", string.IsNullOrWhiteSpace(request.AudioQuality) ? "0" : request.AudioQuality]);
    }
}
