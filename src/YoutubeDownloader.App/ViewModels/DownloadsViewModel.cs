using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using YoutubeDownloader.App.Core;
using YoutubeDownloader.App.Services;

namespace YoutubeDownloader.App.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly YtDlpClient _client;
    private readonly SettingsStore _settings;
    private CancellationTokenSource? _probe;

    public DownloadsViewModel(YtDlpClient client, SettingsStore settings)
    {
        _client = client;
        _settings = settings;

        var s = settings.Current;
        var format = OutputFormats.Find(s.LastFormat);
        _isAudio = format.Kind == MediaKind.Audio;
        _formats = OutputFormats.ForKind(format.Kind);
        _selectedFormat = format;
        _qualities = Core.Qualities.For([]);
        _selectedQuality = _qualities.FirstOrDefault(q => q.MaxHeight == s.LastMaxHeight) ?? _qualities[0];
        _selectedAudioQuality = Core.Qualities.Audio.FirstOrDefault(a => a.Value == s.LastAudioQuality) ?? Core.Qualities.Audio[0];
        _embedSubtitles = s.EmbedSubtitles;

        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];
    public IReadOnlyList<AudioQualityOption> AudioQualities => Core.Qualities.Audio;
    public string OutputFolder => _settings.Current.OutputFolder;

    /// <summary>Avisa a janela principal (toast).</summary>
    public event Action<string>? Notify;

    // ---------- Análise do link ----------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private string _url = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private bool _isProbing;

    [ObservableProperty]
    private string? _probeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia), nameof(MediaSubtitle), nameof(DownloadButtonText), nameof(ShowQuality), nameof(ShowSubtitles))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private MediaInfo? _media;

    public bool HasMedia => Media is not null;

    public string MediaSubtitle => Media switch
    {
        null => "",
        { IsPlaylist: true } m => Join(m.Uploader, $"Playlist · {m.Entries.Count} {(m.Entries.Count == 1 ? "vídeo" : "vídeos")}"),
        var m => Join(m.Uploader, Formatting.Duration(m.Duration), m.Site is "Youtube" or "" ? null : m.Site),
    };

    public string DownloadButtonText => Media is { IsPlaylist: true } m ? $"Baixar {m.Entries.Count} {(m.Entries.Count == 1 ? "item" : "itens")}" : "Baixar";

    // ---------- Opções ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuality), nameof(ShowAudioQuality), nameof(ShowSubtitles))]
    private bool _isAudio;

    [ObservableProperty]
    private IReadOnlyList<OutputFormat> _formats;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAudioQuality), nameof(FormatHint))]
    private OutputFormat _selectedFormat;

    [ObservableProperty]
    private IReadOnlyList<QualityOption> _qualities;

    [ObservableProperty]
    private QualityOption _selectedQuality;

    [ObservableProperty]
    private AudioQualityOption _selectedAudioQuality;

    [ObservableProperty]
    private bool _embedSubtitles;

    public bool ShowQuality => !IsAudio;
    public bool ShowAudioQuality => IsAudio && SelectedFormat.HasQuality;
    public bool ShowSubtitles => !IsAudio;
    public string FormatHint => SelectedFormat.Hint;

    partial void OnIsAudioChanged(bool value)
    {
        var kind = value ? MediaKind.Audio : MediaKind.Video;
        Formats = OutputFormats.ForKind(kind);
        var last = OutputFormats.Find(_settings.Current.LastFormat);
        SelectedFormat = last.Kind == kind ? last : Formats[0];
    }

    partial void OnSelectedFormatChanged(OutputFormat value)
    {
        // A ComboBox zera a seleção ao trocar a lista de formatos.
        if (value is null)
            SelectedFormat = Formats[0];
    }

    partial void OnSelectedQualityChanged(QualityOption value)
    {
        if (value is null)
            SelectedQuality = Qualities[0];
    }

    partial void OnSelectedAudioQualityChanged(AudioQualityOption value)
    {
        if (value is null)
            SelectedAudioQuality = Core.Qualities.Audio[0];
    }

    // ---------- Fila ----------

    public string QueueSummary
    {
        get
        {
            var running = Items.Count(i => i.State == DownloadState.Running);
            var queued = Items.Count(i => i.State == DownloadState.Queued);
            var done = Items.Count(i => i.State == DownloadState.Completed);
            var failed = Items.Count(i => i.State == DownloadState.Failed);
            if (Items.Count == 0)
                return "Cole um link do YouTube ou de outro site para começar.";
            var parts = new List<string>();
            if (running > 0) parts.Add($"{running} baixando");
            if (queued > 0) parts.Add($"{queued} na fila");
            if (done > 0) parts.Add($"{done} {(done == 1 ? "concluído" : "concluídos")}");
            if (failed > 0) parts.Add($"{failed} com erro");
            return parts.Count > 0 ? string.Join(" · ", parts) : "Nada em andamento.";
        }
    }

    public bool HasFinished => Items.Any(i => !i.CanCancel);
    public bool HasFailed => Items.Any(i => i.State == DownloadState.Failed);

    // ---------- Comandos ----------

    private bool CanAnalyze() => !IsProbing && !string.IsNullOrWhiteSpace(Url);

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        var url = NormalizeUrl(Url);
        if (url is null)
        {
            ProbeError = "Isso não parece um link. Copie o endereço do vídeo (começa com https://) e cole aqui.";
            return;
        }

        Url = url;
        _probe?.Cancel();
        _probe = new CancellationTokenSource();
        var ct = _probe.Token;

        IsProbing = true;
        ProbeError = null;
        Media = null;
        try
        {
            var media = await _client.ProbeAsync(url, ct);
            if (media.IsLive)
            {
                ProbeError = "Transmissões ao vivo não podem ser baixadas enquanto estão no ar. Tente de novo quando terminar.";
                return;
            }

            if (media.IsPlaylist && media.Entries.Count == 0)
            {
                ProbeError = "A playlist está vazia ou todos os vídeos são privados.";
                return;
            }

            Qualities = Core.Qualities.For(media.Resolutions);
            SelectedQuality = Qualities.FirstOrDefault(q => q.MaxHeight == _settings.Current.LastMaxHeight)
                ?? Qualities.LastOrDefault(q => q.MaxHeight is null || q.MaxHeight <= (_settings.Current.LastMaxHeight ?? int.MaxValue))
                ?? Qualities[0];

            // Link de áudio puro (ex.: SoundCloud): já abre na aba Áudio.
            if (!media.HasVideo)
                IsAudio = true;

            Media = media;
        }
        catch (OperationCanceledException)
        {
        }
        catch (DownloadException ex)
        {
            ProbeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao analisar {Url}", url);
            ProbeError = "Erro inesperado ao analisar o link: " + ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsProbing = false;
        }
    }

    /// <summary>Cola da área de transferência e já analisa.</summary>
    [RelayCommand]
    private async Task PasteAsync()
    {
        var text = Shell.ClipboardText();
        if (string.IsNullOrWhiteSpace(text))
        {
            Notify?.Invoke("A área de transferência não tem um link.");
            return;
        }

        await AcceptUrlAsync(text);
    }

    public async Task AcceptUrlAsync(string text)
    {
        Url = text.Trim();
        if (AnalyzeCommand.CanExecute(null))
            await AnalyzeCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ClearMedia()
    {
        _probe?.Cancel();
        IsProbing = false;
        Media = null;
        ProbeError = null;
        Url = "";
    }

    private bool CanDownload() => Media is not null;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download()
    {
        if (Media is not { } media)
            return;

        var s = _settings.Current;
        s.LastFormat = SelectedFormat.Id;
        if (!IsAudio)
            s.LastMaxHeight = SelectedQuality.MaxHeight;
        else
            s.LastAudioQuality = SelectedAudioQuality.Value;
        s.EmbedSubtitles = EmbedSubtitles;
        _settings.Save();

        if (media.IsPlaylist)
        {
            foreach (var entry in media.Entries)
                Items.Add(new DownloadItemViewModel(BuildRequest(entry.Url), entry.Title, entry.ThumbnailUrl));
            Notify?.Invoke($"{media.Entries.Count} itens adicionados à fila.");
        }
        else
        {
            Items.Add(new DownloadItemViewModel(BuildRequest(media.Url.Length > 0 ? media.Url : Url), media.Title, media.ThumbnailUrl));
        }

        Media = null;
        Url = "";
        Pump();
    }

    private DownloadRequest BuildRequest(string url)
    {
        var s = _settings.Current;
        return new DownloadRequest(
            url,
            SelectedFormat,
            IsAudio ? null : SelectedQuality.MaxHeight,
            SelectedAudioQuality.Value,
            !IsAudio && EmbedSubtitles,
            s.SubtitleLanguages,
            s.OutputFolder,
            s.FileNameTemplate);
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        if (Shell.PickFolder(OutputFolder, "Pasta onde salvar os downloads") is { } folder)
        {
            _settings.Current.OutputFolder = folder;
            _settings.Save();
            RefreshOutputFolder();
        }
    }

    public void RefreshOutputFolder() => OnPropertyChanged(nameof(OutputFolder));

    [RelayCommand]
    private void OpenFolder() => Shell.OpenFolder(OutputFolder);

    [RelayCommand]
    private void CancelItem(DownloadItemViewModel item)
    {
        if (item.State == DownloadState.Queued)
            item.Cancel();
        else
            item.Cancellation?.Cancel();
    }

    [RelayCommand]
    private void RetryItem(DownloadItemViewModel item)
    {
        item.Requeue();
        Pump();
    }

    [RelayCommand]
    private void RemoveItem(DownloadItemViewModel item)
    {
        item.Cancellation?.Cancel();
        Items.Remove(item);
    }

    [RelayCommand]
    private void OpenItem(DownloadItemViewModel item)
    {
        if (item.FilePath is { } file)
            Shell.OpenFile(file);
    }

    [RelayCommand]
    private void RevealItem(DownloadItemViewModel item)
    {
        if (item.FilePath is { } file)
            Shell.Reveal(file);
        else
            Shell.OpenFolder(item.Request.OutputFolder);
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var item in Items.Where(i => !i.CanCancel).ToList())
            Items.Remove(item);
    }

    [RelayCommand]
    private void RetryFailed()
    {
        foreach (var item in Items.Where(i => i.State == DownloadState.Failed))
            item.Requeue();
        Pump();
    }

    public void CancelAll()
    {
        foreach (var item in Items.Where(i => i.CanCancel).ToList())
            CancelItem(item);
    }

    /// <summary>Inicia downloads da fila até o limite de simultâneos.</summary>
    public void Pump()
    {
        var running = Items.Count(i => i.State == DownloadState.Running);
        foreach (var item in Items.Where(i => i.State == DownloadState.Queued).ToList())
        {
            if (running >= _settings.Current.MaxParallel)
                break;
            running++;
            _ = RunAsync(item);
        }
    }

    private async Task RunAsync(DownloadItemViewModel item)
    {
        using var cts = new CancellationTokenSource();
        item.Cancellation = cts;
        item.Start();
        var dispatcher = Application.Current.Dispatcher;
        try
        {
            var file = await _client.DownloadAsync(
                item.Request,
                p => dispatcher.InvokeAsync(() => { if (item.IsActive) item.Apply(p); }),
                s => dispatcher.InvokeAsync(() => { if (item.IsActive) item.SetStage(s); }),
                cts.Token);
            item.Complete(file);
        }
        catch (OperationCanceledException)
        {
            item.Cancel();
        }
        catch (DownloadException ex)
        {
            item.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro inesperado no download de {Url}", item.Request.Url);
            item.Fail("Erro inesperado: " + ex.Message);
        }
        finally
        {
            item.Cancellation = null;
            Pump();
            if (!Items.Any(i => i.CanCancel) && Items.Any(i => i.IsCompleted))
                Notify?.Invoke("Downloads concluídos.");
        }
    }

    // ---------- Apoio ----------

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (DownloadItemViewModel item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;
        if (e.OldItems is not null)
            foreach (DownloadItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;
        RefreshSummary();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItemViewModel.State))
            RefreshSummary();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(HasFailed));
    }

    /// <summary>Aceita link com espaços, sem "https://" ou colado com texto em volta.</summary>
    public static string? NormalizeUrl(string text)
    {
        var candidate = text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.Contains("://") || t.Contains('.'));
        if (candidate is null)
            return null;
        if (!candidate.Contains("://"))
            candidate = "https://" + candidate;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.')
            ? candidate
            : null;
    }

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
