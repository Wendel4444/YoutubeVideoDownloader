using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using YoutubeDownloader.App.Core;

namespace YoutubeDownloader.App.Services;

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = Path.Combine(AppPaths.DownloadsFolder(), "YouTube Downloader");
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int MaxParallel { get; set; } = 2;

    // Últimas escolhas na tela de download.
    public string LastFormat { get; set; } = "mp4";
    public int? LastMaxHeight { get; set; } = 1080;
    public string LastAudioQuality { get; set; } = "0";

    public bool EmbedSubtitles { get; set; }
    public string SubtitleLanguages { get; set; } = "pt.*,en.*";
    public string? CookiesBrowser { get; set; }
    public string FileNameTemplate { get; set; } = FileNameTemplates.Title;

    public bool AutoUpdateYtDlp { get; set; } = true;
    public DateTime? LastYtDlpUpdateCheck { get; set; }

    /// <summary>Pasta do FFmpeg escolhida à mão; vazia = detectar sozinho.</summary>
    public string? FfmpegFolder { get; set; }
}

public static class FileNameTemplates
{
    public const string Title = "%(title)s.%(ext)s";
    public const string TitleWithId = "%(title)s [%(id)s].%(ext)s";
    public const string ChannelAndTitle = "%(uploader)s - %(title)s.%(ext)s";
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _file;

    private SettingsStore(string file, AppSettings current)
    {
        _file = file;
        Current = current;
    }

    public AppSettings Current { get; }

    public static SettingsStore Load(AppPaths paths)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(paths.SettingsFile))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Configurações ilegíveis; usando o padrão");
        }

        settings ??= new AppSettings();
        settings.MaxParallel = Math.Clamp(settings.MaxParallel, 1, 4);
        return new SettingsStore(paths.SettingsFile, settings);
    }

    public void Save()
    {
        try
        {
            // Grava num temporário e troca: um desligamento no meio não corrompe o arquivo.
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Não foi possível salvar as configurações");
        }
    }
}
