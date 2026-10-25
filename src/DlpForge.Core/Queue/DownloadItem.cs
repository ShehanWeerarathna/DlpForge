using CommunityToolkit.Mvvm.ComponentModel;
using DlpForge.Core.Models;
using DlpForge.Core.Storage;

namespace DlpForge.Core.Queue;

/// <summary>A live download the UI binds to. Property changes are raised on the UI thread by <see cref="DownloadQueue"/>.</summary>
public sealed partial class DownloadItem : ObservableObject
{
    public string Id { get; }
    public string Url { get; }
    public DateTimeOffset CreatedAt { get; }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string? _uploader;
    [ObservableProperty] private string? _thumbnail;
    [ObservableProperty] private double? _durationSeconds;
    [ObservableProperty] private DownloadOptions _options;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(CanPause), nameof(CanResume), nameof(CanCancel),
        nameof(CanRetry), nameof(HasFile), nameof(StatusText), nameof(IsFinished), nameof(IsFailed),
        nameof(IsScheduled), nameof(CanSchedule), nameof(CanStartNow))]
    private DownloadStatus _status;

    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _stageText = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasFile))] private string? _filePath;
    [ObservableProperty] private long? _sizeBytes;
    [ObservableProperty] private string? _logPath;
    [ObservableProperty] private DateTimeOffset? _completedAt;
    [ObservableProperty] private bool _hiddenFromQueue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduled), nameof(StatusText), nameof(ScheduleText), nameof(CanStartNow))]
    private DateTimeOffset? _scheduledAt;

    public DownloadItem(DownloadRecord r)
    {
        Id = r.Id;
        Url = r.Url;
        CreatedAt = r.CreatedAt;
        _title = r.Title;
        _uploader = r.Uploader;
        _thumbnail = r.Thumbnail;
        _durationSeconds = r.DurationSeconds;
        _options = r.Options;
        _status = r.Status;
        _filePath = r.FilePath;
        _sizeBytes = r.SizeBytes;
        _error = r.Error;
        _logPath = r.LogPath;
        _completedAt = r.CompletedAt;
        _hiddenFromQueue = r.HiddenFromQueue;
        _scheduledAt = r.ScheduledAt;
        if (r.Status == DownloadStatus.Completed) _percent = 100;
        if (r.SizeBytes is long s) _sizeText = Formatting.Bytes(s);
    }

    public bool IsActive => Status is DownloadStatus.Downloading or DownloadStatus.Processing;
    public bool IsFinished => Status is DownloadStatus.Completed;
    public bool IsFailed => Status is DownloadStatus.Failed or DownloadStatus.Cancelled;
    public bool CanPause => Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing;
    public bool CanResume => Status is DownloadStatus.Paused;
    public bool CanCancel => Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing or DownloadStatus.Paused;
    public bool CanRetry => Status is DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Completed;
    public bool HasFile => Status == DownloadStatus.Completed && FilePath != null;
    public string DurationText => Formatting.Duration(DurationSeconds);

    public string TypeText
    {
        get
        {
            var text = Options.Type == DownloadType.Audio ? $"Audio · {Options.AudioFormat}" : "Video";
            if (Options.Section is { } s) text += $" · part {s.Label}";
            return text;
        }
    }

    /// <summary>Waiting in the queue for a set time. Becomes plain Queued once the time comes.</summary>
    public bool IsScheduled => Status is DownloadStatus.Queued or DownloadStatus.Paused && ScheduledAt != null;
    public bool CanSchedule => Status is DownloadStatus.Queued or DownloadStatus.Paused;
    public bool CanStartNow => IsScheduled || Status == DownloadStatus.Paused;
    public string ScheduleText => ScheduledAt is { } at ? "Starts " + WhenText(at) : "";

    private static string WhenText(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var day = local.Date == today ? "today"
            : local.Date == today.AddDays(1) ? "tomorrow"
            : local.ToString("d MMM yyyy");
        return $"{day} at {local:HH:mm}";
    }

    public string StatusText => Status switch
    {
        DownloadStatus.Queued when ScheduledAt != null => "Scheduled",
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Downloading => "Downloading",
        DownloadStatus.Processing => "Processing",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Failed => "Failed",
        DownloadStatus.Cancelled => "Cancelled",
        _ => Status.ToString(),
    };

    public DownloadRecord ToRecord() => new()
    {
        Id = Id,
        Url = Url,
        Title = Title,
        Uploader = Uploader,
        Thumbnail = Thumbnail,
        DurationSeconds = DurationSeconds,
        Options = Options,
        Status = Status,
        FilePath = FilePath,
        SizeBytes = SizeBytes,
        Error = Error,
        LogPath = LogPath,
        CreatedAt = CreatedAt,
        CompletedAt = CompletedAt,
        HiddenFromQueue = HiddenFromQueue,
        ScheduledAt = ScheduledAt,
    };

    /// <summary>Fills in what yt-dlp reported when the download started, for items queued straight from a link.</summary>
    internal void ApplyMeta(MediaMeta m)
    {
        if (m.Title is { } t && (string.IsNullOrWhiteSpace(Title) || Title == Url)) Title = t;
        if (Thumbnail is null && m.Thumbnail != null) Thumbnail = m.Thumbnail;
        if (Uploader is null && m.Uploader != null) Uploader = m.Uploader;
        if (DurationSeconds is null && m.DurationSeconds != null)
        {
            DurationSeconds = m.DurationSeconds;
            OnPropertyChanged(nameof(DurationText));
        }
    }

    internal void ApplyProgress(DownloadProgress p)
    {
        if (p.Stage == "processing")
        {
            Status = DownloadStatus.Processing;
            StageText = p.Postprocessor is { } pp ? $"Processing ({Humanize(pp)})" : "Processing";
            IsIndeterminate = true;
            SpeedText = EtaText = "";
            return;
        }

        Status = DownloadStatus.Downloading;
        IsIndeterminate = p.Percent is null;
        if (p.Percent is double pct) Percent = pct;
        SpeedText = p.SpeedBytesPerSecond is double sp ? Formatting.Bytes(sp) + "/s" : "";
        EtaText = p.EtaSeconds is double eta && eta > 0 ? Formatting.Duration(eta) + " left" : "";
        if (p.TotalBytes is long total) SizeText = Formatting.Bytes(total);
        StageText = p.Stage == "finished" ? "Finishing" : "Downloading";
    }

    private static string Humanize(string pp) => pp switch
    {
        "Merger" => "merging",
        "ExtractAudio" => "converting audio",
        "EmbedThumbnail" => "embedding thumbnail",
        "FFmpegMetadata" or "Metadata" => "writing metadata",
        "EmbedSubtitle" => "embedding subtitles",
        "MoveFiles" => "moving files",
        _ => pp,
    };
}
