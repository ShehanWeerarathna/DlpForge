namespace DlpForge.Core.Tools;

/// <summary>Finds yt-dlp, FFmpeg and Deno: the app-managed tools folder first, then a "tools" folder next to the exe, then PATH.</summary>
public sealed class ToolLocator(AppPaths paths)
{
    public string YtDlpInstallPath => Path.Combine(paths.ToolsDir, "yt-dlp.exe");
    public string FfmpegInstallDir => Path.Combine(paths.ToolsDir, "ffmpeg");
    public string DenoInstallPath => Path.Combine(paths.ToolsDir, "deno.exe");

    public string? YtDlp => Find("yt-dlp.exe", YtDlpInstallPath);
    public string? Ffmpeg => Find("ffmpeg.exe", Path.Combine(FfmpegInstallDir, "ffmpeg.exe"));
    public string? Deno => Find("deno.exe", DenoInstallPath);

    /// <summary>yt-dlp's --ffmpeg-location accepts the folder that holds ffmpeg and ffprobe.</summary>
    public string? FfmpegFolder => Ffmpeg is { } f ? Path.GetDirectoryName(f) : null;

    private static string? Find(string exe, string managedPath)
    {
        if (File.Exists(managedPath)) return managedPath;

        var besideApp = Path.Combine(AppContext.BaseDirectory, "tools", exe);
        if (File.Exists(besideApp)) return besideApp;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }
}
