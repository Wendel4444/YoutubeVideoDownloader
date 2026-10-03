using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using Serilog;
using YoutubeDownloader.App.Core;

namespace YoutubeDownloader.App.Services;

public sealed record ToolStatus(
    string? YtDlpVersion,
    string? FfmpegFolder,
    string? FfmpegVersion,
    JsRuntime? JsRuntime,
    string? JsVersion)
{
    public static ToolStatus Unknown { get; } = new(null, null, null, null, null);

    public bool HasYtDlp => YtDlpVersion is not null;
    public bool HasFfmpeg => FfmpegFolder is not null;
    public bool HasJs => JsRuntime is not null;
    public bool IsReady => HasYtDlp && HasFfmpeg;
}

/// <summary>
/// Encontra, instala e atualiza as ferramentas externas: yt-dlp (motor de download), FFmpeg (junta e converte)
/// e um runtime JavaScript (Deno ou Node), que o yt-dlp usa para liberar todos os formatos do YouTube.
/// </summary>
public sealed partial class ToolsService(AppPaths paths, SettingsStore settings, HttpClient http)
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    private const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    public ToolStatus Status { get; private set; } = ToolStatus.Unknown;

    public event EventHandler? StatusChanged;

    public async Task<ToolStatus> RefreshAsync()
    {
        var ytDlp = File.Exists(paths.YtDlpExe) ? await ProcessRunner.FirstLineAsync(paths.YtDlpExe, "--version") : null;

        var ffmpegFolder = FindFfmpeg();
        var ffmpegVersion = ffmpegFolder is null
            ? null
            : ShortFfmpegVersion(await ProcessRunner.FirstLineAsync(Path.Combine(ffmpegFolder, "ffmpeg.exe"), "-version"));

        JsRuntime? js = null;
        string? jsVersion = null;
        foreach (var candidate in FindJsRuntimes())
        {
            jsVersion = await ProcessRunner.FirstLineAsync(candidate.Path, "--version");
            if (jsVersion is not null)
            {
                js = candidate;
                break;
            }
        }

        Status = new ToolStatus(ytDlp, ffmpegFolder, ffmpegVersion, js, jsVersion);
        Log.Information("Ferramentas: yt-dlp {YtDlp}, FFmpeg {Ffmpeg} em {Folder}, JS {Js} {JsVersion}",
            ytDlp ?? "ausente", ffmpegVersion ?? "ausente", ffmpegFolder, js?.Name ?? "ausente", jsVersion);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return Status;
    }

    /// <summary>Caminhos para montar o comando; lança se o essencial faltar.</summary>
    public ToolPaths RequirePaths()
    {
        if (!Status.HasYtDlp)
            throw new DownloadException("O yt-dlp ainda não está instalado. Abra Configurações e instale as ferramentas.");
        if (!Status.HasFfmpeg)
            throw new DownloadException("O FFmpeg não foi encontrado. Abra Configurações e instale o FFmpeg.");
        return new ToolPaths(paths.YtDlpExe, Status.FfmpegFolder, Status.JsRuntime, settings.Current.CookiesBrowser);
    }

    public async Task InstallYtDlpAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Log.Information("Baixando yt-dlp");
        await DownloadFileAsync(YtDlpUrl, paths.YtDlpExe, progress, ct);
    }

    /// <summary>Atualiza o yt-dlp pelo próprio "-U". Devolve uma frase para mostrar ao usuário.</summary>
    public async Task<(bool Updated, string Message)> UpdateYtDlpAsync(CancellationToken ct)
    {
        if (!File.Exists(paths.YtDlpExe))
        {
            await InstallYtDlpAsync(null, ct);
            await RefreshAsync();
            return (true, $"yt-dlp instalado ({Status.YtDlpVersion}).");
        }

        var before = Status.YtDlpVersion;
        var result = await ProcessRunner.RunAsync(paths.YtDlpExe, ["--ignore-config", "--no-colors", "-U"], null, ct);
        settings.Current.LastYtDlpUpdateCheck = DateTime.Now;
        settings.Save();

        if (result.ExitCode != 0)
        {
            Log.Warning("yt-dlp -U falhou: {Errors}", string.Join(" | ", result.Errors));
            return (false, "Não foi possível atualizar o yt-dlp. " + ErrorTranslator.Translate(result.Errors));
        }

        await RefreshAsync();
        var updated = before != Status.YtDlpVersion;
        Log.Information("yt-dlp -U: {Before} → {After}", before, Status.YtDlpVersion);
        return (updated, updated
            ? $"yt-dlp atualizado para {Status.YtDlpVersion}."
            : $"O yt-dlp já está na versão mais recente ({Status.YtDlpVersion}).");
    }

    public async Task InstallFfmpegAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Log.Information("Baixando FFmpeg");
        var zip = Path.Combine(paths.Temp, "ffmpeg.zip");
        try
        {
            await DownloadFileAsync(FfmpegUrl, zip, progress, ct);
            Directory.CreateDirectory(paths.FfmpegFolder);
            using var archive = ZipFile.OpenRead(zip);
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/" + name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"{name} não está no pacote baixado.");
                entry.ExtractToFile(Path.Combine(paths.FfmpegFolder, name), overwrite: true);
            }
        }
        finally
        {
            TryDelete(zip);
        }
    }

    public async Task InstallDenoAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Log.Information("Baixando Deno");
        var zip = Path.Combine(paths.Temp, "deno.zip");
        try
        {
            await DownloadFileAsync(DenoUrl, zip, progress, ct);
            var folder = Path.GetDirectoryName(paths.DenoExe)!;
            Directory.CreateDirectory(folder);
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("deno.exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("deno.exe não está no pacote baixado.");
            entry.ExtractToFile(paths.DenoExe, overwrite: true);
        }
        finally
        {
            TryDelete(zip);
        }
    }

    private string? FindFfmpeg()
    {
        foreach (var folder in FfmpegCandidates())
        {
            if (File.Exists(Path.Combine(folder, "ffmpeg.exe")) && File.Exists(Path.Combine(folder, "ffprobe.exe")))
                return folder;
        }

        return null;
    }

    private IEnumerable<string> FfmpegCandidates()
    {
        if (!string.IsNullOrWhiteSpace(settings.Current.FfmpegFolder))
            yield return settings.Current.FfmpegFolder;

        yield return paths.FfmpegFolder;

        foreach (var dir in PathFolders())
            yield return dir;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links");

        // Instalação pelo winget (ex.: Gyan.FFmpeg) nem sempre entra no PATH de quem abriu o app.
        var winget = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(winget))
        {
            foreach (var package in SafeDirectories(winget, "*FFmpeg*"))
            {
                foreach (var exe in SafeFiles(package, "ffmpeg.exe"))
                    yield return Path.GetDirectoryName(exe)!;
            }
        }

        yield return @"C:\ffmpeg\bin";
        yield return @"C:\ProgramData\chocolatey\bin";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims");
    }

    private IEnumerable<JsRuntime> FindJsRuntimes()
    {
        if (File.Exists(paths.DenoExe))
            yield return new JsRuntime("deno", paths.DenoExe);

        foreach (var dir in PathFolders())
        {
            var deno = Path.Combine(dir, "deno.exe");
            if (File.Exists(deno))
                yield return new JsRuntime("deno", deno);
        }

        foreach (var dir in PathFolders().Append(Path.Combine(
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs")))
        {
            var node = Path.Combine(dir, "node.exe");
            if (File.Exists(node))
                yield return new JsRuntime("node", node);
        }
    }

    /// <summary>PATH do usuário e da máquina lidos do registro: o do processo pode estar desatualizado.</summary>
    private static IEnumerable<string> PathFolders()
    {
        var all = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        };
        return all
            .SelectMany(p => (p ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => Environment.ExpandEnvironmentVariables(p.Trim('"')))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".download";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total is > 0)
                        progress?.Report(read / (double)total.Value);
                }
            }

            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static string? ShortFfmpegVersion(string? line)
    {
        if (line is null)
            return null;
        var match = FfmpegVersionRegex().Match(line);
        return match.Success ? match.Groups[1].Value : "instalado";
    }

    [GeneratedRegex(@"ffmpeg version (\S+)")]
    private static partial Regex FfmpegVersionRegex();

    private static IEnumerable<string> SafeDirectories(string root, string pattern)
    {
        try { return Directory.GetDirectories(root, pattern); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeFiles(string root, string name)
    {
        try { return Directory.GetFiles(root, name, SearchOption.AllDirectories); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
