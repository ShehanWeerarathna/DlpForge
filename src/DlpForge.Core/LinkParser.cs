using System.Text.RegularExpressions;

namespace DlpForge.Core;

/// <summary>Pulls links out of pasted text, dropped text, .txt link lists and Windows .url shortcuts.</summary>
public static partial class LinkParser
{
    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public static List<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return UrlRegex().Matches(text)
            .Select(m => m.Value.TrimEnd('.', ',', ')', ']', ';'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Accepts what a user might type in the link box: a URL, or a yt-dlp search like "ytsearch5:lofi".</summary>
    public static bool LooksLikeInput(string text)
    {
        text = text.Trim();
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"^[a-z]+search\d*(all)?:", RegexOptions.IgnoreCase);
    }

    public static List<string> FromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".url")
        {
            // [InternetShortcut]\nURL=https://...
            var line = File.ReadLines(path).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
            return line is null ? [] : Extract(line[4..]);
        }
        if (ext is ".txt" or ".list" or "")
        {
            var info = new FileInfo(path);
            if (info.Length > 5_000_000) return []; // Not a link list.
            return Extract(File.ReadAllText(path));
        }
        return [];
    }
}
