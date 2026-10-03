using System.Diagnostics;
using System.IO;
using System.Text;

namespace YoutubeDownloader.App.Services;

public sealed record ProcessResult(int ExitCode, string Output, IReadOnlyList<string> Errors);

/// <summary>Roda um executável sem janela, lendo stdout/stderr linha a linha em UTF-8.</summary>
public static class ProcessRunner
{
    private const int MaxErrorLines = 200;

    /// <param name="onLine">Recebe cada linha das duas saídas, numa thread de fundo.</param>
    public static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = new StringBuilder();
        var errors = new Queue<string>();

        var outTask = PumpAsync(process.StandardOutput, line =>
        {
            lock (output) output.AppendLine(line);
            onLine?.Invoke(line);
        });
        var errTask = PumpAsync(process.StandardError, line =>
        {
            lock (errors)
            {
                errors.Enqueue(line);
                if (errors.Count > MaxErrorLines) errors.Dequeue();
            }
            onLine?.Invoke(line);
        });

        await using (ct.Register(() => Kill(process)))
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outTask, errTask);
        }

        ct.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output.ToString(), errors.ToList());
    }

    /// <summary>Primeira linha da saída (ex.: "--version"), ou null se não rodar.</summary>
    public static async Task<string?> FirstLineAsync(string exe, params string[] args)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await RunAsync(exe, args, null, timeout.Token);
            return result.ExitCode == 0
                ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
            onLine(line);
    }

    private static void Kill(Process process)
    {
        try
        {
            // A árvore inteira: o yt-dlp abre o FFmpeg como processo filho.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
