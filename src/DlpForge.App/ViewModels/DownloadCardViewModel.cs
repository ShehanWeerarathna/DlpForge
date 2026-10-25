using System.Collections.ObjectModel;
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

/// <summary>Where the card's content came from. Lists (playlist, search, import) show the item grid.</summary>
public enum CardSource { Link, Playlist, Search, Import }

/// <summary>The card shown after inspecting a link: what it is, how to download it, and (for lists) which items.</summary>
public sealed partial class DownloadCardViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<bool> _close;
    private bool _folderEdited;
    private bool _syncingCutText;

    public MediaInfo Info { get; }
    public CardSource Source { get; }
    public string Title => Info.Title;
    public string? Thumbnail => Info.Thumbnail ?? Info.Entries.FirstOrDefault()?.Thumbnail;
    public bool IsPlaylist => Info.IsPlaylist;
    public bool IsSingle => !Info.IsPlaylist;

    /// <summary>Only a real playlist gets the playlist file name template; search results and imports are separate videos.</summary>
    public bool UsesPlaylistTemplate => Source == CardSource.Playlist;

    public string KindLabel => Source switch
    {
        CardSource.Search => "Search results",
        CardSource.Import => "Imported list",
        CardSource.Playlist => "Playlist",
        _ => "",
    };
    public bool HasKindLabel => KindLabel.Length > 0;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Info.Uploader) && Source != CardSource.Search) parts.Add(Info.Uploader);
            if (Info.IsPlaylist) parts.Add(Info.Entries.Count == 1 ? "1 item" : $"{Info.Entries.Count} items");
            else if (Formatting.Duration(Info.DurationSeconds) is { Length: > 0 } d) parts.Add(d);
            if (!string.IsNullOrEmpty(Info.Extractor) && Source is CardSource.Link or CardSource.Playlist) parts.Add(Info.Extractor);
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

    // ---- Edit metadata (single videos) ----

    private readonly string _originalArtist;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FilenamePreview), nameof(IsMetadataEdited))] private string _editTitle;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsMetadataEdited))] private string _editArtist;
    public bool IsMetadataEdited => TitleOverride != null || ArtistOverride != null;
    private string? TitleOverride => IsSingle && !string.IsNullOrWhiteSpace(EditTitle) && EditTitle.Trim() != Info.Title ? EditTitle.Trim() : null;
    private string? ArtistOverride => IsSingle && !string.IsNullOrWhiteSpace(EditArtist) && EditArtist.Trim() != _originalArtist ? EditArtist.Trim() : null;

    [RelayCommand]
    private void ResetMetadata()
    {
        EditTitle = Info.Title;
        EditArtist = _originalArtist;
    }

    // ---- Cut (single videos with a known length) ----

    public double Duration => Info.DurationSeconds ?? 0;
    public bool CanCut => IsSingle && Duration > 1;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CutSummary), nameof(DownloadLabel), nameof(FilenamePreview))] private bool _isCutEnabled;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CutSummary), nameof(DownloadLabel), nameof(FilenamePreview))] private double _cutStart;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CutSummary), nameof(DownloadLabel), nameof(FilenamePreview))] private double _cutEnd;
    [ObservableProperty] private string _cutStartText = "0:00";
    [ObservableProperty] private string _cutEndText = "0:00";
    [ObservableProperty] private bool _forceKeyframes;
    public ObservableCollection<SegmentViewModel> Segments { get; } = [];
    public ObservableCollection<ChapterViewModel> Chapters { get; } = [];
    public bool HasChapters => Chapters.Count > 0;
    public bool HasSegments => Segments.Count > 0;
    public IReadOnlyList<double> ChapterMarks => Info.Chapters.Select(c => c.Start).Where(s => s > 0).ToList();

    /// <summary>Parts drawn on the timeline besides the one being edited.</summary>
    public IReadOnlyList<TimeRange> TimelineRanges =>
        Segments.Select(s => s.Range).Concat(Chapters.Where(c => c.IsChecked).Select(c => c.Chapter.Range)).ToList();

    /// <summary>
    /// The parts to download: everything highlighted on the timeline. That is the added parts, the ticked
    /// chapters, and the range being edited, unless it repeats an added part or is still the whole video
    /// while other parts are picked.
    /// </summary>
    public IReadOnlyList<(TimeRange Range, string Label)> Parts
    {
        get
        {
            if (!CanCut || !IsCutEnabled) return [];
            var parts = Segments.Select(s => (s.Range, s.Range.Label))
                .Concat(Chapters.Where(c => c.IsChecked).Select(c => (c.Chapter.Range, $"{c.Chapter.Number:00} {c.Chapter.Title}")))
                .ToList();
            var current = new TimeRange(CutStart, CutEnd);
            bool whole = CutStart <= 0 && CutEnd >= Duration;
            if (CutEnd > CutStart && !parts.Any(p => p.Range == current) && (parts.Count == 0 || !whole))
                parts.Add((current, current.Label));
            return parts;
        }
    }

    public string CutSummary
    {
        get
        {
            var parts = Parts;
            if (parts.Count == 0) return "Pick a part with the handles or the boxes.";
            if (parts.Count == 1)
                return $"Downloads {parts[0].Range.Label} ({Formatting.Clock(parts[0].Range.Length)} long) as its own file.";
            return $"Downloads {parts.Count} parts, each as its own file ({Formatting.Clock(parts.Sum(p => p.Range.Length))} in total).";
        }
    }

    partial void OnCutStartChanged(double value) => SyncCutText();
    partial void OnCutEndChanged(double value) => SyncCutText();

    private void SyncCutText()
    {
        if (_syncingCutText) return;
        _syncingCutText = true;
        CutStartText = Formatting.Clock(CutStart);
        CutEndText = Formatting.Clock(CutEnd);
        _syncingCutText = false;
    }

    partial void OnCutStartTextChanged(string value)
    {
        if (_syncingCutText || Formatting.ParseClock(value) is not double s) return;
        _syncingCutText = true;
        CutStart = Math.Clamp(s, 0, Duration);
        _syncingCutText = false;
    }

    partial void OnCutEndTextChanged(string value)
    {
        if (_syncingCutText || Formatting.ParseClock(value) is not double s) return;
        _syncingCutText = true;
        CutEnd = Math.Clamp(s, 0, Duration);
        _syncingCutText = false;
    }

    [RelayCommand]
    private void AddSegment()
    {
        if (CutEnd <= CutStart)
        {
            Error = "The end of a part must be after its start.";
            return;
        }
        var range = new TimeRange(CutStart, CutEnd);
        if (Segments.Any(s => s.Range == range)) return;
        Segments.Add(new SegmentViewModel(range, RemoveSegment));
        Error = null;
        CutChanged();
    }

    private void RemoveSegment(SegmentViewModel s)
    {
        Segments.Remove(s);
        CutChanged();
    }

    private void CutChanged()
    {
        OnPropertyChanged(nameof(TimelineRanges));
        OnPropertyChanged(nameof(HasSegments));
        OnPropertyChanged(nameof(CutSummary));
        OnPropertyChanged(nameof(DownloadLabel));
        OnPropertyChanged(nameof(FilenamePreview));
    }

    // ---- Split, SponsorBlock ----

    [ObservableProperty] private bool _splitChapters;
    public bool CanSplitChapters => !IsSingle || Info.ChapterCount > 0;
    [ObservableProperty] private bool _useSponsorBlock;
    public string SponsorBlockSummary { get; }

    // ---- Schedule ----

    [ObservableProperty] private bool _isScheduled;
    [ObservableProperty] private DateTimeOffset? _scheduleDate;
    [ObservableProperty] private TimeSpan? _scheduleTime;

    public DateTimeOffset? ScheduledAt =>
        IsScheduled && ScheduleDate is { } d && ScheduleTime is { } t
            ? new DateTimeOffset(d.Date + t, TimeZoneInfo.Local.GetUtcOffset(d.Date + t))
            : null;

    // ---- Extra arguments ----

    public IReadOnlyList<CommandTemplate> CommandTemplates { get; }
    [ObservableProperty] private CommandTemplate? _selectedTemplate;
    [ObservableProperty] private string _extraArguments = "";
    public bool HasExtraArguments => !string.IsNullOrWhiteSpace(ExtraArguments);

    partial void OnSelectedTemplateChanged(CommandTemplate? value)
    {
        if (value != null) ExtraArguments = value.Arguments;
    }

    partial void OnExtraArgumentsChanged(string value) => OnPropertyChanged(nameof(HasExtraArguments));

    // ---- File name ----

    public string FilenamePreview
    {
        get
        {
            var values = new Dictionary<string, string>(TemplatePreview.SampleValues)
            {
                ["title"] = Info.IsPlaylist ? Info.Entries.FirstOrDefault()?.Title ?? "Title" : EditTitle,
                ["id"] = Info.IsPlaylist ? Info.Entries.FirstOrDefault()?.Id ?? "id" : Info.Id,
                ["ext"] = IsAudio ? (AudioFormat == "best" ? "m4a" : AudioFormat) : VideoContainer,
                ["uploader"] = Info.Uploader ?? "Uploader",
                ["channel"] = Info.Uploader ?? "Channel",
                ["playlist"] = Info.Title,
                ["playlist_title"] = Info.Title,
                ["playlist_index"] = "1",
            };
            var template = Parts is { Count: > 0 } parts ? ArgumentBuilder.AppendToTemplate(FilenameTemplate, $" ({parts[0].Label})") : FilenameTemplate;
            return TemplatePreview.Render(template, values);
        }
    }

    // ---- List selection ----

    public ObservableCollection<PlaylistEntryViewModel> Entries { get; } = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(DownloadLabel))] private int _checkedCount;

    public string DownloadLabel
    {
        get
        {
            if (IsPlaylist) return CheckedCount == 1 ? "Download 1 item" : $"Download {CheckedCount} items";
            var n = Parts.Count;
            return n > 1 ? $"Download {n} parts" : n == 1 ? "Download part" : "Download";
        }
    }

    public DownloadCardViewModel(MediaInfo info, AppServices services, Action<bool> close, CardSource source = CardSource.Link)
    {
        Info = info;
        Source = info.IsPlaylist && source == CardSource.Link ? CardSource.Playlist : source;
        _services = services;
        _close = close;

        var s = services.Settings;
        _isAudio = s.DefaultType == DownloadType.Audio;
        _selectedQuality = QualityChoices.FirstOrDefault(q => q.Value == s.DefaultQuality) ?? QualityChoices[0];
        _audioFormat = s.DefaultAudioFormat;
        _videoContainer = s.DefaultVideoContainer;
        _outputFolder = _isAudio ? s.AudioFolder : s.VideoFolder;
        _filenameTemplate = UsesPlaylistTemplate ? s.PlaylistFilenameTemplate : s.FilenameTemplate;
        _embedThumbnail = s.EmbedThumbnail;
        _embedMetadata = s.EmbedMetadata;
        _embedChapters = s.EmbedChapters;
        _embedSubtitles = s.EmbedSubtitles;
        _subtitleLanguages = s.SubtitleLanguages;

        _editTitle = info.Title;
        _originalArtist = info.Artist ?? info.Uploader ?? "";
        _editArtist = _originalArtist;

        _cutEnd = Duration;
        _cutEndText = Formatting.Clock(Duration);
        foreach (var c in info.Chapters)
        {
            var vm = new ChapterViewModel(c);
            vm.PropertyChanged += (_, _) => CutChanged();
            Chapters.Add(vm);
        }

        _useSponsorBlock = s.SponsorBlockByDefault;
        var (mark, remove) = SponsorBlock.ToArguments(s.SponsorBlockActions);
        SponsorBlockSummary = (mark, remove) switch
        {
            ("", "") => "No categories are set up. Choose them in Settings > SponsorBlock.",
            _ => string.Join("  ·  ", new[] { remove.Length > 0 ? "Removes " + remove.Replace(",", ", ") : null,
                                              mark.Length > 0 ? "Marks " + mark.Replace(",", ", ") : null }.Where(x => x != null)),
        };

        // Default schedule: the next full hour.
        var next = DateTime.Now.AddHours(1);
        _scheduleDate = new DateTimeOffset(next.Date);
        _scheduleTime = new TimeSpan(next.Hour, 0, 0);

        CommandTemplates = s.CommandTemplates.ToList();

        // Search results start unticked: the user picks what they want. Playlists and imports start ticked.
        bool tick = Source != CardSource.Search;
        foreach (var e in info.Entries)
        {
            var vm = new PlaylistEntryViewModel(e) { IsChecked = tick };
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
    private async Task BrowseFolder()
    {
        if (await Dialogs.PickFolderAsync("Choose download folder", OutputFolder) is { } path)
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
        if (IsScheduled && (ScheduledAt is not { } at || at <= DateTimeOffset.Now))
        {
            Error = "The start time has passed. Pick a later time, or untick \"Start at a set time\".";
            return;
        }
        var parts = Parts;
        if (CanCut && IsCutEnabled && parts.Count == 0)
        {
            Error = "Pick the part to download: the end must be after the start.";
            return;
        }

        var options = BuildOptions();
        RememberChoices(options);
        var queue = _services.Queue;
        var when = ScheduledAt;
        if (IsSingle)
        {
            var title = TitleOverride ?? Info.Title;
            if (parts.Count == 0)
            {
                queue.Enqueue(Info.Url, title, options, Thumbnail, Info.Uploader, Info.DurationSeconds, when);
            }
            else
            {
                foreach (var (range, label) in parts)
                {
                    var perPart = options with
                    {
                        Section = range,
                        FilenameTemplate = ArgumentBuilder.AppendToTemplate(options.FilenameTemplate, $" ({label})"),
                    };
                    queue.Enqueue(Info.Url, $"{title} ({label})", perPart, Thumbnail, Info.Uploader, range.Length, when);
                }
            }
        }
        else
        {
            foreach (var e in Entries.Where(e => e.IsChecked))
            {
                var perItem = UsesPlaylistTemplate
                    ? options with { FilenameTemplate = TemplatePreview.FillPlaylistFields(options.FilenameTemplate, Info.Title, e.Entry.Index) }
                    : options;
                queue.Enqueue(e.Entry.Url, e.Entry.Title, perItem, e.Entry.Thumbnail, e.Entry.Uploader ?? Info.Uploader,
                    e.Entry.DurationSeconds, when);
            }
        }
        _close(true);
    }

    /// <summary>Saves what was picked, for quick download and imports.</summary>
    private void RememberChoices(DownloadOptions options)
    {
        _services.Settings.LastOptions = options with
        {
            Section = null,
            TitleOverride = null,
            ArtistOverride = null,
            FilenameTemplate = _services.Settings.FilenameTemplate,
        };
        _services.SaveSettings();
    }

    [RelayCommand]
    private void Close() => _close(false);

    internal DownloadOptions BuildOptions()
    {
        bool custom = UseSpecificFormat && SelectedFormat != null && IsSingle;
        var (mark, remove) = UseSponsorBlock ? SponsorBlock.ToArguments(_services.Settings.SponsorBlockActions) : ("", "");
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
            ForceKeyframesAtCuts = IsCutEnabled && ForceKeyframes,
            SplitChapters = SplitChapters && CanSplitChapters,
            TitleOverride = TitleOverride,
            ArtistOverride = ArtistOverride,
            SponsorBlockMark = mark,
            SponsorBlockRemove = remove,
            ExtraArguments = ExtraArguments.Trim(),
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

/// <summary>A part added on the cut timeline.</summary>
public sealed partial class SegmentViewModel(TimeRange range, Action<SegmentViewModel> remove) : ViewModelBase
{
    public TimeRange Range { get; } = range;
    public string Label => $"{Range.Label}  ({Formatting.Clock(Range.Length)})";

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>A chapter that can be ticked to download just that chapter.</summary>
public sealed partial class ChapterViewModel(ChapterInfo chapter) : ViewModelBase
{
    public ChapterInfo Chapter { get; } = chapter;
    public string Title => $"{Chapter.Number}. {Chapter.Title}";
    public string Times => Chapter.Range.Label;

    [ObservableProperty] private bool _isChecked;
}
