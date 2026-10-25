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

    /// <summary>Skip the download card for links and reuse the last settings picked on it.</summary>
    public bool QuickDownload { get; set; }
    /// <summary>What was picked the last time the download card queued something. Used by quick download and import.</summary>
    public DownloadOptions? LastOptions { get; set; }
    /// <summary>How many results a search typed in the link bar shows.</summary>
    public int SearchResultCount { get; set; } = 20;

    public CookieMode CookieMode { get; set; } = CookieMode.None;
    public string CookieBrowser { get; set; } = "firefox";
    public string CookieProfile { get; set; } = "";
    public string CookieFile { get; set; } = "";

    /// <summary>Whether the SponsorBlock box on the download card starts ticked.</summary>
    public bool SponsorBlockByDefault { get; set; }
    public Dictionary<string, SponsorBlockAction> SponsorBlockActions { get; set; } = SponsorBlock.DefaultActions();

    /// <summary>Keep running in the notification area when the window is closed.</summary>
    public bool CloseToTray { get; set; }

    public List<CommandTemplate> CommandTemplates { get; set; } = CommandTemplate.Samples();
    /// <summary>Recent terminal commands, newest last.</summary>
    public List<string> TerminalHistory { get; set; } = [];

    public CookieOptions Cookies => new()
    {
        Mode = CookieMode,
        Browser = CookieBrowser,
        Profile = string.IsNullOrWhiteSpace(CookieProfile) ? null : CookieProfile,
        FilePath = string.IsNullOrWhiteSpace(CookieFile) ? null : CookieFile,
    };

    /// <summary>
    /// Settings for a download that skips the card (quick download, import): the last card choices when
    /// there are any, with the parts that only made sense for that one video taken out.
    /// </summary>
    public DownloadOptions QuickOptions(bool playlist)
    {
        var template = playlist ? PlaylistFilenameTemplate : FilenameTemplate;
        if (LastOptions is not { } last) return ToOptions() with { FilenameTemplate = template, ExtraArguments = "" };
        return last with
        {
            Quality = last.Quality == QualityPreset.Custom ? QualityPreset.Best : last.Quality,
            CustomFormat = null,
            OutputFolder = string.IsNullOrWhiteSpace(last.OutputFolder)
                ? (last.Type == DownloadType.Audio ? AudioFolder : VideoFolder)
                : last.OutputFolder,
            FilenameTemplate = template,
            Section = null,
            TitleOverride = null,
            ArtistOverride = null,
        };
    }

    public DownloadOptions ToOptions(DownloadType? type = null)
    {
        var t = type ?? DefaultType;
        var (mark, remove) = SponsorBlockByDefault ? SponsorBlock.ToArguments(SponsorBlockActions) : ("", "");
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
            SponsorBlockMark = mark,
            SponsorBlockRemove = remove,
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
