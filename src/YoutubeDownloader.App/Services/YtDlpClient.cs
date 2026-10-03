using System.IO;
using System.Text.Json;
using Serilog;
using YoutubeDownloader.App.Core;

namespace YoutubeDownloader.App.Services;

/// <summary>Erro com mensagem já pronta para o usuário; os detalhes técnicos vão para o log.</summary>
public sealed class DownloadException(string message, IReadOnlyList<string>? details = null) : Exception(message)
{
    public IReadOnlyList<string> Details { get; } = details ?? [];
}

public sealed class YtDlpClient(ToolsService tools, AppPaths paths)
{
    public async Task<MediaInfo> ProbeAsync(string url, CancellationToken ct)
    {
        var info = await ProbeOnceAsync(url, ct);

        // Link de canal: o YouTube devolve as abas (Vídeos, Shorts, Ao vivo). Usa a primeira, que é "Vídeos".
        if (info.IsPlaylist && info.Entries.Count > 0 && info.Entries.All(e => e.IsContainer))
            info = await ProbeOnceAsync(info.Entries[0].Url, ct);

        return info;
    }

    private async Task<MediaInfo> ProbeOnceAsync(string url, CancellationToken ct)
    {
        var tool = tools.RequirePaths();
        var result = await ProcessRunner.RunAsync(tool.YtDlp, YtDlpArguments.Probe(url, tool), null, ct);
        LogWarnings(result.Errors);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            Log.Warning("Análise falhou para {Url}: {Errors}", url, string.Join(" | ", result.Errors.TakeLast(10)));
            throw new DownloadException(ErrorTranslator.Translate(result.Errors), result.Errors);
        }

        try
        {
            return MediaInfoParser.Parse(result.Output);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "JSON inesperado do yt-dlp para {Url}", url);
            throw new DownloadException("Resposta inesperada do yt-dlp. Atualize o yt-dlp em Configurações e tente de novo.");
        }
    }

    /// <summary>Baixa e devolve o caminho final do arquivo. Callbacks chegam numa thread de fundo.</summary>
    public async Task<string?> DownloadAsync(
        DownloadRequest request, Action<ProgressUpdate> onProgress, Action<string> onStage, CancellationToken ct)
    {
        var tool = tools.RequirePaths();
        Directory.CreateDirectory(request.OutputFolder);

        // Partes e arquivos intermediários ficam numa pasta própria do job: cancelar não deixa lixo no destino.
        var temp = Path.Combine(paths.Temp, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string? file = null;
        try
        {
            var args = YtDlpArguments.Download(request, tool, temp);
            Log.Information("Download {Url} como {Format} {Height}", request.Url, request.Format.Id, request.MaxHeight);

            var result = await ProcessRunner.RunAsync(tool.YtDlp, args, line =>
            {
                if (ProgressParser.TryParse(line, out var progress))
                    onProgress(progress);
                else if (ProgressParser.TryParseStage(line, out var stage))
                    onStage(stage);
                else if (line.StartsWith(YtDlpArguments.FilePrefix, StringComparison.Ordinal))
                    file = line[YtDlpArguments.FilePrefix.Length..].Trim();
            }, ct);

            LogWarnings(result.Errors);
            if (result.ExitCode != 0)
            {
                Log.Warning("Download falhou ({Code}) para {Url}: {Errors}",
                    result.ExitCode, request.Url, string.Join(" | ", result.Errors.TakeLast(15)));
                throw new DownloadException(ErrorTranslator.Translate(result.Errors), result.Errors);
            }

            Log.Information("Concluído: {File}", file);
            return file;
        }
        finally
        {
            await DeleteTempAsync(temp);
        }
    }

    private static async Task DeleteTempAsync(string folder)
    {
        // Depois de um cancelamento o FFmpeg pode levar um instante para soltar os arquivos.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(400);
            }
        }
    }

    private static void LogWarnings(IEnumerable<string> lines)
    {
        foreach (var line in lines.Where(l => l.StartsWith("WARNING:", StringComparison.Ordinal)).Distinct().Take(5))
            Log.Warning("yt-dlp: {Line}", line);
    }
}
