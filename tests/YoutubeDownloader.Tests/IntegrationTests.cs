using System.IO;
using System.Net.Http;
using YoutubeDownloader.App.Core;
using YoutubeDownloader.App.Services;

namespace YoutubeDownloader.Tests;

/// <summary>
/// Downloads reais pelo mesmo caminho do app (yt-dlp + FFmpeg + rede). Só rodam com YTD_INTEGRATION=1
/// e usam uma pasta de dados temporária, nunca a do usuário. O yt-dlp é copiado de YTD_YTDLP, se informado.
/// </summary>
public sealed class IntegrationTests : IAsyncLifetime
{
    private const string ShortVideo = "https://www.youtube.com/watch?v=jNQXAC9IVRw"; // "Me at the zoo", 19 s

    private static readonly bool Enabled = Environment.GetEnvironmentVariable("YTD_INTEGRATION") == "1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytd-tests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private AppPaths _paths = null!;
    private ToolsService _tools = null!;
    private YtDlpClient _client = null!;

    public async Task InitializeAsync()
    {
        if (!Enabled) return;

        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        var settings = SettingsStore.Load(_paths);
        _tools = new ToolsService(_paths, settings, _http);

        if (Environment.GetEnvironmentVariable("YTD_YTDLP") is { } local && File.Exists(local))
            File.Copy(local, _paths.YtDlpExe);
        else
            await _tools.InstallYtDlpAsync(null, CancellationToken.None);

        var status = await _tools.RefreshAsync();
        Assert.True(status.IsReady, "yt-dlp e FFmpeg precisam estar disponíveis");
        _client = new YtDlpClient(_tools, _paths);
    }

    public Task DisposeAsync()
    {
        _http.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private DownloadRequest Request(OutputFormat format, int? height = 360, string audio = "0") =>
        new(ShortVideo, format, height, audio, false, "pt.*", Path.Combine(_root, "out", format.Id), FileNameTemplates.Title);

    [Fact]
    public async Task Probe_video()
    {
        if (!Enabled) return;

        var info = await _client.ProbeAsync(ShortVideo, CancellationToken.None);

        Assert.Equal("Me at the zoo", info.Title);
        Assert.False(info.IsPlaylist);
        Assert.Contains(240, info.Resolutions);
    }

    [Fact]
    public async Task Probe_playlist()
    {
        if (!Enabled) return;

        var info = await _client.ProbeAsync("https://www.youtube.com/playlist?list=PLFgquLnL59alCl_2TQvOiD5Vgm1hCaGSI", CancellationToken.None);

        Assert.True(info.IsPlaylist);
        Assert.True(info.Entries.Count > 10);
        Assert.All(info.Entries, e => Assert.StartsWith("https://", e.Url));
    }

    [Fact]
    public async Task Probe_channel_uses_videos_tab()
    {
        if (!Enabled) return;

        var info = await _client.ProbeAsync("https://www.youtube.com/@jawed", CancellationToken.None);

        Assert.True(info.IsPlaylist);
        Assert.Contains(info.Entries, e => e.Id == "jNQXAC9IVRw");
    }

    [Fact]
    public async Task Invalid_video_gives_friendly_error()
    {
        if (!Enabled) return;

        var ex = await Assert.ThrowsAsync<DownloadException>(() =>
            _client.ProbeAsync("https://www.youtube.com/watch?v=xxxxxxxxxxx", CancellationToken.None));
        Assert.Contains("indisponível", ex.Message);
    }

    [Theory]
    [InlineData("mp4")]
    [InlineData("mkv")]
    [InlineData("webm")]
    [InlineData("mp3")]
    [InlineData("m4a")]
    [InlineData("opus")]
    [InlineData("flac")]
    [InlineData("wav")]
    public async Task Downloads_each_format(string formatId)
    {
        if (!Enabled) return;

        var progress = 0;
        var stages = new List<string>();
        var file = await _client.DownloadAsync(Request(OutputFormats.Find(formatId)),
            _ => Interlocked.Increment(ref progress), s => { lock (stages) stages.Add(s); }, CancellationToken.None);

        Assert.NotNull(file);
        Assert.True(File.Exists(file), $"arquivo não existe: {file}");
        Assert.Equal("." + formatId, Path.GetExtension(file));
        Assert.True(new FileInfo(file).Length > 10_000);
        Assert.True(progress > 0);
        Assert.Empty(Directory.GetDirectories(_paths.Temp));
    }

    [Fact]
    public async Task Cancel_stops_and_cleans_temp()
    {
        if (!Enabled) return;

        using var cts = new CancellationTokenSource();
        var request = Request(OutputFormats.Mkv, height: null) with { Url = "https://www.youtube.com/watch?v=aqz-KE-bpKQ" };
        var task = _client.DownloadAsync(request, p => { if (p.Downloaded > 1_000_000) cts.Cancel(); }, _ => { }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(Directory.GetDirectories(_paths.Temp));
        Assert.Empty(Directory.Exists(request.OutputFolder) ? Directory.GetFiles(request.OutputFolder) : []);
    }
}
