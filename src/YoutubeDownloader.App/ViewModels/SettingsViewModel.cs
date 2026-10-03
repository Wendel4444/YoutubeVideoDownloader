using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using YoutubeDownloader.App.Core;
using YoutubeDownloader.App.Services;

namespace YoutubeDownloader.App.ViewModels;

public sealed record Choice<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly ToolsService _tools;
    private readonly AppPaths _paths;
    private readonly DownloadsViewModel _downloads;
    private readonly AppTheme _startupTheme;

    public SettingsViewModel(SettingsStore settings, ToolsService tools, AppPaths paths, DownloadsViewModel downloads)
    {
        _settings = settings;
        _tools = tools;
        _paths = paths;
        _downloads = downloads;

        var s = settings.Current;
        _startupTheme = s.Theme;
        _selectedTheme = Themes.First(t => t.Value == s.Theme);
        _selectedCookies = CookieBrowsers.FirstOrDefault(c => c.Value == (s.CookiesBrowser ?? "")) ?? CookieBrowsers[0];
        _selectedFileName = FileNames.FirstOrDefault(f => f.Value == s.FileNameTemplate) ?? FileNames[0];
        _maxParallel = s.MaxParallel;
        _subtitleLanguages = s.SubtitleLanguages;
        _autoUpdate = s.AutoUpdateYtDlp;

        tools.StatusChanged += (_, _) => RefreshTools();
    }

    public event Action<string>? Notify;

    public IReadOnlyList<Choice<AppTheme>> Themes { get; } =
    [
        new(AppTheme.Dark, "Escuro"),
        new(AppTheme.Light, "Claro"),
        new(AppTheme.System, "Igual ao Windows"),
    ];

    public IReadOnlyList<Choice<string>> CookieBrowsers { get; } =
    [
        new("", "Não usar"),
        new("firefox", "Firefox"),
        new("chrome", "Google Chrome"),
        new("edge", "Microsoft Edge"),
        new("brave", "Brave"),
        new("opera", "Opera"),
        new("vivaldi", "Vivaldi"),
    ];

    public IReadOnlyList<Choice<string>> FileNames { get; } =
    [
        new(FileNameTemplates.Title, "Título do vídeo"),
        new(FileNameTemplates.ChannelAndTitle, "Canal - Título"),
        new(FileNameTemplates.TitleWithId, "Título [ID do vídeo]"),
    ];

    public IReadOnlyList<int> ParallelOptions { get; } = [1, 2, 3, 4];

    public string OutputFolder => _settings.Current.OutputFolder;
    public string DataFolder => _paths.Root;

    /// <summary>A pasta de destino também pode ser trocada na tela de download.</summary>
    public void OnShown() => OnPropertyChanged(nameof(OutputFolder));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeNotice))]
    private Choice<AppTheme> _selectedTheme;

    public string? ThemeNotice => SelectedTheme.Value != _startupTheme ? "O novo tema é aplicado ao reabrir o app." : null;

    [ObservableProperty]
    private Choice<string> _selectedCookies;

    [ObservableProperty]
    private Choice<string> _selectedFileName;

    [ObservableProperty]
    private int _maxParallel;

    [ObservableProperty]
    private string _subtitleLanguages;

    [ObservableProperty]
    private bool _autoUpdate;

    partial void OnSelectedThemeChanged(Choice<AppTheme> value) => Save(s => s.Theme = value.Value);
    partial void OnSelectedCookiesChanged(Choice<string> value) => Save(s => s.CookiesBrowser = value.Value.Length > 0 ? value.Value : null);
    partial void OnSelectedFileNameChanged(Choice<string> value) => Save(s => s.FileNameTemplate = value.Value);
    partial void OnAutoUpdateChanged(bool value) => Save(s => s.AutoUpdateYtDlp = value);

    partial void OnMaxParallelChanged(int value)
    {
        Save(s => s.MaxParallel = Math.Clamp(value, 1, 4));
        _downloads.Pump();
    }

    partial void OnSubtitleLanguagesChanged(string value) =>
        Save(s => s.SubtitleLanguages = string.IsNullOrWhiteSpace(value) ? "pt.*,en.*" : value.Trim());

    private void Save(Action<AppSettings> change)
    {
        change(_settings.Current);
        _settings.Save();
    }

    // ---------- Ferramentas ----------

    public string YtDlpText => _tools.Status.YtDlpVersion is { } v ? $"Versão {v}" : "Não instalado";
    public string FfmpegText => _tools.Status.HasFfmpeg ? $"{_tools.Status.FfmpegVersion} · {_tools.Status.FfmpegFolder}" : "Não encontrado";
    public string JsText => _tools.Status.JsRuntime is { } js
        ? $"{(js.Name == "deno" ? "Deno" : "Node.js")} {_tools.Status.JsVersion} · {js.Path}"
        : "Não encontrado: alguns formatos do YouTube podem faltar";
    public bool HasYtDlp => _tools.Status.HasYtDlp;
    public bool HasFfmpeg => _tools.Status.HasFfmpeg;
    public bool HasJs => _tools.Status.HasJs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand), nameof(InstallFfmpegCommand), nameof(InstallDenoCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _busyText;

    [ObservableProperty]
    private double _busyProgress;

    private void RefreshTools()
    {
        OnPropertyChanged(nameof(YtDlpText));
        OnPropertyChanged(nameof(FfmpegText));
        OnPropertyChanged(nameof(JsText));
        OnPropertyChanged(nameof(HasYtDlp));
        OnPropertyChanged(nameof(HasFfmpeg));
        OnPropertyChanged(nameof(HasJs));
    }

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task UpdateYtDlpAsync() => RunBusyAsync("Atualizando o yt-dlp…", async _ =>
    {
        var (_, message) = await _tools.UpdateYtDlpAsync(CancellationToken.None);
        Notify?.Invoke(message);
    });

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task InstallFfmpegAsync() => RunBusyAsync("Baixando o FFmpeg (cerca de 150 MB)…", async progress =>
    {
        await _tools.InstallFfmpegAsync(progress, CancellationToken.None);
        await _tools.RefreshAsync();
        Notify?.Invoke("FFmpeg instalado.");
    });

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task InstallDenoAsync() => RunBusyAsync("Baixando o Deno…", async progress =>
    {
        await _tools.InstallDenoAsync(progress, CancellationToken.None);
        await _tools.RefreshAsync();
        Notify?.Invoke("Deno instalado: todos os formatos do YouTube liberados.");
    });

    [RelayCommand]
    private async Task ChooseFfmpegFolderAsync()
    {
        if (Shell.PickFolder(_settings.Current.FfmpegFolder, "Pasta que contém ffmpeg.exe e ffprobe.exe") is not { } folder)
            return;

        if (!File.Exists(Path.Combine(folder, "ffmpeg.exe")) || !File.Exists(Path.Combine(folder, "ffprobe.exe")))
        {
            Notify?.Invoke("Essa pasta não tem ffmpeg.exe e ffprobe.exe (geralmente ficam na pasta \"bin\").");
            return;
        }

        Save(s => s.FfmpegFolder = folder);
        await _tools.RefreshAsync();
    }

    [RelayCommand]
    private void ChooseOutputFolder()
    {
        if (Shell.PickFolder(OutputFolder, "Pasta onde salvar os downloads") is not { } folder)
            return;
        Save(s => s.OutputFolder = folder);
        OnPropertyChanged(nameof(OutputFolder));
        _downloads.RefreshOutputFolder();
    }

    [RelayCommand]
    private void OpenLogs() => Shell.OpenFolder(_paths.Logs);

    // ---------- Apoio e licença ----------

    public string PixKey => PixPayload.Key;
    public string PixCode => PixPayload.Donation;
    public System.Windows.Media.ImageSource PixQr => _pixQr ??= PixQrCode.Image(PixPayload.Donation);
    private System.Windows.Media.ImageSource? _pixQr;

    public string AboutText { get; } =
        $"Versão {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3)} · Copyright (c) 2026 Wendel4444";

    [RelayCommand]
    private void CopyPixCode() =>
        Notify?.Invoke(Shell.CopyText(PixPayload.Donation)
            ? "Código Pix copiado. Cole no app do seu banco em “Pix copia e cola”."
            : "Não foi possível copiar agora. Tente de novo.");

    [RelayCommand]
    private void CopyPixKey() =>
        Notify?.Invoke(Shell.CopyText(PixPayload.Key) ? "Chave Pix copiada." : "Não foi possível copiar agora. Tente de novo.");

    [RelayCommand]
    private void OpenLicense() => Shell.OpenFile(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"));

    private async Task RunBusyAsync(string text, Func<IProgress<double>, Task> work)
    {
        IsBusy = true;
        BusyText = text;
        BusyProgress = 0;
        try
        {
            await work(new Progress<double>(p => BusyProgress = p * 100));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha em: {Task}", text);
            Notify?.Invoke("Não deu certo: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }
}
