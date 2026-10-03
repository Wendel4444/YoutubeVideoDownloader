using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using YoutubeDownloader.App.Core;

namespace YoutubeDownloader.App.ViewModels;

public enum DownloadState
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled,
}

/// <summary>Uma linha da fila de downloads.</summary>
public partial class DownloadItemViewModel(DownloadRequest request, string title, string? thumbnailUrl) : ObservableObject
{
    private readonly Stopwatch _sinceLastUpdate = Stopwatch.StartNew();

    public DownloadRequest Request { get; } = request;
    public string Title { get; } = title;
    public string? ThumbnailUrl { get; } = thumbnailUrl;
    public MediaKind Kind => Request.Format.Kind;
    public string KindText => Kind == MediaKind.Video ? "VÍDEO" : "ÁUDIO";

    public string FormatText => Kind == MediaKind.Video
        ? $"{Request.Format.Name} · {(Request.MaxHeight is int h ? Qualities.HeightName(h) : "melhor qualidade")}"
        : Request.Format.HasQuality
            ? $"{Request.Format.Name} · {Qualities.AudioName(Request.AudioQuality)}"
            : Request.Format.Name;

    public CancellationTokenSource? Cancellation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanCancel), nameof(CanRetry), nameof(IsActive), nameof(IsCompleted))]
    private DownloadState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private double _percent;

    [ObservableProperty]
    private string _detail = "Aguardando na fila";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string? _filePath;

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);

    public bool IsActive => State == DownloadState.Running;
    public bool IsCompleted => State == DownloadState.Completed;
    public bool CanCancel => State is DownloadState.Queued or DownloadState.Running;
    public bool CanRetry => State is DownloadState.Failed or DownloadState.Canceled;

    public string StateText => State switch
    {
        DownloadState.Queued => "Na fila",
        DownloadState.Running => $"{Percent:0}%",
        DownloadState.Completed => "Concluído",
        DownloadState.Failed => "Falhou",
        _ => "Cancelado",
    };

    public void Start()
    {
        State = DownloadState.Running;
        Percent = 0;
        Error = null;
        Detail = "Conectando…";
    }

    /// <summary>
    /// Vídeo com áudio separado chega em duas etapas (vídeo, depois áudio): a barra mostra o total,
    /// com o vídeo ocupando 90% e o áudio o resto.
    /// </summary>
    public void Apply(ProgressUpdate update)
    {
        var finished = update.Status == "finished";
        if (!finished && _sinceLastUpdate.ElapsedMilliseconds < 150)
            return;
        _sinceLastUpdate.Restart();

        if (update.Fraction is { } fraction)
        {
            var overall = Kind == MediaKind.Video
                ? update.IsAudioOnlyStream ? 0.9 + fraction * 0.1 : fraction * 0.9
                : fraction;
            // Nunca volta: formato único sem áudio separado pode terminar em 90% e saltar para 100% no fim.
            Percent = Math.Max(Percent, overall * 100);
        }

        var phase = Kind == MediaKind.Video && update.IsAudioOnlyStream ? "Áudio: " : "";
        Detail = finished ? "Processando…" : phase + Formatting.Progress(update);
    }

    public void SetStage(string stage) => Detail = stage;

    public void Complete(string? file)
    {
        FilePath = file;
        Percent = 100;
        Detail = file is not null && File.Exists(file) ? "Salvo · " + Formatting.Bytes(new FileInfo(file).Length) : "Salvo na pasta de destino";
        State = DownloadState.Completed;
    }

    public void Fail(string message)
    {
        Error = message;
        Detail = "";
        State = DownloadState.Failed;
    }

    public void Cancel()
    {
        Detail = "";
        State = DownloadState.Canceled;
    }

    public void Requeue()
    {
        Error = null;
        Percent = 0;
        Detail = "Aguardando na fila";
        State = DownloadState.Queued;
    }
}
