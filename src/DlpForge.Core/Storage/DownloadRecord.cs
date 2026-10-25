using DlpForge.Core.Models;

namespace DlpForge.Core.Storage;

/// <summary>One row in the downloads table: a queue entry while it runs, a history entry once it is done.</summary>
public sealed record DownloadRecord
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string Title { get; init; } = "";
    public string? Uploader { get; init; }
    public string? Thumbnail { get; init; }
    public double? DurationSeconds { get; init; }
    public required DownloadOptions Options { get; init; }
    public DownloadStatus Status { get; init; }
    public string? FilePath { get; init; }
    public long? SizeBytes { get; init; }
    public string? Error { get; init; }
    public string? LogPath { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>Removed from the Downloads view with "Clear finished" but still listed in History.</summary>
    public bool HiddenFromQueue { get; init; }
    /// <summary>A queued download waits until this time before it starts. Null starts as soon as there is room.</summary>
    public DateTimeOffset? ScheduledAt { get; init; }
}
