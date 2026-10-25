using System.Text;
using DlpForge.Core.Models;
using DlpForge.Core.Tools;

namespace DlpForge.Core.YtDlp;

public sealed class YtDlpException(string message) : Exception(message);

public sealed record DownloadResult(int ExitCode, string? FilePath, string? Error)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// What DlpForge asks yt-dlp to do: inspect, download, report its version, update itself, and run
/// a command typed in the terminal.
/// </summary>
public sealed class YtDlpClient(ToolLocator tools)
{
    public ToolLocator Tools => tools;

    /// <summary>Cookies passed to every call. Set from Settings and updated when they change.</summary>
    public CookieOptions? Cookies { get; set; }

    private string Exe => tools.YtDlp
        ?? throw new YtDlpException("yt-dlp is not installed. Open Settings > Tools and click Install.");

    public ArgumentBuilder.CommonOptions CommonOptions => new(tools.FfmpegFolder, tools.Deno, Cookies);

    public async Task<MediaInfo> InspectAsync(string url, CancellationToken ct = default)
    {
        var stdout = new StringBuilder();
        var errors = new List<string>();
        int code = await ProcessRunner.RunAsync(
            Exe, ArgumentBuilder.ForInspect(url, CommonOptions),
            line => stdout.AppendLine(line),
            line => { if (line.StartsWith("ERROR:")) errors.Add(line["ERROR:".Length..].Trim()); },
            ct).ConfigureAwait(false);

        var json = stdout.ToString().Trim();
        if (code != 0 || json.Length == 0)
            throw new YtDlpException(ErrorHints.Explain(errors.LastOrDefault() ?? $"yt-dlp exited with code {code}."));
        return MediaInfo.Parse(json, url);
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        Action<DownloadProgress> onProgress,
        Action<string> onLog,
        CancellationToken ct,
        Action<MediaMeta>? onMeta = null)
    {
        string? finalPath = null;
        string? lastError = null;
        var args = ArgumentBuilder.ForDownload(url, options, CommonOptions);
        onLog("> yt-dlp " + CommandLine.Join(args));

        void Handle(string line)
        {
            switch (OutputParser.Parse(line))
            {
                case OutputLine.Progress p:
                    onProgress(p.Value);
                    // Keep logs readable: only log stage changes, not every progress tick.
                    if (p.Value.Stage != "downloading") onLog(line);
                    break;
                case OutputLine.Meta m:
                    onMeta?.Invoke(m.Value);
                    onLog(line);
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
        return new DownloadResult(code, finalPath, code == 0 ? null : ErrorHints.Explain(lastError ?? $"yt-dlp exited with code {code}."));
    }

    /// <summary>
    /// Asks YouTube whether a cookies.txt file signs you in. yt-dlp saves the cookies back into the file it is
    /// given, and drops the sign-in cookies when YouTube rejects them, so this runs on a temporary copy and
    /// the user's file is never changed.
    /// </summary>
    public async Task<CheckNote> TestYouTubeSignInAsync(string cookieFile, CancellationToken ct = default)
    {
        var exe = Exe;
        var copy = Path.Combine(Path.GetTempPath(), $"dlpforge-cookies-{Guid.NewGuid():N}.txt");
        File.Copy(cookieFile, copy);
        try
        {
            var stderr = new List<string>();
            var common = CommonOptions with { Cookies = new CookieOptions { Mode = CookieMode.File, FilePath = copy } };
            int code = await ProcessRunner.RunAsync(exe, ArgumentBuilder.ForSignInTest(common), _ => { }, stderr.Add, ct)
                .ConfigureAwait(false);
            return CookieFileCheck.InterpretSignInTest(code, stderr);
        }
        finally
        {
            try { File.Delete(copy); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        if (tools.YtDlp is null) return null;
        return await FirstLineAsync(tools.YtDlp, ["--version"], ct).ConfigureAwait(false);
    }

    /// <summary>Runs yt-dlp --update-to: a channel (stable, nightly, master) or channel@tag to roll back.</summary>
    public async Task<int> UpdateAsync(string target, Action<string> onLog, CancellationToken ct = default) =>
        await ProcessRunner.RunAsync(Exe, ["--update-to", target], onLog, onLog, ct).ConfigureAwait(false);

    /// <summary>Runs yt-dlp with arguments typed in the terminal, streaming every output line.</summary>
    public Task<int> RunTerminalAsync(IReadOnlyList<string> userArgs, bool addCommon, string? workingDirectory,
        Action<string> onLine, CancellationToken ct)
    {
        var args = ArgumentBuilder.ForTerminal(userArgs, addCommon ? CommonOptions : null);
        if (workingDirectory != null) Directory.CreateDirectory(workingDirectory);
        return ProcessRunner.RunAsync(Exe, args, onLine, onLine, ct, workingDirectory);
    }

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
}
