namespace DlpForge.Core.Models;

public enum CookieMode { None, Browser, File }

/// <summary>Where yt-dlp gets cookies from, for sites that need a signed-in session or age check.</summary>
public sealed record CookieOptions
{
    public CookieMode Mode { get; init; }
    /// <summary>A browser yt-dlp knows: chrome, edge, firefox, brave, opera, vivaldi, chromium, whale.</summary>
    public string Browser { get; init; } = "firefox";
    /// <summary>Optional browser profile name or path.</summary>
    public string? Profile { get; init; }
    /// <summary>A Netscape-format cookies.txt file.</summary>
    public string? FilePath { get; init; }

    public static readonly string[] Browsers = ["firefox", "chrome", "edge", "brave", "opera", "vivaldi", "chromium", "whale"];
}
