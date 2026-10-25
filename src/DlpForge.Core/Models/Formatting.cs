using System.Globalization;

namespace DlpForge.Core.Models;

public static class Formatting
{
    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.0} {units[i]}";
    }

    public static string Duration(double? seconds)
    {
        if (seconds is not double s || s <= 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>A position in a video: "0:00", "1:23", "1:02:03". Unlike <see cref="Duration"/>, zero is shown.</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>Reads "83", "1:23", "1:02:03" or "1:23.5" as seconds. Returns null for anything else.</summary>
    public static double? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            var style = last ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
            if (!double.TryParse(parts[i], style, CultureInfo.InvariantCulture, out var n) || n < 0) return null;
            // Minutes and seconds after the first part must be below 60.
            if (i > 0 && n >= 60) return null;
            total = total * 60 + n;
        }
        return total;
    }

    /// <summary>Seconds as yt-dlp reads them in --download-sections: invariant culture, at most 3 decimals.</summary>
    public static string Seconds(double seconds) =>
        Math.Round(seconds, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
