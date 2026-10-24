using System.Text.Json;

namespace DlpForge.Core.Models;

/// <summary>What yt-dlp -J reports about a link: a single video, or a playlist with entries.</summary>
public sealed class MediaInfo
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Uploader { get; init; }
    public string? Extractor { get; init; }
    public string? Thumbnail { get; init; }
    public double? DurationSeconds { get; init; }
    public bool IsPlaylist { get; init; }
    public IReadOnlyList<FormatInfo> Formats { get; init; } = [];
    public IReadOnlyList<PlaylistEntry> Entries { get; init; } = [];
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];
    public int ChapterCount { get; init; }

    public static MediaInfo Parse(string json, string sourceUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.Str("_type");
        bool isPlaylist = type is "playlist" or "multi_video" || root.TryGetProperty("entries", out _);

        var formats = new List<FormatInfo>();
        if (root.TryGetProperty("formats", out var fmts) && fmts.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fmts.EnumerateArray())
            {
                var fi = FormatInfo.FromJson(f);
                if (fi.FormatId.Length > 0 && !fi.IsStoryboard) formats.Add(fi);
            }
        }

        var entries = new List<PlaylistEntry>();
        if (root.TryGetProperty("entries", out var ents) && ents.ValueKind == JsonValueKind.Array)
        {
            int index = 1;
            foreach (var e in ents.EnumerateArray())
            {
                int current = index++;
                if (e.ValueKind != JsonValueKind.Object) continue;
                var url = e.Str("webpage_url") ?? e.Str("url") ?? "";
                if (url.Length == 0) continue;
                entries.Add(new PlaylistEntry
                {
                    Index = current,
                    Id = e.Str("id") ?? "",
                    Url = url,
                    Title = e.Str("title") ?? url,
                    Uploader = e.Str("uploader") ?? e.Str("channel"),
                    DurationSeconds = e.Num("duration"),
                    Thumbnail = BestThumbnail(e),
                });
            }
        }

        var subs = new List<string>();
        if (root.TryGetProperty("subtitles", out var s) && s.ValueKind == JsonValueKind.Object)
            subs.AddRange(s.EnumerateObject().Select(p => p.Name).Where(n => n != "live_chat"));

        int chapters = root.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Array
            ? ch.GetArrayLength() : 0;

        return new MediaInfo
        {
            Id = root.Str("id") ?? "",
            Url = root.Str("webpage_url") ?? root.Str("original_url") ?? sourceUrl,
            Title = root.Str("title") ?? sourceUrl,
            Uploader = root.Str("uploader") ?? root.Str("channel"),
            Extractor = root.Str("extractor_key") ?? root.Str("extractor"),
            Thumbnail = BestThumbnail(root),
            DurationSeconds = root.Num("duration"),
            IsPlaylist = isPlaylist,
            Formats = formats,
            Entries = entries,
            SubtitleLanguages = subs,
            ChapterCount = chapters,
        };
    }

    internal static string? BestThumbnail(JsonElement e)
    {
        var single = e.Str("thumbnail");
        if (single != null) return single;
        if (e.TryGetProperty("thumbnails", out var t) && t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0)
        {
            // yt-dlp sorts thumbnails worst to best; take the last one with a URL.
            return t.EnumerateArray().Select(x => x.Str("url")).LastOrDefault(u => u != null);
        }
        return null;
    }
}

public sealed class PlaylistEntry
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Uploader { get; init; }
    public double? DurationSeconds { get; init; }
    public string? Thumbnail { get; init; }
}

public sealed class FormatInfo
{
    public string FormatId { get; init; } = "";
    public string Extension { get; init; } = "";
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Fps { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public double? Tbr { get; init; }
    public long? FileSize { get; init; }
    public bool FileSizeIsApprox { get; init; }
    public string? Note { get; init; }

    public bool HasVideo => VideoCodec is not null and not "none";
    public bool HasAudio => AudioCodec is not null and not "none";
    internal bool IsStoryboard => Extension == "mhtml";

    public string Kind => (HasVideo, HasAudio) switch
    {
        (true, true) => "video+audio",
        (true, false) => "video only",
        (false, true) => "audio only",
        _ => "unknown",
    };

    public string Resolution => Height is int h
        ? (Width is int w ? $"{w}x{h}" : $"{h}p")
        : (HasAudio && !HasVideo ? "audio" : "");

    public string SizeText => FileSize is long s ? (FileSizeIsApprox ? "~" : "") + Formatting.Bytes(s) : "";

    public string Codecs => (HasVideo, HasAudio) switch
    {
        (true, true) => $"{VideoCodec} / {AudioCodec}",
        (true, false) => VideoCodec!,
        (false, true) => AudioCodec!,
        _ => "",
    };

    internal static FormatInfo FromJson(JsonElement f)
    {
        long? size = f.Long("filesize");
        bool approx = false;
        if (size == null) { size = f.Long("filesize_approx"); approx = size != null; }
        return new FormatInfo
        {
            FormatId = f.Str("format_id") ?? "",
            Extension = f.Str("ext") ?? "",
            Width = (int?)f.Num("width"),
            Height = (int?)f.Num("height"),
            Fps = f.Num("fps"),
            VideoCodec = f.Str("vcodec"),
            AudioCodec = f.Str("acodec"),
            Tbr = f.Num("tbr"),
            FileSize = size,
            FileSizeIsApprox = approx,
            Note = f.Str("format_note"),
        };
    }
}

internal static class JsonExt
{
    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static double? Num(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;

    public static long? Long(this JsonElement e, string name)
    {
        var d = e.Num(name);
        return d is null ? null : (long)d.Value;
    }
}
