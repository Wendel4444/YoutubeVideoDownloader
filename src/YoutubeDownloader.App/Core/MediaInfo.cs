using System.Text.Json;

namespace YoutubeDownloader.App.Core;

public sealed record PlaylistEntry(string Id, string Title, string Url, TimeSpan? Duration, string? ThumbnailUrl, bool IsContainer);

/// <summary>Resultado da análise de um link: um vídeo ou uma playlist/canal com suas entradas.</summary>
public sealed record MediaInfo(
    string Id,
    string Title,
    string? Uploader,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    string Url,
    string Site,
    bool IsLive,
    bool IsPlaylist,
    IReadOnlyList<int> Resolutions,
    IReadOnlyList<PlaylistEntry> Entries)
{
    public bool HasVideo => IsPlaylist || Resolutions.Count > 0;
}

/// <summary>Lê o JSON de "yt-dlp --dump-single-json --flat-playlist".</summary>
public static class MediaInfoParser
{
    // Entradas que o YouTube mantém na playlist mas não podem ser baixadas.
    private static readonly string[] Unavailable = ["[Private video]", "[Deleted video]", "[Unavailable video]"];

    public static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var id = Str(root, "id") ?? "";
        var site = Str(root, "extractor_key") ?? Str(root, "ie_key") ?? "";
        var isPlaylist = Str(root, "_type") == "playlist" || root.TryGetProperty("entries", out _);
        var entries = isPlaylist ? ParseEntries(root) : [];

        return new MediaInfo(
            id,
            Str(root, "title") ?? Str(root, "fulltitle") ?? id,
            Str(root, "uploader") ?? Str(root, "channel") ?? Str(root, "playlist_uploader"),
            Seconds(root, "duration"),
            isPlaylist ? entries.FirstOrDefault(e => e.ThumbnailUrl is not null)?.ThumbnailUrl ?? Thumbnail(root, id, site)
                       : Thumbnail(root, id, site),
            Str(root, "webpage_url") ?? Str(root, "original_url") ?? "",
            site,
            root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True,
            isPlaylist,
            isPlaylist ? [] : Resolutions(root),
            entries);
    }

    private static List<PlaylistEntry> ParseEntries(JsonElement root)
    {
        var list = new List<PlaylistEntry>();
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var e in entries.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                continue;

            var id = Str(e, "id") ?? "";
            var title = Str(e, "title") ?? id;
            if (Unavailable.Contains(title))
                continue;

            var ieKey = Str(e, "ie_key") ?? Str(e, "extractor_key") ?? "";
            var url = Str(e, "url") ?? Str(e, "webpage_url") ?? "";
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (ieKey == "Youtube" && id.Length > 0)
                    url = $"https://www.youtube.com/watch?v={id}";
                else if (Str(e, "webpage_url") is { } page)
                    url = page;
            }

            if (url.Length == 0)
                continue;

            var isContainer = ieKey == "YoutubeTab" || Str(e, "_type") == "playlist";
            list.Add(new PlaylistEntry(id, title, url, Seconds(e, "duration"), Thumbnail(e, id, ieKey), isContainer));
        }

        return list;
    }

    /// <summary>Menor dimensão de cada formato com vídeo (é o "res" do yt-dlp), sem repetições, maior primeiro.</summary>
    private static List<int> Resolutions(JsonElement root)
    {
        var set = new HashSet<int>();
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in formats.EnumerateArray())
            {
                if (Str(f, "vcodec") is "none" || f.ValueKind != JsonValueKind.Object)
                    continue;
                var w = Int(f, "width");
                var h = Int(f, "height");
                var res = w > 0 && h > 0 ? Math.Min(w, h) : h;
                if (res > 0)
                    set.Add(res);
            }
        }
        else if (Int(root, "height") is var single and > 0)
        {
            set.Add(Math.Min(single, Int(root, "width") is var w and > 0 ? w : single));
        }

        return set.OrderDescending().ToList();
    }

    /// <summary>
    /// O WPF não decodifica WebP sem codec extra; para o YouTube a miniatura JPG tem endereço fixo,
    /// nos outros sites é escolhida a maior que não seja WebP.
    /// </summary>
    private static string? Thumbnail(JsonElement e, string id, string site)
    {
        if (site == "Youtube" && id.Length > 0)
            return $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";

        if (e.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
        {
            var best = thumbs.EnumerateArray()
                .Select(t => Str(t, "url"))
                .Where(u => u is not null && !u.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                .LastOrDefault();
            if (best is not null)
                return best;
        }

        var single = Str(e, "thumbnail");
        return single is not null && !single.Contains(".webp", StringComparison.OrdinalIgnoreCase) ? single : null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (int)d : 0;

    private static TimeSpan? Seconds(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d > 0
            ? TimeSpan.FromSeconds(d)
            : null;
}
