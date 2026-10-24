namespace DlpForge.Core.Models;

public enum DownloadType { Video, Audio }

public enum DownloadStatus { Queued, Downloading, Processing, Paused, Completed, Failed, Cancelled }

/// <summary>Quick quality presets shown on the download card.</summary>
public enum QualityPreset { Best, P2160, P1440, P1080, P720, P480, P360, Custom }

/// <summary>Everything needed to build one yt-dlp command line. Stored as JSON in the downloads database.</summary>
public sealed record DownloadOptions
{
    public DownloadType Type { get; init; } = DownloadType.Video;
    public QualityPreset Quality { get; init; } = QualityPreset.Best;
    /// <summary>Explicit yt-dlp -f selector, used when Quality is Custom.</summary>
    public string? CustomFormat { get; init; }
    /// <summary>Audio target for audio downloads: mp3, m4a, opus, flac, wav, or "best" to keep the source.</summary>
    public string AudioFormat { get; init; } = "mp3";
    /// <summary>Container to merge video into: mp4, mkv, webm. Empty lets yt-dlp decide.</summary>
    public string VideoContainer { get; init; } = "mp4";
    public string OutputFolder { get; init; } = "";
    public string FilenameTemplate { get; init; } = Defaults.FilenameTemplate;
    public bool EmbedThumbnail { get; init; } = true;
    public bool EmbedMetadata { get; init; } = true;
    public bool EmbedChapters { get; init; } = true;
    public bool EmbedSubtitles { get; init; }
    public string SubtitleLanguages { get; init; } = "en.*";
}

public static class Defaults
{
    public const string FilenameTemplate = "%(title)s [%(id)s].%(ext)s";
    public static readonly string[] AudioFormats = ["mp3", "m4a", "opus", "flac", "wav", "best"];
    public static readonly string[] VideoContainers = ["mp4", "mkv", "webm"];
}
