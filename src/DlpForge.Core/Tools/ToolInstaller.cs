using System.IO.Compression;
using DlpForge.Core.YtDlp;

namespace DlpForge.Core.Tools;

public enum ToolKind { YtDlp, Ffmpeg, Deno }

public sealed record ToolStatus(ToolKind Kind, string Name, string? Path, string? Version)
{
    public bool Installed => Path != null;
}

/// <summary>Downloads and updates the command-line tools DlpForge depends on, into the app's tools folder.</summary>
public sealed class ToolInstaller(ToolLocator locator, HttpClient http)
{
    // Official release feeds. FFmpeg comes from yt-dlp's own builds, which carry patches yt-dlp relies on.
    public const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    public const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    public async Task<IReadOnlyList<ToolStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        var ytdlp = locator.YtDlp;
        var ffmpeg = locator.Ffmpeg;
        var deno = locator.Deno;
        var versions = await Task.WhenAll(
            ytdlp is null ? Task.FromResult<string?>(null) : YtDlpClient.FirstLineAsync(ytdlp, ["--version"], ct),
            ffmpeg is null ? Task.FromResult<string?>(null) : YtDlpClient.FirstLineAsync(ffmpeg, ["-hide_banner", "-version"], ct),
            deno is null ? Task.FromResult<string?>(null) : YtDlpClient.FirstLineAsync(deno, ["--version"], ct));

        return
        [
            new ToolStatus(ToolKind.YtDlp, "yt-dlp", ytdlp, versions[0]),
            new ToolStatus(ToolKind.Ffmpeg, "FFmpeg", ffmpeg, ShortFfmpegVersion(versions[1])),
            new ToolStatus(ToolKind.Deno, "Deno", deno, versions[2]),
        ];
    }

    public Task InstallAsync(ToolKind kind, IProgress<double>? progress, CancellationToken ct = default) => kind switch
    {
        ToolKind.YtDlp => DownloadFileAsync(YtDlpUrl, locator.YtDlpInstallPath, progress, ct),
        ToolKind.Ffmpeg => InstallFfmpegAsync(progress, ct),
        ToolKind.Deno => InstallZipEntriesAsync(DenoUrl, ["deno.exe"], Path.GetDirectoryName(locator.DenoInstallPath)!, progress, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private Task InstallFfmpegAsync(IProgress<double>? progress, CancellationToken ct) =>
        InstallZipEntriesAsync(FfmpegUrl, ["ffmpeg.exe", "ffprobe.exe"], locator.FfmpegInstallDir, progress, ct);

    private async Task InstallZipEntriesAsync(string url, string[] wanted, string targetDir, IProgress<double>? progress, CancellationToken ct)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"dlpforge-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(url, zipPath, progress, ct);
            Directory.CreateDirectory(targetDir);
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var name in wanted)
            {
                var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"{name} was not found in the downloaded archive.");
                var dest = Path.Combine(targetDir, name);
                var tmp = dest + ".tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, dest, overwrite: true);
            }
        }
        finally
        {
            try { File.Delete(zipPath); } catch (IOException) { }
        }
    }

    private async Task DownloadFileAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".download";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total is > 0) progress?.Report(done * 100.0 / total.Value);
            }
        }
        File.Move(tmp, dest, overwrite: true);
        progress?.Report(100);
    }

    private static string? ShortFfmpegVersion(string? line)
    {
        // "ffmpeg version N-121234-gabc123-20251020 Copyright ..." -> "N-121234-gabc123-20251020"
        if (line is null) return null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[1] == "version" ? parts[2] : line;
    }
}
