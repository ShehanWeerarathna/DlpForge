using System.Globalization;
using System.Text;

namespace DlpForge.Core.YtDlp;

public sealed record BrowserCookie(string Domain, string Path, string Name, string Value,
    bool Secure, bool HttpOnly, DateTimeOffset? Expires);

/// <summary>
/// Turns the cookies of DlpForge's YouTube window into a cookies.txt file, and manages the files it saves.
/// Only YouTube cookies are written, including HttpOnly cookies that page scripts can't see.
/// </summary>
public static class YouTubeCookieExport
{
    private const string FilePrefix = "youtube-";

    public static string Serialize(IEnumerable<BrowserCookie> cookies, DateTimeOffset now)
    {
        var text = new StringBuilder("# Netscape HTTP Cookie File\n");
        foreach (var c in cookies)
        {
            var host = c.Domain.TrimStart('.');
            if (!(host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
                  || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))) continue;
            if (c.Expires is { } expires && expires <= now) continue;
            // Never allow a cookie field to inject a new Netscape row or column.
            string[] fields = [c.Domain, c.Path, c.Name, c.Value];
            if (fields.Any(f => f.IndexOfAny(['\t', '\r', '\n', '\0']) >= 0))
                throw new InvalidDataException("A browser cookie contains an unsupported character. Please sign in again.");
            text.Append(c.HttpOnly ? "#HttpOnly_" : "").Append(c.Domain).Append('\t')
                .Append(c.Domain.StartsWith('.') ? "TRUE" : "FALSE").Append('\t')
                .Append(c.Path).Append('\t').Append(c.Secure ? "TRUE" : "FALSE").Append('\t')
                .Append((c.Expires?.ToUnixTimeSeconds() ?? 0).ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(c.Name).Append('\t').Append(c.Value).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Whether the cookies hold a signed-in YouTube session, by the same rule Verify uses.</summary>
    public static bool HasSignIn(IEnumerable<BrowserCookie> cookies, DateTimeOffset now)
    {
        try
        {
            return CookieFileCheck.Check(Encoding.UTF8.GetBytes(Serialize(cookies, now)), now).HasYouTubeSignIn;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// YouTube, or Google including its country domains (google.lk, google.co.uk, google.com.au), and their subdomains.
    /// Google's sign-in passes through the country domain when it sets cookies there.
    /// </summary>
    public static bool IsGoogleOrYouTubeHost(string host)
    {
        var parts = host.TrimEnd('.').ToLowerInvariant().Split('.');
        int n = parts.Length;
        if (n >= 2 && parts[n - 2] == "youtube" && parts[n - 1] == "com") return true;
        if (n >= 2 && parts[n - 2] == "google" && (parts[n - 1] == "com" || IsCountryCode(parts[n - 1]))) return true;
        return n >= 3 && parts[n - 3] == "google" && parts[n - 2] is "co" or "com" && IsCountryCode(parts[n - 1]);

        static bool IsCountryCode(string label) => label.Length == 2 && label.All(char.IsAsciiLetterLower);
    }

    /// <summary>Where the YouTube window saves cookies files: data\cookies.</summary>
    public static string SavedFolder(string dataDir) => System.IO.Path.Combine(dataDir, "cookies");

    /// <summary>
    /// A new file name for each save. A download that is running when cookies are saved writes its own copy back
    /// into the file it was given when it ends, so reusing one name could bring back old cookies.
    /// </summary>
    public static string NewSavedFile(string dataDir) =>
        System.IO.Path.Combine(SavedFolder(dataDir), $"{FilePrefix}{Guid.NewGuid():N}.txt");

    /// <summary>Whether a path is a cookies file the YouTube window saved.</summary>
    public static bool IsSavedFile(string dataDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = System.IO.Path.GetFullPath(path.Trim());
            return string.Equals(System.IO.Path.GetDirectoryName(full), System.IO.Path.GetFullPath(SavedFolder(dataDir)), StringComparison.OrdinalIgnoreCase)
                && System.IO.Path.GetFileName(full).StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Deletes saved cookies files other than <paramref name="keep"/>. They hold account credentials, so old ones shouldn't linger.</summary>
    public static void DeleteSavedFiles(string dataDir, string? keep)
    {
        var folder = SavedFolder(dataDir);
        if (!Directory.Exists(folder)) return;
        var kept = IsSavedFile(dataDir, keep) ? System.IO.Path.GetFullPath(keep!.Trim()) : null;
        foreach (var file in Directory.EnumerateFiles(folder, FilePrefix + "*.txt"))
        {
            if (string.Equals(System.IO.Path.GetFullPath(file), kept, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
