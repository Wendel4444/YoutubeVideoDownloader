using System.IO;
using System.Runtime.InteropServices;

namespace YoutubeDownloader.App.Services;

/// <summary>
/// Dados do app em %LocalAppData%\YoutubeDownloader (fora do OneDrive): ferramentas, temporários, logs e configurações.
/// </summary>
public sealed class AppPaths(string root)
{
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeDownloader"));

    public string Root { get; } = root;
    public string Tools => Path.Combine(Root, "tools");
    public string Temp => Path.Combine(Root, "temp");
    public string Logs => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string YtDlpExe => Path.Combine(Tools, "yt-dlp.exe");
    public string FfmpegFolder => Path.Combine(Tools, "ffmpeg");
    public string DenoExe => Path.Combine(Tools, "deno", "deno.exe");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Tools);
        Directory.CreateDirectory(Temp);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>Restos de downloads interrompidos (app fechado no meio) são apagados ao abrir.</summary>
    public void CleanTemp()
    {
        foreach (var dir in Directory.EnumerateDirectories(Temp))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Pasta Downloads do usuário (pode ter sido movida para outro disco).</summary>
    public static string DownloadsFolder()
    {
        var folderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(folderId, 0, 0, out var path) == 0)
        {
            try { return Marshal.PtrToStringUni(path) ?? Fallback(); }
            finally { Marshal.FreeCoTaskMem(path); }
        }

        return Fallback();

        static string Fallback() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);
}
