using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DlpForge.App.Services;
using DlpForge.Core.Models;
using DlpForge.Core.Storage;
using DlpForge.Core.Tools;
using DlpForge.Core.YtDlp;

namespace DlpForge.App.ViewModels;

/// <summary>The Settings screen. Every change is saved immediately.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _toolsChanged;
    private AppSettings S => _services.Settings;

    public IReadOnlyList<Choice<ThemeMode>> ThemeChoices { get; } =
    [
        new(ThemeMode.System, "Use Windows setting"),
        new(ThemeMode.Light, "Light"),
        new(ThemeMode.Dark, "Dark"),
    ];

    public IReadOnlyList<Choice<DownloadType>> TypeChoices { get; } =
    [
        new(DownloadType.Video, "Video"),
        new(DownloadType.Audio, "Audio"),
    ];

    public IReadOnlyList<Choice<QualityPreset>> QualityChoices { get; } =
    [
        new(QualityPreset.Best, "Best available"),
        new(QualityPreset.P2160, "Up to 2160p"),
        new(QualityPreset.P1440, "Up to 1440p"),
        new(QualityPreset.P1080, "Up to 1080p"),
        new(QualityPreset.P720, "Up to 720p"),
        new(QualityPreset.P480, "Up to 480p"),
        new(QualityPreset.P360, "Up to 360p"),
    ];

    public IReadOnlyList<string> AudioFormats => Defaults.AudioFormats;
    public IReadOnlyList<string> VideoContainers => Defaults.VideoContainers;
    public IReadOnlyList<string> Channels { get; } = ["stable", "nightly", "master"];

    [ObservableProperty] private string _videoFolder;
    [ObservableProperty] private string _audioFolder;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FilenamePreview))] private string _filenameTemplate;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PlaylistFilenamePreview))] private string _playlistFilenameTemplate;
    [ObservableProperty] private decimal _maxConcurrent;
    [ObservableProperty] private Choice<DownloadType> _defaultType;
    [ObservableProperty] private Choice<QualityPreset> _defaultQuality;
    [ObservableProperty] private string _defaultAudioFormat;
    [ObservableProperty] private string _defaultVideoContainer;
    [ObservableProperty] private bool _embedThumbnail;
    [ObservableProperty] private bool _embedMetadata;
    [ObservableProperty] private bool _embedChapters;
    [ObservableProperty] private bool _embedSubtitles;
    [ObservableProperty] private string _subtitleLanguages;
    [ObservableProperty] private Choice<ThemeMode> _theme;
    [ObservableProperty] private bool _showNotifications;
    [ObservableProperty] private string _ytDlpChannel;
    [ObservableProperty] private string _rollbackTag = "";

    public ObservableCollection<ToolViewModel> Tools { get; } = [];
    [ObservableProperty] private string _toolLog = "";
    [ObservableProperty] private bool _isUpdatingYtDlp;

    public string FilenamePreview => TemplatePreview.Render(FilenameTemplate);
    public string PlaylistFilenamePreview => TemplatePreview.Render(PlaylistFilenameTemplate);
    public string DataFolder => _services.Paths.DataDir + (_services.Paths.IsPortable ? "  (portable mode)" : "");

    public SettingsViewModel(AppServices services, Action toolsChanged)
    {
        _services = services;
        _toolsChanged = toolsChanged;
        _videoFolder = S.VideoFolder;
        _audioFolder = S.AudioFolder;
        _filenameTemplate = S.FilenameTemplate;
        _playlistFilenameTemplate = S.PlaylistFilenameTemplate;
        _maxConcurrent = S.MaxConcurrentDownloads;
        _defaultType = TypeChoices.First(c => c.Value == S.DefaultType);
        _defaultQuality = QualityChoices.FirstOrDefault(c => c.Value == S.DefaultQuality) ?? QualityChoices[0];
        _defaultAudioFormat = S.DefaultAudioFormat;
        _defaultVideoContainer = S.DefaultVideoContainer;
        _embedThumbnail = S.EmbedThumbnail;
        _embedMetadata = S.EmbedMetadata;
        _embedChapters = S.EmbedChapters;
        _embedSubtitles = S.EmbedSubtitles;
        _subtitleLanguages = S.SubtitleLanguages;
        _theme = ThemeChoices.First(c => c.Value == S.Theme);
        _showNotifications = S.ShowNotifications;
        _ytDlpChannel = S.YtDlpChannel;

        foreach (var kind in new[] { ToolKind.YtDlp, ToolKind.Ffmpeg, ToolKind.Deno })
            Tools.Add(new ToolViewModel(kind, InstallAsync));
        _ = RefreshToolsAsync();
    }

    // Persist each change as it happens.
    partial void OnVideoFolderChanged(string value) => Save(s => s.VideoFolder = value);
    partial void OnAudioFolderChanged(string value) => Save(s => s.AudioFolder = value);
    partial void OnFilenameTemplateChanged(string value) => Save(s => s.FilenameTemplate = value);
    partial void OnPlaylistFilenameTemplateChanged(string value) => Save(s => s.PlaylistFilenameTemplate = value);
    partial void OnDefaultTypeChanged(Choice<DownloadType> value) => Save(s => s.DefaultType = value.Value);
    partial void OnDefaultQualityChanged(Choice<QualityPreset> value) => Save(s => s.DefaultQuality = value.Value);
    partial void OnDefaultAudioFormatChanged(string value) => Save(s => s.DefaultAudioFormat = value);
    partial void OnDefaultVideoContainerChanged(string value) => Save(s => s.DefaultVideoContainer = value);
    partial void OnEmbedThumbnailChanged(bool value) => Save(s => s.EmbedThumbnail = value);
    partial void OnEmbedMetadataChanged(bool value) => Save(s => s.EmbedMetadata = value);
    partial void OnEmbedChaptersChanged(bool value) => Save(s => s.EmbedChapters = value);
    partial void OnEmbedSubtitlesChanged(bool value) => Save(s => s.EmbedSubtitles = value);
    partial void OnSubtitleLanguagesChanged(string value) => Save(s => s.SubtitleLanguages = value);
    partial void OnShowNotificationsChanged(bool value) => Save(s => s.ShowNotifications = value);
    partial void OnYtDlpChannelChanged(string value) => Save(s => s.YtDlpChannel = value);

    partial void OnMaxConcurrentChanged(decimal value)
    {
        int n = Math.Clamp((int)value, 1, 10);
        _services.Queue.MaxConcurrent = n;
        Save(s => s.MaxConcurrentDownloads = n);
    }

    partial void OnThemeChanged(Choice<ThemeMode> value)
    {
        App.ApplyTheme(value.Value);
        Save(s => s.Theme = value.Value);
    }

    private void Save(Action<AppSettings> change)
    {
        change(S);
        _services.SaveSettings();
    }

    [RelayCommand]
    private async Task BrowseFolder(string which)
    {
        var top = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top is null) return;
        var current = which == "audio" ? AudioFolder : VideoFolder;
        var start = await top.StorageProvider.TryGetFolderFromPathAsync(current);
        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = which == "audio" ? "Folder for audio downloads" : "Folder for video downloads",
            SuggestedStartLocation = start,
        });
        if (result.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (which == "audio") AudioFolder = path; else VideoFolder = path;
    }

    [RelayCommand] private void ResetTemplate() => FilenameTemplate = Defaults.FilenameTemplate;
    [RelayCommand] private void OpenDataFolder() => Shell.OpenFolder(_services.Paths.DataDir);
    [RelayCommand] private void OpenLogsFolder() => Shell.OpenFolder(_services.Paths.LogsDir);
    [RelayCommand] private void OpenToolsFolder() => Shell.OpenFolder(_services.Paths.ToolsDir);

    // ---- Tools ----

    [RelayCommand]
    public async Task RefreshToolsAsync()
    {
        var statuses = await _services.Installer.GetStatusAsync();
        foreach (var s in statuses)
            Tools.First(t => t.Kind == s.Kind).Apply(s, _services.Paths.ToolsDir);
        _toolsChanged();
    }

    public async Task InstallMissingAsync()
    {
        await RefreshToolsAsync();
        foreach (var t in Tools.Where(t => !t.Installed).ToList())
            await InstallAsync(t);
    }

    private async Task InstallAsync(ToolViewModel tool)
    {
        if (tool.IsBusy) return;
        // yt-dlp updates itself in place; the others are re-downloaded.
        if (tool.Kind == ToolKind.YtDlp && tool.IsManaged)
        {
            await UpdateYtDlpAsync(YtDlpChannel);
            return;
        }

        tool.IsBusy = true;
        tool.Progress = 0;
        try
        {
            var progress = new Progress<double>(p => tool.Progress = p);
            await Task.Run(() => _services.Installer.InstallAsync(tool.Kind, progress));
            AppendLog($"{tool.Name} installed.");
        }
        catch (Exception ex)
        {
            AppendLog($"{tool.Name} install failed: {ex.Message}");
            NotificationService.ShowInApp($"{tool.Name} install failed", ex.Message, error: true);
        }
        finally
        {
            tool.IsBusy = false;
        }
        await RefreshToolsAsync();
    }

    [RelayCommand]
    private Task UpdateYtDlp() => UpdateYtDlpAsync(YtDlpChannel);

    [RelayCommand]
    private Task Rollback()
    {
        var tag = RollbackTag.Trim();
        if (tag.Length == 0) return Task.CompletedTask;
        // Accept "2025.10.22" or "stable@2025.10.22".
        return UpdateYtDlpAsync(tag.Contains('@') ? tag : $"{YtDlpChannel}@{tag}");
    }

    private async Task UpdateYtDlpAsync(string target)
    {
        var tool = Tools.First(t => t.Kind == ToolKind.YtDlp);
        if (!tool.Installed)
        {
            await InstallAsync(tool);
            return;
        }
        if (_services.Queue.Items.Any(i => i.IsActive))
        {
            NotificationService.ShowInApp("Downloads are running", "Pause them before updating yt-dlp.", error: true);
            return;
        }

        IsUpdatingYtDlp = true;
        tool.IsBusy = true;
        AppendLog($"> yt-dlp --update-to {target}");
        try
        {
            int code = await Task.Run(() => _services.Client.UpdateAsync(target,
                line => Dispatcher.UIThread.Post(() => AppendLog(line))));
            AppendLog(code == 0 ? "Update finished." : $"Update failed (exit code {code}).");
        }
        catch (Exception ex)
        {
            AppendLog("Update failed: " + ex.Message);
        }
        finally
        {
            tool.IsBusy = false;
            IsUpdatingYtDlp = false;
        }
        await RefreshToolsAsync();
    }

    private void AppendLog(string line)
    {
        ToolLog = (ToolLog.Length > 20_000 ? ToolLog[^10_000..] : ToolLog) + line + Environment.NewLine;
    }
}

public sealed partial class ToolViewModel : ViewModelBase
{
    private readonly Func<ToolViewModel, Task> _install;

    public ToolKind Kind { get; }
    public string Name { get; }
    public string Purpose { get; }

    [ObservableProperty] private string _version = "Checking…";
    [ObservableProperty] private string? _path;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ActionLabel))] private bool _installed;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ActionLabel))] private bool _isManaged;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public string ActionLabel => !Installed ? "Install" : IsManaged ? "Update" : "Install managed copy";

    public ToolViewModel(ToolKind kind, Func<ToolViewModel, Task> install)
    {
        Kind = kind;
        _install = install;
        (Name, Purpose) = kind switch
        {
            ToolKind.YtDlp => ("yt-dlp", "The downloader itself"),
            ToolKind.Ffmpeg => ("FFmpeg", "Merges video and audio, converts audio, embeds thumbnails and subtitles"),
            ToolKind.Deno => ("Deno", "JavaScript runtime yt-dlp needs for full YouTube support"),
            _ => (kind.ToString(), ""),
        };
    }

    public void Apply(ToolStatus s, string toolsDir)
    {
        Installed = s.Installed;
        Path = s.Path;
        IsManaged = s.Path?.StartsWith(toolsDir, StringComparison.OrdinalIgnoreCase) ?? false;
        Version = s.Installed ? s.Version ?? "Installed (version unknown)" : "Not installed";
    }

    [RelayCommand]
    private Task Install() => _install(this);
}
