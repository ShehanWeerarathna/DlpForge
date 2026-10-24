namespace DlpForge.Core.Models;

/// <summary>One parsed progress update from a running yt-dlp process.</summary>
public sealed record DownloadProgress
{
    /// <summary>"downloading", "finished" (one file done), "processing" (a postprocessor is running).</summary>
    public string Stage { get; init; } = "downloading";
    public double? Percent { get; init; }
    public long? DownloadedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public double? EtaSeconds { get; init; }
    public string? Postprocessor { get; init; }
}
