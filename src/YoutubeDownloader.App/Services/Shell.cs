using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Serilog;

namespace YoutubeDownloader.App.Services;

/// <summary>Interações com o Windows: abrir pastas e arquivos, escolher pasta, área de transferência.</summary>
public static class Shell
{
    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Start("explorer.exe", $"\"{folder}\"");
    }

    public static void Reveal(string file)
    {
        if (File.Exists(file))
            Start("explorer.exe", $"/select,\"{file}\"");
        else if (Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder))
            OpenFolder(folder);
    }

    public static void OpenFile(string file)
    {
        if (File.Exists(file))
            Start(file, null, useShell: true);
    }

    public static string? PickFolder(string? initial, string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            dialog.InitialDirectory = initial;
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public static string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Outro programa está segurando a área de transferência.
            return null;
        }
    }

    public static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    private static void Start(string file, string? args, bool useShell = false)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = useShell };
            if (args is not null)
                psi.Arguments = args;
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning(ex, "Não foi possível abrir {File}", file);
        }
    }
}
