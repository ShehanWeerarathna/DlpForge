using System.Text.RegularExpressions;

namespace DlpForge.Core;

/// <summary>Pulls links out of pasted text, dropped text, .txt link lists and Windows .url shortcuts.</summary>
public static partial class LinkParser
{
    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // yt-dlp search prefixes: ytsearch:, ytsearch10:, ytsearchall:, ytsearchdate5:, scsearch3: and so on.
    [GeneratedRegex(@"^[a-z0-9]+search(date)?(\d+|all)?:", RegexOptions.IgnoreCase)]
    private static partial Regex SearchPrefixRegex();

    // A link typed without the scheme: youtube.com/watch?v=..., www.example.org/x, youtu.be/abc
    [GeneratedRegex(@"^(www\.)?[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}(/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BareDomainRegex();

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
        return IsUrl(text) || IsSearch(text);
    }

    public static bool IsUrl(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static bool IsSearch(string text) => SearchPrefixRegex().IsMatch(text.Trim());

    /// <summary>Adds https:// to something that is clearly a link typed without it, like "youtu.be/abc".</summary>
    public static string? AsBareLink(string text)
    {
        text = text.Trim();
        return !text.Contains(' ') && text.Contains('/') && BareDomainRegex().IsMatch(text) ? "https://" + text : null;
    }

    /// <summary>A yt-dlp YouTube search for plain words typed in the link bar.</summary>
    public static string ToSearch(string words, int count) => $"ytsearch{Math.Clamp(count, 1, 100)}:{words.Trim()}";

    /// <summary>
    /// Reads an import list: one entry per line. A line can be a link (playlists included), a yt-dlp
    /// search such as "ytsearch5:words", or plain words, which become a search for the best match.
    /// Blank lines and lines starting with # or // are skipped. Duplicates are dropped.
    /// </summary>
    public static List<string> ParseImportList(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;

            string entry;
            if (IsSearch(line)) entry = line;
            else if (Extract(line) is { Count: > 0 } links) entry = links[0];
            else if (AsBareLink(line) is { } bare) entry = bare;
            else entry = ToSearch(line, 1);

            if (!result.Contains(entry, StringComparer.Ordinal)) result.Add(entry);
        }
        return result;
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
        if (IsListFile(path))
        {
            var info = new FileInfo(path);
            if (info.Length > 5_000_000) return []; // Not a link list.
            return Extract(File.ReadAllText(path));
        }
        return [];
    }

    public static bool IsListFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".list" or "";
}
