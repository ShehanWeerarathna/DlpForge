using System.Text.RegularExpressions;

namespace DlpForge.Core.YtDlp;

/// <summary>
/// Approximates what an output template like "%(title)s [%(id)s].%(ext)s" turns into, for the live
/// preview in the template editor. yt-dlp is the source of truth; this covers the common fields.
/// </summary>
public static partial class TemplatePreview
{
    public static readonly IReadOnlyDictionary<string, string> SampleValues = new Dictionary<string, string>
    {
        ["title"] = "Big Buck Bunny",
        ["id"] = "aqz-KE-bpKQ",
        ["ext"] = "mp4",
        ["uploader"] = "Blender",
        ["channel"] = "Blender",
        ["upload_date"] = "20140410",
        ["playlist"] = "Open Movies",
        ["playlist_title"] = "Open Movies",
        ["playlist_index"] = "3",
        ["resolution"] = "1920x1080",
        ["height"] = "1080",
        ["width"] = "1920",
        ["fps"] = "30",
        ["duration_string"] = "10:34",
        ["extractor"] = "youtube",
        ["artist"] = "Blender",
        ["album"] = "Open Movies",
        ["track"] = "Big Buck Bunny",
    };

    [GeneratedRegex(@"%\((?<field>[^)]*)\)(?<flags>[-#0+ ]*)(?<width>\d*)(?:\.\d+)?(?<conv>[sdfj])")]
    private static partial Regex FieldRegex();

    /// <summary>
    /// Playlist items are downloaded one URL at a time, so yt-dlp does not know their playlist.
    /// This writes the playlist title and index into the template as literal text instead.
    /// </summary>
    public static string FillPlaylistFields(string template, string playlistTitle, int index)
    {
        var safeTitle = SanitizeSegment(playlistTitle).Replace("%", "%%");
        return FieldRegex().Replace(template, m =>
        {
            var key = m.Groups["field"].Value.Split(',', '|', '.', '>', '&')[0].Trim();
            switch (key)
            {
                case "playlist" or "playlist_title":
                    return safeTitle;
                case "playlist_index" or "playlist_autonumber":
                    var w = m.Groups["width"].Value;
                    return w.Length > 0 && m.Groups["flags"].Value.Contains('0')
                        ? index.ToString().PadLeft(int.Parse(w), '0')
                        : index.ToString();
                default:
                    return m.Value;
            }
        });
    }

    private static string SanitizeSegment(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().TrimEnd('.');
    }

    public static string Render(string template, IReadOnlyDictionary<string, string>? values = null)
    {
        values ??= SampleValues;
        var result = FieldRegex().Replace(template, m =>
        {
            var spec = m.Groups["field"].Value;
            // "%(a,b)s" tries each field in turn; "%(a|default)s" falls back to the default text.
            string? fallback = null;
            int bar = spec.IndexOf('|');
            if (bar >= 0) { fallback = spec[(bar + 1)..]; spec = spec[..bar]; }

            foreach (var name in spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var key = name.Split('.', '>', '&')[0];
                if (values.TryGetValue(key, out var v))
                {
                    if (m.Groups["conv"].Value == "d" && m.Groups["width"].Value is { Length: > 0 } w
                        && int.TryParse(v, out var n) && m.Groups["flags"].Value.Contains('0'))
                        return n.ToString().PadLeft(int.Parse(w), '0');
                    return v;
                }
            }
            return fallback ?? "NA";
        });

        // Show what Windows would do with characters it does not allow in file names (folders are allowed).
        var invalid = Path.GetInvalidFileNameChars().Where(c => c is not '/' and not '\\').ToArray();
        foreach (var c in invalid) result = result.Replace(c, '_');
        return result;
    }
}
