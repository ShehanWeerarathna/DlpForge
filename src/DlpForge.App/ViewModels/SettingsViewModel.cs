using System.Collections.ObjectModel;
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

    public IReadOnlyList<Choice<CookieMode>> CookieModeChoices { get; } =
    [
        new(CookieMode.None, "Don't use cookies"),
        new(CookieMode.Browser, "From a browser"),
        new(CookieMode.File, "From a cookies.txt file"),
    ];
    public IReadOnlyList<string> CookieBrowsers => CookieOptions.Browsers;

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

    [ObservableProperty] private bool _quickDownload;
    [ObservableProperty] private decimal _searchResultCount;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsCookieBrowser), nameof(IsCookieFile), nameof(ShowCookieCheck))] private Choice<CookieMode> _selectedCookieMode;
    [ObservableProperty] private string _cookieBrowser;
    [ObservableProperty] private string _cookieProfile;
    [ObservableProperty] private string _cookieFile;
    [ObservableProperty] private bool _sponsorBlockByDefault;
    [ObservableProperty] private bool _closeToTray;
    public bool IsCookieBrowser => SelectedCookieMode.Value == CookieMode.Browser;
    public bool IsCookieFile => SelectedCookieMode.Value == CookieMode.File;
    /// <summary>What the last Verify of the cookies.txt file found.</summary>
    public ObservableCollection<CheckNoteViewModel> CookieNotes { get; } = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowCookieCheck))] private string _cookieCheckStatus = "";
    public bool ShowCookieCheck => IsCookieFile && (CookieNotes.Count > 0 || CookieCheckStatus.Length > 0);
    private CancellationTokenSource? _cookieCheck;
    public ObservableCollection<SponsorBlockCategoryViewModel> SponsorBlockCategories { get; } = [];
    public ObservableCollection<CommandTemplateViewModel> CommandTemplates { get; } = [];

    /// <summary>Raised when "Keep running in the tray" changes, so the tray icon can show or hide.</summary>
    public event Action<bool>? CloseToTrayChanged;

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
        _quickDownload = S.QuickDownload;
        _searchResultCount = Math.Clamp(S.SearchResultCount, 1, 100);
        _selectedCookieMode = CookieModeChoices.First(c => c.Value == S.CookieMode);
        _cookieBrowser = CookieOptions.Browsers.Contains(S.CookieBrowser) ? S.CookieBrowser : "firefox";
        _cookieProfile = S.CookieProfile;
        _cookieFile = S.CookieFile;
        _sponsorBlockByDefault = S.SponsorBlockByDefault;
        _closeToTray = S.CloseToTray;
        _services.Client.Cookies = S.Cookies;
        CookieNotes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowCookieCheck));

        foreach (var c in SponsorBlock.Categories)
        {
            var action = S.SponsorBlockActions.GetValueOrDefault(c.Id, SponsorBlockAction.Off);
            SponsorBlockCategories.Add(new SponsorBlockCategoryViewModel(c, action, SaveSponsorBlock));
        }
        foreach (var t in S.CommandTemplates) AddTemplateRow(t);

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
    partial void OnQuickDownloadChanged(bool value) => Save(s => s.QuickDownload = value);
    partial void OnSearchResultCountChanged(decimal value) => Save(s => s.SearchResultCount = Math.Clamp((int)value, 1, 100));
    partial void OnSelectedCookieModeChanged(Choice<CookieMode> value) => SaveCookies(s => s.CookieMode = value.Value);
    partial void OnCookieBrowserChanged(string value) => SaveCookies(s => s.CookieBrowser = value);
    partial void OnCookieProfileChanged(string value) => SaveCookies(s => s.CookieProfile = value.Trim());
    partial void OnCookieFileChanged(string value) => SaveCookies(s => s.CookieFile = value.Trim());
    partial void OnSponsorBlockByDefaultChanged(bool value) => Save(s => s.SponsorBlockByDefault = value);

    partial void OnCloseToTrayChanged(bool value)
    {
        Save(s => s.CloseToTray = value);
        CloseToTrayChanged?.Invoke(value);
    }

    private void SaveCookies(Action<AppSettings> change)
    {
        Save(change);
        _services.Client.Cookies = S.Cookies;
        // A result for another file or mode would mislead.
        _cookieCheck?.Cancel();
        CookieNotes.Clear();
    }

    private void SaveSponsorBlock()
    {
        Save(s => s.SponsorBlockActions = SponsorBlockCategories
            .Where(c => c.Action.Value != SponsorBlockAction.Off)
            .ToDictionary(c => c.Category.Id, c => c.Action.Value));
    }

    [RelayCommand]
    private async Task BrowseCookieFile()
    {
        if (await Dialogs.OpenFileAsync("Choose a cookies.txt file", Dialogs.CookieFiles) is { } path) CookieFile = path;
    }

    /// <summary>
    /// Checks the cookies.txt file the way yt-dlp reads it, then, if it has YouTube sign-in cookies,
    /// asks YouTube whether they still sign you in. The file itself is never changed.
    /// </summary>
    [RelayCommand]
    private async Task VerifyCookieFile()
    {
        _cookieCheck?.Cancel();
        var cts = _cookieCheck = new CancellationTokenSource();
        var path = CookieFile.Trim();
        CookieNotes.Clear();
        CookieCheckStatus = "Reading the file…";
        try
        {
            var report = await Task.Run(() => CookieFileCheck.CheckFile(path, DateTimeOffset.Now));
            if (cts.IsCancellationRequested) return;
            foreach (var note in report.Notes) CookieNotes.Add(new CheckNoteViewModel(note));
            if (!report.HasYouTubeSignIn) return;

            if (_services.Client.Tools.YtDlp is null)
            {
                CookieNotes.Add(new CheckNoteViewModel(new CheckNote(NoteLevel.Info,
                    "Install yt-dlp in Settings > Tools to also check the sign-in with YouTube.")));
                return;
            }
            CookieCheckStatus = "Asking YouTube whether these cookies sign you in…";
            var signIn = await Task.Run(() => _services.Client.TestYouTubeSignInAsync(path, cts.Token));
            if (!cts.IsCancellationRequested) CookieNotes.Add(new CheckNoteViewModel(signIn));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YtDlpException
                                       or System.ComponentModel.Win32Exception)
        {
            if (!cts.IsCancellationRequested)
                CookieNotes.Add(new CheckNoteViewModel(new CheckNote(NoteLevel.Error, "The check failed: " + ex.Message)));
        }
        finally
        {
            if (_cookieCheck == cts)
            {
                _cookieCheck = null;
                CookieCheckStatus = "";
            }
            cts.Dispose();
        }
    }

    // ---- Command templates ----

    private void AddTemplateRow(CommandTemplate t) =>
        CommandTemplates.Add(new CommandTemplateViewModel(t.Name, t.Arguments, SaveTemplates, RemoveTemplate));

    private void RemoveTemplate(CommandTemplateViewModel row)
    {
        CommandTemplates.Remove(row);
        SaveTemplates();
    }

    private void SaveTemplates()
    {
        Save(s => s.CommandTemplates = CommandTemplates
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Arguments))
            .Select(t => new CommandTemplate(t.Name.Trim(), t.Arguments.Trim()))
            .ToList());
    }

    [RelayCommand]
    private void AddTemplate() => AddTemplateRow(new CommandTemplate($"Template {CommandTemplates.Count + 1}", ""));

    [RelayCommand]
    private async Task ImportTemplates()
    {
        var path = await Dialogs.OpenFileAsync("Import command templates", Dialogs.JsonFiles);
        if (path is null) return;
        try
        {
            var incoming = CommandTemplate.Import(await File.ReadAllTextAsync(path));
            var merged = CommandTemplate.Merge(S.CommandTemplates, incoming);
            CommandTemplates.Clear();
            foreach (var t in merged) AddTemplateRow(t);
            SaveTemplates();
            NotificationService.ShowInApp("Templates imported", $"{incoming.Count} templates from {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            NotificationService.ShowInApp("Could not import templates", ex.Message, error: true);
        }
    }

    [RelayCommand]
    private async Task ExportTemplates()
    {
        var path = await Dialogs.SaveFileAsync("Export command templates", "dlpforge-templates.json", Dialogs.JsonFiles);
        if (path is null) return;
        try
        {
            await File.WriteAllTextAsync(path, CommandTemplate.Export(S.CommandTemplates));
            NotificationService.ShowInApp("Templates exported", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NotificationService.ShowInApp("Could not export templates", ex.Message, error: true);
        }
    }

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
        var current = which == "audio" ? AudioFolder : VideoFolder;
        var title = which == "audio" ? "Folder for audio downloads" : "Folder for video downloads";
        if (await Dialogs.PickFolderAsync(title, current) is not { } path) return;
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

/// <summary>One SponsorBlock category row in Settings: off, mark as chapter, or remove.</summary>
public sealed partial class SponsorBlockCategoryViewModel : ViewModelBase
{
    private readonly Action _changed;
    public SponsorBlock.Category Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public IReadOnlyList<Choice<SponsorBlockAction>> Choices { get; }

    [ObservableProperty] private Choice<SponsorBlockAction> _action;

    public SponsorBlockCategoryViewModel(SponsorBlock.Category category, SponsorBlockAction action, Action changed)
    {
        Category = category;
        _changed = changed;
        var choices = new List<Choice<SponsorBlockAction>>
        {
            new(SponsorBlockAction.Off, "Off"),
            new(SponsorBlockAction.Mark, "Mark as chapter"),
        };
        if (category.CanRemove) choices.Add(new(SponsorBlockAction.Remove, "Remove"));
        Choices = choices;
        _action = choices.FirstOrDefault(c => c.Value == action) ?? choices[0];
    }

    partial void OnActionChanged(Choice<SponsorBlockAction> value) => _changed();
}

/// <summary>One line of a cookie check result: a coloured mark and the text.</summary>
public sealed class CheckNoteViewModel(CheckNote note)
{
    public string Text => note.Text;
    public string Mark => note.Level switch
    {
        NoteLevel.Ok => "✓",
        NoteLevel.Warning => "!",
        NoteLevel.Error => "✕",
        _ => "•",
    };
    public bool IsOk => note.Level == NoteLevel.Ok;
    public bool IsWarning => note.Level == NoteLevel.Warning;
    public bool IsError => note.Level == NoteLevel.Error;
}

/// <summary>One editable command template row in Settings.</summary>
public sealed partial class CommandTemplateViewModel(string name, string arguments, Action changed, Action<CommandTemplateViewModel> remove)
    : ViewModelBase
{
    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _arguments = arguments;

    partial void OnNameChanged(string value) => changed();
    partial void OnArgumentsChanged(string value) => changed();

    [RelayCommand]
    private void Remove() => remove(this);
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
