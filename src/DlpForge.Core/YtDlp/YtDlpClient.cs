using System.Text;
using DlpForge.Core.Models;
using DlpForge.Core.Tools;

namespace DlpForge.Core.YtDlp;

public sealed class YtDlpException(string message) : Exception(message);

public sealed record DownloadResult(int ExitCode, string? FilePath, string? Error)
{
    public bool Success => ExitCode == 0;
}

/// <summary>The four things DlpForge asks yt-dlp to do: inspect, download, report its version, update itself.</summary>
public sealed class YtDlpClient(ToolLocator tools)
{
    public ToolLocator Tools => tools;

    private string Exe => tools.YtDlp
        ?? throw new YtDlpException("yt-dlp is not installed. Open Settings > Tools and click Install.");

    private ArgumentBuilder.ToolOptions ToolOptions => new(tools.FfmpegFolder, tools.Deno);

    public async Task<MediaInfo> InspectAsync(string url, CancellationToken ct = default)
    {
        var stdout = new StringBuilder();
        var errors = new List<string>();
        int code = await ProcessRunner.RunAsync(
            Exe, ArgumentBuilder.ForInspect(url, ToolOptions),
            line => stdout.AppendLine(line),
            line => { if (line.StartsWith("ERROR:")) errors.Add(line["ERROR:".Length..].Trim()); },
            ct).ConfigureAwait(false);

        var json = stdout.ToString().Trim();
        if (code != 0 || json.Length == 0)
            throw new YtDlpException(errors.LastOrDefault() ?? $"yt-dlp exited with code {code}.");
        return MediaInfo.Parse(json, url);
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        Action<DownloadProgress> onProgress,
        Action<string> onLog,
        CancellationToken ct)
    {
        string? finalPath = null;
        string? lastError = null;
        var args = ArgumentBuilder.ForDownload(url, options, ToolOptions);
        onLog("> yt-dlp " + string.Join(' ', args.Select(Quote)));

        void Handle(string line)
        {
            switch (OutputParser.Parse(line))
            {
                case OutputLine.Progress p:
                    onProgress(p.Value);
                    // Keep logs readable: only log stage changes, not every progress tick.
                    if (p.Value.Stage != "downloading") onLog(line);
                    break;
                case OutputLine.FinalPath f:
                    finalPath = f.Path;
                    onLog(line);
                    break;
                case OutputLine.Error e:
                    lastError = e.Message;
                    onLog(line);
                    break;
                default:
                    onLog(line);
                    break;
            }
        }

        int code = await ProcessRunner.RunAsync(Exe, args, Handle, Handle, ct).ConfigureAwait(false);
        onLog($"[dlpforge] yt-dlp exited with code {code}");
        return new DownloadResult(code, finalPath, code == 0 ? null : lastError ?? $"yt-dlp exited with code {code}.");
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        if (tools.YtDlp is null) return null;
        return await FirstLineAsync(tools.YtDlp, ["--version"], ct).ConfigureAwait(false);
    }

    /// <summary>Runs yt-dlp --update-to: a channel (stable, nightly, master) or channel@tag to roll back.</summary>
    public async Task<int> UpdateAsync(string target, Action<string> onLog, CancellationToken ct = default) =>
        await ProcessRunner.RunAsync(Exe, ["--update-to", target], onLog, onLog, ct).ConfigureAwait(false);

    public static async Task<string?> FirstLineAsync(string exe, string[] args, CancellationToken ct = default)
    {
        string? first = null;
        try
        {
            await ProcessRunner.RunAsync(exe, args,
                l => first ??= l.Trim().Length > 0 ? l.Trim() : null,
                _ => { }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
        return first;
    }

    private static string Quote(string a) => a.Contains(' ') || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;
}
