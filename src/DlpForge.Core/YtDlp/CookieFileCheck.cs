using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DlpForge.Core.YtDlp;

public enum NoteLevel { Info, Ok, Warning, Error }

/// <summary>One finding from a cookie check, shown as a line under the cookies.txt box in Settings.</summary>
public sealed record CheckNote(NoteLevel Level, string Text);

/// <summary>What reading a cookies.txt file found.</summary>
/// <param name="Readable">yt-dlp can load the file. When false, yt-dlp stops every call with an error.</param>
/// <param name="HasYouTubeSignIn">The file has unexpired YouTube sign-in cookies, so asking YouTube is worth it.</param>
public sealed record CookieFileReport(IReadOnlyList<CheckNote> Notes, bool Readable, bool HasYouTubeSignIn);

/// <summary>
/// Checks a cookies.txt file the way yt-dlp reads it, without running yt-dlp or going online, and asks
/// YouTube (through yt-dlp) whether the cookies still sign you in.
/// </summary>
/// <remarks>
/// The rules match yt-dlp 2026.10. yt-dlp refuses the whole file when it is JSON, starts with a byte order
/// mark, lacks the "# Netscape HTTP Cookie File" header, or has a line whose second column disagrees with a
/// leading dot on the domain. It skips lines without 7 tab-separated columns or with an expiry that isn't a
/// plain number, and drops them from the file when it saves the cookies after a run. A missing file is not
/// an error to yt-dlp: it runs without cookies and creates an empty file.
/// </remarks>
public static partial class CookieFileCheck
{
    public const long MaxFileSize = 10 * 1024 * 1024;

    private const string HttpOnlyPrefix = "#HttpOnly_";

    // Python's http.cookiejar matches this at the start of the first line yt-dlp keeps.
    [GeneratedRegex("^#( Netscape)? HTTP Cookie File")]
    private static partial Regex Header();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)?$")]
    private static partial Regex Expiry();

    // Python reads text files with these line breaks only.
    [GeneratedRegex("\r\n|\r|\n")]
    private static partial Regex LineBreak();

    // yt-dlp treats YouTube cookies as a signed-in account when LOGIN_INFO and one of these are present.
    private static readonly string[] YouTubeSessionCookies = ["SAPISID", "__Secure-1PAPISID", "__Secure-3PAPISID"];

    private const string ExportTip = "Export the cookies again with a browser extension that saves cookies.txt (Netscape) format.";

    private sealed record Cookie(int Line, string Domain, string Name, DateTimeOffset? Expires);

    public static CookieFileReport CheckFile(string? path, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path)) return Fail("Choose a cookies.txt file first.");
        try
        {
            var info = new FileInfo(path.Trim());
            if (!info.Exists)
                return Fail("The file doesn't exist. yt-dlp would run without cookies and create an empty file there.");
            if (info.Length > MaxFileSize)
                return Fail("The file is larger than 10 MB, so it isn't a cookies.txt file.");
            return Check(File.ReadAllBytes(info.FullName), now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail("DlpForge couldn't read the file: " + ex.Message);
        }
    }

    public static CookieFileReport Check(byte[] bytes, DateTimeOffset now)
    {
        if (bytes.Length >= 2 && (bytes[0], bytes[1]) is (0xFF, 0xFE) or (0xFE, 0xFF))
            return Fail("The file is saved as UTF-16 (\"Unicode\"), which yt-dlp can't read. Open it in Notepad and save it with the encoding UTF-8.");

        var errors = new List<string>();
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        if (bom)
            errors.Add("The file starts with a byte order mark (BOM), so yt-dlp doesn't see the header line. Open it in Notepad and save it with the encoding UTF-8, not \"UTF-8 with BOM\".");

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            return Fail("The file isn't UTF-8 text, so it isn't a cookies.txt file. " + ExportTip);
        }
        if (string.IsNullOrWhiteSpace(text)) return Fail("The file is empty.");

        var cookies = new List<Cookie>();
        var skipped = new List<int>();
        string? firstKept = null;
        int mismatchLine = 0, mismatches = 0;

        var lines = LineBreak().Split(text);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i == lines.Length - 1 && line.Length == 0) break;   // after the last line break
            bool httpOnly = line.StartsWith(HttpOnlyPrefix, StringComparison.Ordinal);
            var entry = httpOnly ? line[HttpOnlyPrefix.Length..] : line;

            if (entry.StartsWith('#') || string.IsNullOrWhiteSpace(entry))
            {
                firstKept ??= entry;
                continue;
            }

            var f = entry.Split('\t');
            if (f.Length != 7 || (f[4].Length > 0 && !Expiry().IsMatch(f[4])))
            {
                if (line.Trim() is { Length: > 0 } t && t[0] is '[' or '{' or '"')
                    return Fail("This is a JSON export. yt-dlp only reads cookies.txt (Netscape) format. " + ExportTip);
                skipped.Add(i + 1);
                continue;
            }

            firstKept ??= entry;
            if (entry.Trim().StartsWith('#') || entry.Trim().StartsWith('$')) continue;   // a comment to yt-dlp after all

            if ((f[1] == "TRUE") != f[0].StartsWith('.'))
            {
                if (mismatches++ == 0) mismatchLine = i + 1;
                continue;
            }

            DateTimeOffset? expires = null;
            if (f[4].Length > 0 && double.TryParse(f[4], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && seconds >= 1)
                expires = DateTimeOffset.FromUnixTimeSeconds((long)Math.Min(seconds, 253402300799));
            cookies.Add(new Cookie(i + 1, f[0].TrimStart('.').ToLowerInvariant(), f[5], expires));
        }

        if (firstKept is null || !Header().IsMatch(firstKept))
            errors.Add("The first line must be \"# Netscape HTTP Cookie File\". Without it yt-dlp refuses the file. " + ExportTip);
        if (mismatches > 0)
            errors.Add($"Line {mismatchLine}{(mismatches > 1 ? $" (and {mismatches - 1} more)" : "")}: the second column must be TRUE when the domain starts with a dot and FALSE when it doesn't. yt-dlp refuses the whole file because of this. {ExportTip}");

        if (errors.Count > 0)
            return new CookieFileReport([.. errors.Select(e => new CheckNote(NoteLevel.Error, e))], false, false);

        var notes = new List<CheckNote>();
        if (cookies.Count == 0)
            notes.Add(new(NoteLevel.Error, "yt-dlp can read the file, but there are no cookies in it."));
        else
            notes.Add(new(NoteLevel.Ok, $"yt-dlp can read this file: {Plural(cookies.Count, "cookie")} for {SiteSummary(cookies)}."));

        if (skipped.Count > 0)
            notes.Add(new(NoteLevel.Warning,
                $"{Plural(skipped.Count, "line")} {(skipped.Count == 1 ? "isn't" : "aren't")} in cookies.txt format ({LineList(skipped)}). " +
                "yt-dlp skips them and removes them from the file the next time it saves the cookies."));

        int expired = cookies.Count(c => c.Expires < now);
        if (expired > 0)
            notes.Add(new(NoteLevel.Info, $"{Plural(expired, "cookie")} {(expired == 1 ? "has" : "have")} expired and won't be sent."));

        bool signedIn = AddYouTubeNotes(cookies, now, notes);
        return new CookieFileReport(notes, true, signedIn);
    }

    private static bool AddYouTubeNotes(List<Cookie> cookies, DateTimeOffset now, List<CheckNote> notes)
    {
        var youTube = cookies.Where(c => c.Domain == "youtube.com" || c.Domain.EndsWith(".youtube.com", StringComparison.Ordinal)).ToList();
        if (youTube.Count == 0)
        {
            if (cookies.Count > 0) notes.Add(new(NoteLevel.Info, "There are no YouTube cookies, so the file won't sign you in to YouTube."));
            return false;
        }

        var loginInfo = youTube.Where(c => c.Name == "LOGIN_INFO").ToList();
        var session = youTube.Where(c => YouTubeSessionCookies.Contains(c.Name)).ToList();
        if (loginInfo.Count == 0 || session.Count == 0)
        {
            notes.Add(new(NoteLevel.Warning,
                "The YouTube cookies are from a signed-out browser (LOGIN_INFO or SAPISID is missing). Sign in to YouTube, then export the cookies again."));
            return false;
        }

        bool Live(Cookie c) => c.Expires is null || c.Expires >= now;
        if (!loginInfo.Any(Live) || !session.Any(Live))
        {
            var ended = loginInfo.Concat(session).Where(c => !Live(c)).Max(c => c.Expires)!.Value;
            notes.Add(new(NoteLevel.Error, $"The YouTube sign-in cookies expired on {Date(ended)}. Export a fresh file."));
            return false;
        }

        var ends = loginInfo.Concat(session).Where(c => c.Expires >= now).Select(c => c.Expires!.Value).DefaultIfEmpty().Min();
        notes.Add(new(NoteLevel.Ok, "YouTube sign-in cookies found" + (ends == default ? "." : $", valid until {Date(ends)}.")));
        return true;
    }

    /// <summary>The URL used to test a YouTube sign-in: the Watch Later list, which every account has and needs an account to open.</summary>
    public const string SignInTestUrl = "https://www.youtube.com/playlist?list=WL";

    private const string RotatedWarning = "cookies are no longer valid";

    /// <summary>Reads yt-dlp's answer to the sign-in test (<see cref="ArgumentBuilder.ForSignInTest"/>).</summary>
    public static CheckNote InterpretSignInTest(int exitCode, IEnumerable<string> stderr)
    {
        var lines = stderr.ToList();
        if (lines.Any(l => l.Contains(RotatedWarning, StringComparison.OrdinalIgnoreCase)))
            return new(NoteLevel.Error,
                "YouTube rejected these cookies: they were signed out or replaced by the browser. Export them again from a private window, " +
                "then close that window without using YouTube in it, so the browser doesn't replace them.");
        if (exitCode == 0)
            return new(NoteLevel.Ok, "YouTube accepted the cookies: you're signed in.");

        var error = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal))?["ERROR:".Length..].Trim();
        if (error?.Contains("Login details are needed", StringComparison.OrdinalIgnoreCase) == true)
            return new(NoteLevel.Error, "YouTube found no signed-in account in these cookies.");
        return new(NoteLevel.Warning, "DlpForge couldn't check the sign-in with YouTube: " + (error ?? $"yt-dlp exited with code {exitCode}."));
    }

    private static CookieFileReport Fail(string message) => new([new CheckNote(NoteLevel.Error, message)], false, false);

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";

    private static string Date(DateTimeOffset d) => d.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    private static string LineList(List<int> lines)
    {
        var shown = lines.Take(3).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList();
        var list = shown.Count == 1 ? shown[0] : string.Join(", ", shown[..^1]) + " and " + shown[^1];
        return (lines.Count == 1 ? "line " : "lines ") + list + (lines.Count > 3 ? $" and {lines.Count - 3} more" : "");
    }

    /// <summary>"youtube.com, google.com and 3 more sites", busiest first.</summary>
    private static string SiteSummary(List<Cookie> cookies)
    {
        var sites = cookies.GroupBy(c => Site(c.Domain)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        if (sites.Count == 1) return sites[0];
        if (sites.Count <= 3) return string.Join(", ", sites[..^1]) + " and " + sites[^1];
        return string.Join(", ", sites.Take(3)) + $" and {sites.Count - 3} more site{(sites.Count - 3 == 1 ? "" : "s")}";
    }

    /// <summary>The site a cookie domain belongs to: "accounts.google.com" gives "google.com", "www.bbc.co.uk" gives "bbc.co.uk".</summary>
    internal static string Site(string domain)
    {
        var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2 || parts[^1].All(char.IsAsciiDigit)) return domain;
        // Two-letter country endings with a short second level, such as co.uk or com.au, keep three parts.
        int keep = parts[^1].Length == 2 && parts[^2].Length <= 3 ? 3 : 2;
        return string.Join('.', parts[^Math.Min(keep, parts.Length)..]);
    }
}
