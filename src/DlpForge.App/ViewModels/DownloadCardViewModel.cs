using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DlpForge.App.Services;
using DlpForge.Core.Models;
using DlpForge.Core.YtDlp;

namespace DlpForge.App.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The card shown after inspecting a link: what it is, how to download it, and (for playlists) which items.</summary>
public sealed partial class DownloadCardViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<bool> _close;
    private bool _folderEdited;

    public MediaInfo Info { get; }
    public string Title => Info.Title;
    public string? Thumbnail => Info.Thumbnail ?? Info.Entries.FirstOrDefault()?.Thumbnail;
    public bool IsPlaylist => Info.IsPlaylist;
    public bool IsSingle => !Info.IsPlaylist;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Info.Uploader)) parts.Add(Info.Uploader);
            if (Info.IsPlaylist) parts.Add($"{Info.Entries.Count} items");
            else if (Formatting.Duration(Info.DurationSeconds) is { Length: > 0 } d) parts.Add(d);
            if (!string.IsNullOrEmpty(Info.Extractor)) parts.Add(Info.Extractor);
            if (Info.ChapterCount > 0) parts.Add($"{Info.ChapterCount} chapters");
            return string.Join("  ·  ", parts);
        }
    }

    public IReadOnlyList<Choice<QualityPreset>> QualityChoices { get; } =
    [
        new(QualityPreset.Best, "Best available"),
        new(QualityPreset.P2160, "Up to 2160p (4K)"),
        new(QualityPreset.P1440, "Up to 1440p"),
        new(QualityPreset.P1080, "Up to 1080p"),
        new(QualityPreset.P720, "Up to 720p"),
        new(QualityPreset.P480, "Up to 480p"),
        new(QualityPreset.P360, "Up to 360p"),
    ];

    public IReadOnlyList<string> AudioFormats => Defaults.AudioFormats;
    public IReadOnlyList<string> VideoContainers => Defaults.VideoContainers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideo), nameof(VisibleFormats), nameof(ShowSubtitleOption))]
    private bool _isAudio;

    [ObservableProperty] private Choice<QualityPreset> _selectedQuality;
    [ObservableProperty] private string _audioFormat;
    [ObservableProperty] private string _videoContainer;

    /// <summary>When on, the selected row of the format table is downloaded instead of the quality preset.</summary>
    [ObservableProperty] private bool _useSpecificFormat;
    [ObservableProperty] private FormatInfo? _selectedFormat;

    [ObservableProperty] private string _outputFolder;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FilenamePreview))] private string _filenameTemplate;

    [ObservableProperty] private bool _embedThumbnail;
    [ObservableProperty] private bool _embedMetadata;
    [ObservableProperty] private bool _embedChapters;
    [ObservableProperty] private bool _embedSubtitles;
    [ObservableProperty] private string _subtitleLanguages;

    [ObservableProperty] private string? _error;

    public bool IsVideo => !IsAudio;
    public bool ShowSubtitleOption => !IsAudio;
    public bool HasFormats => Info.Formats.Count > 0;
    public string SubtitlesAvailable => Info.SubtitleLanguages.Count == 0
        ? "No subtitles listed for this video"
        : "Available: " + string.Join(", ", Info.SubtitleLanguages.Take(12)) + (Info.SubtitleLanguages.Count > 12 ? "…" : "");

    /// <summary>The format table, best first, filtered to what makes sense for the chosen type.</summary>
    public IEnumerable<FormatInfo> VisibleFormats => Info.Formats
        .Where(f => IsAudio ? f.HasAudio && !f.HasVideo : f.HasVideo)
        .OrderByDescending(f => f.Height ?? 0)
        .ThenByDescending(f => f.Tbr ?? 0);

    public string FilenamePreview
    {
        get
        {
            var values = new Dictionary<string, string>(TemplatePreview.SampleValues)
            {
                ["title"] = Info.IsPlaylist ? Info.Entries.FirstOrDefault()?.Title ?? "Title" : Info.Title,
                ["id"] = Info.IsPlaylist ? Info.Entries.FirstOrDefault()?.Id ?? "id" : Info.Id,
                ["ext"] = IsAudio ? (AudioFormat == "best" ? "m4a" : AudioFormat) : VideoContainer,
                ["uploader"] = Info.Uploader ?? "Uploader",
                ["channel"] = Info.Uploader ?? "Channel",
                ["playlist"] = Info.Title,
                ["playlist_title"] = Info.Title,
                ["playlist_index"] = "1",
            };
            return TemplatePreview.Render(FilenameTemplate, values);
        }
    }

    // Playlist selection
    public ObservableCollection<PlaylistEntryViewModel> Entries { get; } = [];
    [ObservableProperty] private int _checkedCount;

    public DownloadCardViewModel(MediaInfo info, AppServices services, Action<bool> close)
    {
        Info = info;
        _services = services;
        _close = close;

        var s = services.Settings;
        _isAudio = s.DefaultType == DownloadType.Audio;
        _selectedQuality = QualityChoices.FirstOrDefault(q => q.Value == s.DefaultQuality) ?? QualityChoices[0];
        _audioFormat = s.DefaultAudioFormat;
        _videoContainer = s.DefaultVideoContainer;
        _outputFolder = _isAudio ? s.AudioFolder : s.VideoFolder;
        _filenameTemplate = info.IsPlaylist ? s.PlaylistFilenameTemplate : s.FilenameTemplate;
        _embedThumbnail = s.EmbedThumbnail;
        _embedMetadata = s.EmbedMetadata;
        _embedChapters = s.EmbedChapters;
        _embedSubtitles = s.EmbedSubtitles;
        _subtitleLanguages = s.SubtitleLanguages;

        foreach (var e in info.Entries)
        {
            var vm = new PlaylistEntryViewModel(e);
            vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(PlaylistEntryViewModel.IsChecked)) UpdateChecked(); };
            Entries.Add(vm);
        }
        UpdateChecked();
    }

    private void UpdateChecked()
    {
        CheckedCount = Entries.Count(e => e.IsChecked);
        DownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAudioChanged(bool value)
    {
        if (!_folderEdited)
            OutputFolder = value ? _services.Settings.AudioFolder : _services.Settings.VideoFolder;
        SelectedFormat = null;
        UseSpecificFormat = false;
        OnPropertyChanged(nameof(FilenamePreview));
    }

    partial void OnAudioFormatChanged(string value) => OnPropertyChanged(nameof(FilenamePreview));
    partial void OnVideoContainerChanged(string value) => OnPropertyChanged(nameof(FilenamePreview));

    partial void OnSelectedFormatChanged(FormatInfo? value)
    {
        if (value != null) UseSpecificFormat = true;
    }

    [RelayCommand]
    private void SetType(string type) => IsAudio = type == "audio";

    [RelayCommand]
    private async Task BrowseFolder(Control? anchor)
    {
        var top = anchor is null ? null : TopLevel.GetTopLevel(anchor);
        if (top is null) return;
        var start = await top.StorageProvider.TryGetFolderFromPathAsync(OutputFolder);
        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose download folder",
            SuggestedStartLocation = start,
        });
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            OutputFolder = path;
            _folderEdited = true;
        }
    }

    [RelayCommand]
    private void SelectAll() { foreach (var e in Entries) e.IsChecked = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var e in Entries) e.IsChecked = false; }

    [RelayCommand]
    private void InvertSelection() { foreach (var e in Entries) e.IsChecked = !e.IsChecked; }

    /// <summary>Ticks or unticks every row highlighted in the list (Shift/Ctrl multi-select).</summary>
    [RelayCommand]
    private void ToggleHighlighted(System.Collections.IList? highlighted)
    {
        if (highlighted is null || highlighted.Count == 0) return;
        var rows = highlighted.OfType<PlaylistEntryViewModel>().ToList();
        bool target = !rows.All(r => r.IsChecked);
        foreach (var r in rows) r.IsChecked = target;
    }

    private bool CanDownload() => IsSingle || CheckedCount > 0;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download()
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            Error = "Choose a download folder first.";
            return;
        }

        var options = BuildOptions();
        var queue = _services.Queue;
        if (IsSingle)
        {
            queue.Enqueue(Info.Url, Info.Title, options, Thumbnail, Info.Uploader, Info.DurationSeconds);
        }
        else
        {
            foreach (var e in Entries.Where(e => e.IsChecked))
            {
                var perItem = options with
                {
                    FilenameTemplate = TemplatePreview.FillPlaylistFields(options.FilenameTemplate, Info.Title, e.Entry.Index),
                };
                queue.Enqueue(e.Entry.Url, e.Entry.Title, perItem, e.Entry.Thumbnail, e.Entry.Uploader ?? Info.Uploader,
                    e.Entry.DurationSeconds);
            }
        }
        _close(true);
    }

    [RelayCommand]
    private void Close() => _close(false);

    internal DownloadOptions BuildOptions()
    {
        bool custom = UseSpecificFormat && SelectedFormat != null && IsSingle;
        return new DownloadOptions
        {
            Type = IsAudio ? DownloadType.Audio : DownloadType.Video,
            Quality = custom ? QualityPreset.Custom : SelectedQuality.Value,
            CustomFormat = custom ? ArgumentBuilder.SelectorFor(SelectedFormat!) : null,
            AudioFormat = AudioFormat,
            VideoContainer = VideoContainer,
            OutputFolder = OutputFolder,
            FilenameTemplate = FilenameTemplate,
            EmbedThumbnail = EmbedThumbnail,
            EmbedMetadata = EmbedMetadata,
            EmbedChapters = EmbedChapters,
            EmbedSubtitles = EmbedSubtitles && !IsAudio,
            SubtitleLanguages = SubtitleLanguages,
        };
    }
}

public sealed partial class PlaylistEntryViewModel(PlaylistEntry entry) : ViewModelBase
{
    public PlaylistEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public string Index => Entry.Index.ToString();
    public string? Thumbnail => Entry.Thumbnail;
    public string Details => string.Join("  ·  ",
        new[] { Entry.Uploader, Formatting.Duration(Entry.DurationSeconds) }.Where(s => !string.IsNullOrEmpty(s)));

    [ObservableProperty] private bool _isChecked = true;
}
