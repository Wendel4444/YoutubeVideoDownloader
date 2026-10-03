using YoutubeDownloader.App.Core;
using YoutubeDownloader.App.ViewModels;

namespace YoutubeDownloader.Tests;

public class YtDlpArgumentsTests
{
    private static readonly ToolPaths Tools = new(@"C:\t\yt-dlp.exe", @"C:\ffmpeg\bin", new JsRuntime("node", @"C:\node\node.exe"), null);

    private static DownloadRequest Request(OutputFormat format, int? height = 1080, string audio = "0", bool subs = false) =>
        new("https://www.youtube.com/watch?v=abc", format, height, audio, subs, "pt.*", @"D:\Videos", "%(title)s.%(ext)s");

    private static string After(List<string> args, string flag) => args[args.IndexOf(flag) + 1];

    [Fact]
    public void Mp4_prefers_resolution_then_h264_and_merges_to_mp4()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.Mp4), Tools, @"C:\tmp\1");

        Assert.Equal("bv*+ba/b", After(args, "-f"));
        Assert.Equal("res:1080,vcodec:h264,acodec:aac", After(args, "-S"));
        Assert.Equal("mp4", After(args, "--merge-output-format"));
        Assert.Contains("--embed-thumbnail", args);
        Assert.Equal("node:C:\\node\\node.exe", After(args, "--js-runtimes"));
        Assert.Equal(@"C:\ffmpeg\bin", After(args, "--ffmpeg-location"));
    }

    [Fact]
    public void Url_always_comes_last_after_double_dash()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.Mp3), Tools, @"C:\tmp\1");

        Assert.Equal("--", args[^2]);
        Assert.Equal("https://www.youtube.com/watch?v=abc", args[^1]);
    }

    [Fact]
    public void Best_quality_has_no_resolution_limit()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.Mkv, height: null), Tools, @"C:\tmp\1");

        Assert.Equal("res", After(args, "-S"));
        Assert.Equal("mkv", After(args, "--merge-output-format"));
    }

    [Fact]
    public void Webm_only_picks_webm_streams_and_skips_thumbnail()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.WebM), Tools, @"C:\tmp\1");

        Assert.Equal("bv*[ext=webm]+ba[ext=webm]/b[ext=webm]", After(args, "-f"));
        Assert.DoesNotContain("--embed-thumbnail", args);
    }

    [Fact]
    public void Mp3_extracts_audio_with_chosen_bitrate()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.Mp3, audio: "320K"), Tools, @"C:\tmp\1");

        Assert.Contains("-x", args);
        Assert.Equal("mp3", After(args, "--audio-format"));
        Assert.Equal("320K", After(args, "--audio-quality"));
        Assert.DoesNotContain("-S", args);
    }

    [Fact]
    public void Lossless_audio_ignores_bitrate()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.Flac, audio: "128K"), Tools, @"C:\tmp\1");

        Assert.DoesNotContain("--audio-quality", args);
    }

    [Fact]
    public void M4a_prefers_native_aac_stream()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.M4a), Tools, @"C:\tmp\1");

        Assert.Equal("ba[ext=m4a]/ba/b", After(args, "-f"));
    }

    [Fact]
    public void Subtitles_only_when_requested()
    {
        var without = YtDlpArguments.Download(Request(OutputFormats.Mp4), Tools, @"C:\tmp\1");
        var with = YtDlpArguments.Download(Request(OutputFormats.Mp4, subs: true), Tools, @"C:\tmp\1");

        Assert.DoesNotContain("--embed-subs", without);
        Assert.Equal("pt.*", After(with, "--sub-langs"));
    }

    [Fact]
    public void Cookies_browser_is_passed_when_set()
    {
        var args = YtDlpArguments.Probe("https://x.com/v", Tools with { CookiesBrowser = "firefox" });

        Assert.Equal("firefox", After(args, "--cookies-from-browser"));
        Assert.Contains("--flat-playlist", args);
    }

    [Fact]
    public void Temp_folder_is_separate_from_destination()
    {
        var args = YtDlpArguments.Download(Request(OutputFormats.Mp4), Tools, @"C:\tmp\job");

        Assert.Contains(@"temp:C:\tmp\job", args);
        Assert.Contains(@"D:\Videos", args);
    }
}

public class ProgressParserTests
{
    [Fact]
    public void Parses_progress_line()
    {
        Assert.True(ProgressParser.TryParse("[dl]downloading|1024|4096|NA|2048.5|3|avc1.4d400b", out var p));

        Assert.Equal(1024, p.Downloaded);
        Assert.Equal(4096, p.Total);
        Assert.Equal(0.25, p.Fraction);
        Assert.Equal(2048.5, p.Speed);
        Assert.False(p.IsAudioOnlyStream);
    }

    [Fact]
    public void Uses_estimate_when_total_is_unknown_and_flags_audio()
    {
        Assert.True(ProgressParser.TryParse("[dl]downloading|500|NA|1000.0|NA|NA|none", out var p));

        Assert.Equal(1000, p.Total);
        Assert.Null(p.Speed);
        Assert.True(p.IsAudioOnlyStream);
    }

    [Fact]
    public void Ignores_other_lines()
    {
        Assert.False(ProgressParser.TryParse("[youtube] abc: Downloading webpage", out _));
        Assert.False(ProgressParser.TryParse("[dl]broken", out _));
    }

    [Fact]
    public void Translates_postprocessor_stage()
    {
        Assert.True(ProgressParser.TryParseStage("[pp]started|Merger", out var stage));
        Assert.Equal("Juntando vídeo e áudio…", stage);
        Assert.False(ProgressParser.TryParseStage("[pp]finished|Merger", out _));
    }
}

public class ErrorTranslatorTests
{
    [Theory]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you’re not a bot. Use --cookies-from-browser", "anti-robô")]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm your age. This video may be inappropriate for some users.", "idade")]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access to this video", "privado")]
    [InlineData("ERROR: unable to download video data: HTTP Error 403: Forbidden", "403")]
    [InlineData("ERROR: [youtube] xxxxxxxxxxx: This video is unavailable", "indisponível")]
    [InlineData("ERROR: [generic] Unsupported URL: https://example.com/", "não é suportado")]
    [InlineData("ERROR: [youtube] abc: Requested format is not available. Use --list-formats", "formato")]
    public void Known_errors_become_actionable_messages(string line, string expected)
    {
        Assert.Contains(expected, ErrorTranslator.Translate(["WARNING: algo", line]));
    }

    [Fact]
    public void Unknown_error_is_cleaned()
    {
        Assert.Equal("Something odd happened", ErrorTranslator.Translate(["ERROR: [vimeo] 123: Something odd happened"]));
    }

    [Fact]
    public void Warning_alone_does_not_hide_real_error()
    {
        var message = ErrorTranslator.Translate(
        [
            "WARNING: [youtube] No supported JavaScript runtime could be found. some formats may be missing",
            "ERROR: [youtube] abc: Private video",
        ]);

        Assert.Equal("Este vídeo é privado.", message);
    }
}

public class MediaInfoParserTests
{
    [Fact]
    public void Parses_video_with_resolutions_by_smallest_side()
    {
        const string json = """
        {"_type":"video","id":"abc","title":"Vídeo","uploader":"Canal","duration":125.0,"extractor_key":"Youtube",
         "webpage_url":"https://www.youtube.com/watch?v=abc","is_live":false,
         "formats":[
           {"format_id":"140","vcodec":"none","acodec":"mp4a"},
           {"format_id":"137","vcodec":"avc1","width":1920,"height":1080},
           {"format_id":"short","vcodec":"vp9","width":720,"height":1280},
           {"format_id":"18","vcodec":"avc1","width":640,"height":360}
         ]}
        """;

        var info = MediaInfoParser.Parse(json);

        Assert.False(info.IsPlaylist);
        Assert.Equal("Vídeo", info.Title);
        Assert.Equal(TimeSpan.FromSeconds(125), info.Duration);
        Assert.Equal([1080, 720, 360], info.Resolutions);
        Assert.Equal("https://i.ytimg.com/vi/abc/mqdefault.jpg", info.ThumbnailUrl);
    }

    [Fact]
    public void Parses_playlist_skipping_private_entries()
    {
        const string json = """
        {"_type":"playlist","id":"PL1","title":"Lista","extractor_key":"YoutubeTab","entries":[
          {"_type":"url","ie_key":"Youtube","id":"a1","url":"https://www.youtube.com/watch?v=a1","title":"Um","duration":60},
          {"_type":"url","ie_key":"Youtube","id":"a2","url":"a2","title":"Dois"},
          {"_type":"url","ie_key":"Youtube","id":"a3","url":"https://www.youtube.com/watch?v=a3","title":"[Private video]"}
        ]}
        """;

        var info = MediaInfoParser.Parse(json);

        Assert.True(info.IsPlaylist);
        Assert.Equal(2, info.Entries.Count);
        Assert.Equal("https://www.youtube.com/watch?v=a2", info.Entries[1].Url);
        Assert.False(info.Entries[0].IsContainer);
    }

    [Fact]
    public void Channel_tabs_are_containers()
    {
        const string json = """
        {"_type":"playlist","id":"UC1","title":"Canal","entries":[
          {"_type":"url","ie_key":"YoutubeTab","url":"https://www.youtube.com/@canal/videos","title":"Canal - Videos"}
        ]}
        """;

        Assert.True(MediaInfoParser.Parse(json).Entries[0].IsContainer);
    }

    [Fact]
    public void Webp_thumbnails_are_skipped_for_other_sites()
    {
        const string json = """
        {"id":"1","title":"x","extractor_key":"Vimeo","thumbnails":[{"url":"https://a/1.jpg"},{"url":"https://a/2.webp"}]}
        """;

        Assert.Equal("https://a/1.jpg", MediaInfoParser.Parse(json).ThumbnailUrl);
    }
}

public class QualitiesTests
{
    [Fact]
    public void Offers_only_resolutions_the_video_has()
    {
        var options = Qualities.For([1080, 720, 360]);

        Assert.Null(options[0].MaxHeight);
        Assert.Equal("Melhor disponível (1080p)", options[0].Name);
        Assert.DoesNotContain(options, o => o.MaxHeight == 1440);
        Assert.Contains(options, o => o.MaxHeight == 480);
    }

    [Fact]
    public void Unknown_resolutions_offer_everything()
    {
        Assert.Equal(1 + Qualities.StandardHeights.Length, Qualities.For([]).Count);
    }
}

public class UrlTests
{
    [Theory]
    [InlineData("https://youtu.be/abc", "https://youtu.be/abc")]
    [InlineData("  youtube.com/watch?v=abc  ", "https://youtube.com/watch?v=abc")]
    [InlineData("olha esse https://www.youtube.com/watch?v=abc&t=10s", "https://www.youtube.com/watch?v=abc&t=10s")]
    public void Normalizes_pasted_links(string input, string expected)
    {
        Assert.Equal(expected, DownloadsViewModel.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é link")]
    [InlineData("file:///C:/x.mp4")]
    public void Rejects_non_links(string input)
    {
        Assert.Null(DownloadsViewModel.NormalizeUrl(input));
    }
}

public class PixPayloadTests
{
    [Fact]
    public void Matches_known_code_from_cinema_os()
    {
        Assert.Equal(
            "00020126410014br.gov.bcb.pix0119delsanvfx@gmail.com5204000053039865802BR5920CINEMA PRODUCTION OS6009SAO PAULO62070503***6304AC78",
            PixPayload.Build("delsanvfx@gmail.com", "CINEMA PRODUCTION OS", "SAO PAULO"));
    }

    [Fact]
    public void Donation_code_uses_the_email_key()
    {
        Assert.Contains("0119delsanvfx@gmail.com", PixPayload.Donation);
        Assert.Contains("5918YOUTUBE DOWNLOADER", PixPayload.Donation);
        Assert.Matches("6304[0-9A-F]{4}$", PixPayload.Donation);
    }

    [Fact]
    public void Donation_code_matches_independent_calculation()
    {
        // Calculado fora do app (Python, mesmo padrão BR Code) e conferido lendo o QR gerado.
        Assert.Equal(
            "00020126410014br.gov.bcb.pix0119delsanvfx@gmail.com5204000053039865802BR5918YOUTUBE DOWNLOADER6009SAO PAULO62070503***6304FC64",
            PixPayload.Donation);
    }
}
