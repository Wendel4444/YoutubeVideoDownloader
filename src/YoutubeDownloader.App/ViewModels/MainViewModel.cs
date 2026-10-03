using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using YoutubeDownloader.App.Services;

namespace YoutubeDownloader.App.ViewModels;

public sealed record NavItem(string Name, string Shortcut, object Page)
{
    // Nome lido por leitores de tela e pela automação de interface.
    public override string ToString() => Name;
}

public partial class MainViewModel : ObservableObject
{
    private readonly ToolsService _tools;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _toastTimer;

    public MainViewModel(DownloadsViewModel downloads, SettingsViewModel settingsPage, ToolsService tools, SettingsStore settings)
    {
        Downloads = downloads;
        SettingsPage = settingsPage;
        _tools = tools;
        _settings = settings;

        Navigation = [new NavItem("Baixar", "Ctrl+1", downloads), new NavItem("Configurações", "Ctrl+2", settingsPage)];
        _selectedNav = Navigation[0];
        _currentPage = downloads;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = null;
        };

        downloads.Notify += ShowToast;
        settingsPage.Notify += ShowToast;
        tools.StatusChanged += (_, _) => OnPropertyChanged(nameof(ToolsSummary));
    }

    public DownloadsViewModel Downloads { get; }
    public SettingsViewModel SettingsPage { get; }
    public IReadOnlyList<NavItem> Navigation { get; }

    [ObservableProperty]
    private NavItem _selectedNav;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string? _toast;

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is null)
            return;
        CurrentPage = value.Page;
        if (value.Page == SettingsPage)
            SettingsPage.OnShown();
    }

    public string ToolsSummary
    {
        get
        {
            var s = _tools.Status;
            if (!s.HasYtDlp) return "yt-dlp não instalado";
            return s.HasFfmpeg ? $"yt-dlp {s.YtDlpVersion} · FFmpeg ok" : $"yt-dlp {s.YtDlpVersion} · sem FFmpeg";
        }
    }

    // ---------- Preparação (primeira execução) ----------

    [ObservableProperty]
    private bool _isSetupOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareCommand))]
    private bool _isPreparing;

    [ObservableProperty]
    private string _setupText = "";

    [ObservableProperty]
    private string? _setupStep;

    [ObservableProperty]
    private double _setupProgress;

    [ObservableProperty]
    private string? _setupError;

    public async Task InitializeAsync()
    {
        try
        {
            var status = await _tools.RefreshAsync();
            if (!status.IsReady)
            {
                SetupText = DescribeMissing(status);
                IsSetupOpen = true;
                return;
            }

            if (!status.HasJs)
                ShowToast("Sem Deno ou Node.js, alguns formatos do YouTube podem faltar. Instale o Deno em Configurações.");

            _ = AutoUpdateAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao verificar as ferramentas");
            ShowToast("Não foi possível verificar as ferramentas. Veja Configurações.");
        }
    }

    private static string DescribeMissing(ToolStatus status)
    {
        var items = new List<string>();
        if (!status.HasYtDlp) items.Add("• yt-dlp, o motor de download (≈ 18 MB)");
        if (!status.HasFfmpeg) items.Add("• FFmpeg, que junta e converte vídeo e áudio (≈ 150 MB)");
        if (!status.HasJs) items.Add("• Deno, que libera todos os formatos do YouTube (≈ 40 MB)");
        return "Para baixar, o app precisa de algumas ferramentas gratuitas. Elas ficam só na pasta do app, sem instalar nada no Windows:\n\n"
               + string.Join("\n", items);
    }

    private bool CanPrepare() => !IsPreparing;

    [RelayCommand(CanExecute = nameof(CanPrepare))]
    private async Task PrepareAsync()
    {
        IsPreparing = true;
        SetupError = null;
        var progress = new Progress<double>(p => SetupProgress = p * 100);
        try
        {
            if (!_tools.Status.HasYtDlp)
            {
                SetupStep = "Baixando o yt-dlp…";
                SetupProgress = 0;
                await _tools.InstallYtDlpAsync(progress, CancellationToken.None);
            }

            if (!_tools.Status.HasFfmpeg)
            {
                SetupStep = "Baixando o FFmpeg…";
                SetupProgress = 0;
                await _tools.InstallFfmpegAsync(progress, CancellationToken.None);
            }

            if (!_tools.Status.HasJs)
            {
                SetupStep = "Baixando o Deno…";
                SetupProgress = 0;
                await _tools.InstallDenoAsync(progress, CancellationToken.None);
            }

            SetupStep = "Conferindo…";
            var status = await _tools.RefreshAsync();
            if (status.IsReady)
            {
                IsSetupOpen = false;
                ShowToast("Tudo pronto. Cole um link para começar.");
            }
            else
            {
                SetupError = "Alguma ferramenta não respondeu depois de instalada. Veja os detalhes em Configurações.";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao preparar as ferramentas");
            SetupError = "Não foi possível baixar: " + ex.Message + " Verifique a internet e tente de novo.";
        }
        finally
        {
            IsPreparing = false;
            SetupStep = null;
        }
    }

    [RelayCommand]
    private void SkipSetup()
    {
        IsSetupOpen = false;
        if (!_tools.Status.IsReady)
            SelectedNav = Navigation[1];
    }

    /// <summary>O YouTube muda com frequência; o yt-dlp acompanha. Verifica uma vez por dia, sem atrapalhar.</summary>
    private async Task AutoUpdateAsync()
    {
        var s = _settings.Current;
        if (!s.AutoUpdateYtDlp || s.LastYtDlpUpdateCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(20))
            return;

        try
        {
            var (updated, message) = await _tools.UpdateYtDlpAsync(CancellationToken.None);
            if (updated)
                ShowToast(message);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Verificação automática do yt-dlp falhou");
        }
    }

    public void ShowToast(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
