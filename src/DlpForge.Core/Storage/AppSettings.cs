using System.Text.Json;
using System.Text.Json.Serialization;
using DlpForge.Core.Models;

namespace DlpForge.Core.Storage;

public enum ThemeMode { System, Light, Dark }

public sealed class AppSettings
{
    public string VideoFolder { get; set; } = AppPaths.DefaultVideoFolder;
    public string AudioFolder { get; set; } = AppPaths.DefaultAudioFolder;
    public string FilenameTemplate { get; set; } = Defaults.FilenameTemplate;
    public string PlaylistFilenameTemplate { get; set; } = "%(playlist_title)s/%(playlist_index)03d - %(title)s.%(ext)s";
    public int MaxConcurrentDownloads { get; set; } = 3;
    public DownloadType DefaultType { get; set; } = DownloadType.Video;
    public QualityPreset DefaultQuality { get; set; } = QualityPreset.Best;
    public string DefaultAudioFormat { get; set; } = "mp3";
    public string DefaultVideoContainer { get; set; } = "mp4";
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedChapters { get; set; } = true;
    public bool EmbedSubtitles { get; set; }
    public string SubtitleLanguages { get; set; } = "en.*";
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string YtDlpChannel { get; set; } = "stable";
    public bool ShowNotifications { get; set; } = true;

    public DownloadOptions ToOptions(DownloadType? type = null)
    {
        var t = type ?? DefaultType;
        return new DownloadOptions
        {
            Type = t,
            Quality = DefaultQuality,
            AudioFormat = DefaultAudioFormat,
            VideoContainer = DefaultVideoContainer,
            OutputFolder = t == DownloadType.Audio ? AudioFolder : VideoFolder,
            FilenameTemplate = FilenameTemplate,
            EmbedThumbnail = EmbedThumbnail,
            EmbedMetadata = EmbedMetadata,
            EmbedChapters = EmbedChapters,
            EmbedSubtitles = EmbedSubtitles,
            SubtitleLanguages = SubtitleLanguages,
        };
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A broken settings file falls back to defaults rather than blocking startup.
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
