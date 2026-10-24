using System.Collections.ObjectModel;
using DlpForge.Core.Models;
using DlpForge.Core.Storage;
using DlpForge.Core.YtDlp;

namespace DlpForge.Core.Queue;

/// <summary>
/// Owns every download: starts queued items up to the parallel limit, tracks progress,
/// handles pause, resume, cancel and retry, and persists state to the store.
/// All public members must be called on the UI thread; worker callbacks are marshalled back to it.
/// </summary>
public sealed class DownloadQueue
{
    private readonly YtDlpClient _client;
    private readonly DownloadStore _store;
    private readonly AppPaths _paths;
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<string, Run> _running = [];

    private sealed class Run
    {
        public required CancellationTokenSource Cts { get; init; }
        /// <summary>The status to land in when the run is stopped on purpose (Paused or Cancelled).</summary>
        public DownloadStatus StopAs { get; set; } = DownloadStatus.Cancelled;
    }

    public ObservableCollection<DownloadItem> Items { get; } = [];
    public int MaxConcurrent { get; set; } = 3;

    public event Action<DownloadItem>? ItemCompleted;
    public event Action<DownloadItem>? ItemFailed;
    /// <summary>Raised when overall progress changes: (active count, average percent 0-100).</summary>
    public event Action<int, double>? OverallProgressChanged;

    public DownloadQueue(YtDlpClient client, DownloadStore store, AppPaths paths, SynchronizationContext uiContext)
    {
        _client = client;
        _store = store;
        _paths = paths;
        _ui = uiContext;
    }

    /// <summary>Loads saved items. Downloads that were running when the app closed go back into the queue.</summary>
    public void Restore()
    {
        foreach (var r in _store.LoadAll())
        {
            var item = new DownloadItem(r);
            if (item.Status is DownloadStatus.Downloading or DownloadStatus.Processing)
            {
                item.Status = DownloadStatus.Queued;
                Save(item);
            }
            Items.Add(item);
        }
        Pump();
    }

    public DownloadItem Enqueue(string url, string title, DownloadOptions options,
        string? thumbnail = null, string? uploader = null, double? duration = null)
    {
        var item = new DownloadItem(new DownloadRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Thumbnail = thumbnail,
            Uploader = uploader,
            DurationSeconds = duration,
            Options = options,
            Status = DownloadStatus.Queued,
            CreatedAt = DateTimeOffset.Now,
        });
        item.LogPath = Path.Combine(_paths.LogsDir, item.Id + ".log");
        Items.Add(item);
        Save(item);
        Pump();
        return item;
    }

    public void Pause(DownloadItem item)
    {
        if (_running.TryGetValue(item.Id, out var run))
        {
            run.StopAs = DownloadStatus.Paused;
            run.Cts.Cancel();
        }
        else if (item.Status == DownloadStatus.Queued)
        {
            item.Status = DownloadStatus.Paused;
            Save(item);
        }
    }

    /// <summary>Resume re-runs yt-dlp, which continues from the .part file it left behind.</summary>
    public void Resume(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Paused) return;
        item.Status = DownloadStatus.Queued;
        Save(item);
        Pump();
    }

    public void Cancel(DownloadItem item)
    {
        if (_running.TryGetValue(item.Id, out var run))
        {
            run.StopAs = DownloadStatus.Cancelled;
            run.Cts.Cancel();
        }
        else if (item.CanCancel)
        {
            item.Status = DownloadStatus.Cancelled;
            Save(item);
        }
    }

    public void Retry(DownloadItem item)
    {
        if (!item.CanRetry) return;
        item.Error = null;
        item.Percent = 0;
        item.StageText = "";
        item.HiddenFromQueue = false;
        item.Status = DownloadStatus.Queued;
        Save(item);
        Pump();
    }

    public void PauseAll()
    {
        foreach (var i in Items.Where(i => i.CanPause).ToList()) Pause(i);
    }

    public void ResumeAll()
    {
        foreach (var i in Items.Where(i => i.CanResume).ToList()) Resume(i);
    }

    /// <summary>Hides finished items from the Downloads view; they stay in History.</summary>
    public void ClearFinished()
    {
        foreach (var i in Items.Where(i => !i.HiddenFromQueue &&
                     i.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled))
        {
            i.HiddenFromQueue = true;
            Save(i);
        }
    }

    /// <summary>Deletes an item from the queue and history (not the downloaded file).</summary>
    public void Remove(DownloadItem item)
    {
        if (_running.ContainsKey(item.Id)) Cancel(item);
        Items.Remove(item);
        var id = item.Id;
        Persist(s => s.Delete(id));
    }

    public void HideFromQueue(DownloadItem item)
    {
        if (item.IsActive || item.Status == DownloadStatus.Queued) return;
        item.HiddenFromQueue = true;
        Save(item);
    }

    /// <summary>Kills all running downloads; used on exit. They resume next launch.</summary>
    public void StopAllForExit()
    {
        foreach (var run in _running.Values)
        {
            run.StopAs = DownloadStatus.Queued;
            run.Cts.Cancel();
        }
    }

    private void Pump()
    {
        while (_running.Count < Math.Max(1, MaxConcurrent))
        {
            var next = Items.FirstOrDefault(i => i.Status == DownloadStatus.Queued && !_running.ContainsKey(i.Id));
            if (next is null) break;
            Start(next);
        }
        ReportOverall();
    }

    private void Start(DownloadItem item)
    {
        var run = new Run { Cts = new CancellationTokenSource() };
        _running[item.Id] = run;
        item.Status = DownloadStatus.Downloading;
        item.StageText = "Starting";
        item.IsIndeterminate = true;
        item.Error = null;
        item.LogPath ??= Path.Combine(_paths.LogsDir, item.Id + ".log");
        Save(item);

        var url = item.Url;
        var options = item.Options;
        var logPath = item.LogPath;
        _ = Task.Run(() => Execute(item, url, options, logPath, run));
    }

    private async Task Execute(DownloadItem item, string url, DownloadOptions options, string logPath, Run run)
    {
        DownloadResult? result = null;
        Exception? failure = null;
        DownloadProgress? pending = null;
        var progressLock = new Lock();
        bool flushScheduled = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(options.OutputFolder))
                Directory.CreateDirectory(options.OutputFolder);

            await using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
            await log.WriteLineAsync($"[dlpforge] {DateTimeOffset.Now:O} starting {url}");
            var logLock = new Lock();

            result = await _client.DownloadAsync(url, options,
                p =>
                {
                    // Coalesce progress ticks so the UI thread sees at most one update per post.
                    lock (progressLock)
                    {
                        pending = p;
                        if (flushScheduled) return;
                        flushScheduled = true;
                    }
                    _ui.Post(_ =>
                    {
                        DownloadProgress? latest;
                        lock (progressLock) { latest = pending; flushScheduled = false; }
                        if (latest != null && _running.ContainsKey(item.Id)) item.ApplyProgress(latest);
                        ReportOverall();
                    }, null);
                },
                line => { lock (logLock) log.WriteLine(line); },
                run.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose; handled below.
        }
        catch (Exception ex)
        {
            failure = ex;
            try { File.AppendAllText(logPath, $"[dlpforge] error: {ex}\n"); } catch (IOException) { }
        }

        _ui.Post(_ => Finish(item, run, result, failure), null);
    }

    private void Finish(DownloadItem item, Run run, DownloadResult? result, Exception? failure)
    {
        _running.Remove(item.Id);
        item.IsIndeterminate = false;
        item.SpeedText = item.EtaText = "";

        if (run.Cts.IsCancellationRequested)
        {
            item.Status = run.StopAs;
            item.StageText = "";
        }
        else if (failure != null)
        {
            item.Status = DownloadStatus.Failed;
            item.Error = failure.Message;
        }
        else if (result is { Success: true })
        {
            item.Status = DownloadStatus.Completed;
            item.Percent = 100;
            item.StageText = "";
            item.CompletedAt = DateTimeOffset.Now;
            item.FilePath = result.FilePath;
            if (result.FilePath != null && File.Exists(result.FilePath))
            {
                item.SizeBytes = new FileInfo(result.FilePath).Length;
                item.SizeText = Formatting.Bytes(item.SizeBytes.Value);
            }
        }
        else
        {
            item.Status = DownloadStatus.Failed;
            item.Error = result?.Error ?? "Unknown error";
        }

        run.Cts.Dispose();
        if (!Items.Contains(item))
        {
            // Removed while it was running: nothing to save or announce.
            Pump();
            return;
        }
        Save(item);

        if (item.Status == DownloadStatus.Completed) ItemCompleted?.Invoke(item);
        else if (item.Status == DownloadStatus.Failed) ItemFailed?.Invoke(item);

        Pump();
    }

    private void ReportOverall()
    {
        var active = Items.Where(i => i.IsActive).ToList();
        OverallProgressChanged?.Invoke(active.Count, active.Count == 0 ? 0 : active.Average(i => i.Percent));
    }

    private Task _writes = Task.CompletedTask;

    /// <summary>Database writes run off the UI thread, one after another so they never land out of order.</summary>
    private void Persist(Action<DownloadStore> write)
    {
        _writes = _writes.ContinueWith(_ => write(_store), TaskScheduler.Default);
    }

    private void Save(DownloadItem item)
    {
        var record = item.ToRecord();
        Persist(s => s.Upsert(record));
    }

    /// <summary>Waits for pending database writes; call before the app exits.</summary>
    public Task FlushAsync() => _writes;
}
