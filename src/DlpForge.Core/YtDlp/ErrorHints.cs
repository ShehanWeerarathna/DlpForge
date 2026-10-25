using System.Text.RegularExpressions;

namespace DlpForge.Core.YtDlp;

/// <summary>Adds a plain-language hint to yt-dlp errors that have a known fix.</summary>
public static partial class ErrorHints
{
    // A quoted Windows path in a Python error message: 'C:\\...'
    [GeneratedRegex(@"'([A-Za-z]:\\\\[^']+)'")]
    private static partial Regex QuotedPath();

    public static string Explain(string error)
    {
        var hint = HintFor(error);
        return hint is null ? error : $"{hint}\n{error}";
    }

    internal static string? HintFor(string error)
    {
        if (error.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
            && QuotedPath().Match(error) is { Success: true } m
            && m.Groups[1].Value.Replace(@"\\", @"\").Length >= 255)
            return "The file path is longer than Windows allows (260 characters). Pick a shorter download folder or file name template, or turn on long paths in Windows.";

        if (error.Contains("Netscape format cookies file", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Cookies file must be Netscape formatted", StringComparison.OrdinalIgnoreCase))
            return "yt-dlp couldn't read the cookies.txt file. Open Settings > Cookies and click Verify to see what's wrong with it.";

        if (error.Contains("cookies database", StringComparison.OrdinalIgnoreCase)
            || error.Contains("cookie database", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Failed to decrypt with DPAPI", StringComparison.OrdinalIgnoreCase))
            return "DlpForge couldn't read the browser's cookies. Check Settings > Cookies: the browser must be installed and signed in. Chrome and Edge must be closed; a cookies.txt file always works.";

        if (error.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Private video", StringComparison.OrdinalIgnoreCase)
            || error.Contains("members-only", StringComparison.OrdinalIgnoreCase)
            || error.Contains("--cookies", StringComparison.Ordinal))
            return "This video needs a signed-in session. Turn on cookies in Settings > Cookies and try again.";

        return null;
    }
}
