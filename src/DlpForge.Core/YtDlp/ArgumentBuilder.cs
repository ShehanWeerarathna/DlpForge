using DlpForge.Core.Models;

namespace DlpForge.Core.YtDlp;

/// <summary>Turns download options into a yt-dlp argument list. Pure, so it is easy to test.</summary>
public static class ArgumentBuilder
{
    // Markers DlpForge adds to yt-dlp's machine-readable output so they can't be confused with normal log text.
    public const string ProgressMarker = "[dlpforge:dl]";
    public const string PostprocessMarker = "[dlpforge:pp]";
    public const string FileMarker = "[dlpforge:file]";

    public sealed record ToolOptions(string? FfmpegFolder, string? DenoPath);

    public static List<string> ForInspect(string url, ToolOptions tools)
    {
        var args = new List<string> { "-J", "--flat-playlist", "--no-warnings", "--color", "never" };
        AddTools(args, tools);
        args.Add("--");
        args.Add(url);
        return args;
    }

    public static List<string> ForDownload(string url, DownloadOptions o, ToolOptions tools)
    {
        var args = new List<string>
        {
            "--newline", "--progress", "--color", "never", "--no-playlist",
            "--progress-template", "download:" + ProgressMarker + "%(progress)j",
            "--progress-template", "postprocess:" + PostprocessMarker + "%(progress.postprocessor)s",
            "--print", "after_move:" + FileMarker + "%(filepath)s",
        };

        if (o.Type == DownloadType.Audio)
        {
            args.Add("-f");
            args.Add("ba/b");
            args.Add("-x");
            if (!string.Equals(o.AudioFormat, "best", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--audio-format");
                args.Add(o.AudioFormat);
            }
            args.Add("--audio-quality");
            args.Add("0");
        }
        else
        {
            args.Add("-f");
            args.Add(VideoFormatSelector(o));
            if (!string.IsNullOrWhiteSpace(o.VideoContainer))
            {
                args.Add("--merge-output-format");
                args.Add(o.VideoContainer);
            }
        }

        if (!string.IsNullOrWhiteSpace(o.OutputFolder))
        {
            args.Add("-P");
            args.Add(o.OutputFolder);
        }
        args.Add("-o");
        args.Add(string.IsNullOrWhiteSpace(o.FilenameTemplate) ? Defaults.FilenameTemplate : o.FilenameTemplate);

        if (o.EmbedMetadata) args.Add("--embed-metadata");
        if (o.EmbedChapters) args.Add("--embed-chapters");
        if (o.EmbedThumbnail) args.Add("--embed-thumbnail");
        if (o.EmbedSubtitles && o.Type == DownloadType.Video)
        {
            args.Add("--embed-subs");
            args.Add("--sub-langs");
            args.Add(string.IsNullOrWhiteSpace(o.SubtitleLanguages) ? "en.*" : o.SubtitleLanguages);
        }

        AddTools(args, tools);
        args.Add("--");
        args.Add(url);
        return args;
    }

    public static string VideoFormatSelector(DownloadOptions o)
    {
        if (o.Quality == QualityPreset.Custom && !string.IsNullOrWhiteSpace(o.CustomFormat))
            return o.CustomFormat;

        int? height = o.Quality switch
        {
            QualityPreset.P2160 => 2160,
            QualityPreset.P1440 => 1440,
            QualityPreset.P1080 => 1080,
            QualityPreset.P720 => 720,
            QualityPreset.P480 => 480,
            QualityPreset.P360 => 360,
            _ => null,
        };
        return height is int h
            ? $"bv*[height<={h}]+ba/b[height<={h}]/bv*+ba/b"
            : "bv*+ba/b";
    }

    /// <summary>A selector for one specific format from the format table; video-only formats get the best audio merged in.</summary>
    public static string SelectorFor(FormatInfo f) =>
        f.HasVideo && !f.HasAudio ? $"{f.FormatId}+ba/{f.FormatId}" : f.FormatId;

    private static void AddTools(List<string> args, ToolOptions tools)
    {
        if (tools.FfmpegFolder != null)
        {
            args.Add("--ffmpeg-location");
            args.Add(tools.FfmpegFolder);
        }
        if (tools.DenoPath != null)
        {
            args.Add("--js-runtimes");
            args.Add("deno:" + tools.DenoPath);
        }
    }
}
