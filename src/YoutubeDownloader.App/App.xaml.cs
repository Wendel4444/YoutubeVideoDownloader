using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using YoutubeDownloader.App.Services;
using YoutubeDownloader.App.ViewModels;

namespace YoutubeDownloader.App;

public partial class App : Application
{
    private readonly AppPaths _paths = AppPaths.Default;
    private MainViewModel? _main;
    private HttpClient? _http;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _paths.EnsureCreated();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(_paths.Logs, "app-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Exceção não observada em tarefa");
            args.SetObserved();
        };

        try
        {
            _paths.CleanTemp();
            var settings = SettingsStore.Load(_paths);
            ThemeManager.Apply(this, settings.Current.Theme);

            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YoutubeDownloader/2.0");

            var tools = new ToolsService(_paths, settings, _http);
            var client = new YtDlpClient(tools, _paths);
            var downloads = new DownloadsViewModel(client, settings);
            var settingsPage = new SettingsViewModel(settings, tools, _paths, downloads);
            _main = new MainViewModel(downloads, settingsPage, tools, settings);

            var window = new MainWindow(_main);
            MainWindow = window;
            window.Show();
            Log.Information("Aplicativo iniciado. Dados em {Root}", _paths.Root);

            await _main.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha ao iniciar o aplicativo");
            MessageBox.Show($"Não foi possível iniciar o YouTube Downloader.\n\n{ex.Message}\n\nDetalhes em {_paths.Logs}",
                "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Encerra o yt-dlp/FFmpeg em andamento; as partes ficam na pasta temporária e são limpas na próxima abertura.
        _main?.Downloads.CancelAll();
        _http?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exceção não tratada na interface");
        MessageBox.Show($"Ocorreu um erro inesperado.\n\n{e.Exception.Message}\n\nDetalhes em {_paths.Logs}",
            "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
