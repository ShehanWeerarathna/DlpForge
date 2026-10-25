using System.Text.RegularExpressions;
using DlpForge.Core.Models;

namespace DlpForge.Core.YtDlp;

/// <summary>Turns download options into a yt-dlp argument list. Pure, so it is easy to test.</summary>
public static partial class ArgumentBuilder
{
    // Markers DlpForge adds to yt-dlp's machine-readable output so they can't be confused with normal log text.
    public const string ProgressMarker = "[dlpforge:dl]";
    public const string PostprocessMarker = "[dlpforge:pp]";
    public const string FileMarker = "[dlpforge:file]";
    public const string MetaMarker = "[dlpforge:meta]";

    /// <summary>
    /// yt-dlp.exe ignores PYTHONIOENCODING, so without this it writes file paths and messages in the
    /// Windows code page and drops or mangles characters outside it (Sinhala, Japanese, en dashes...).
    /// </summary>
    private static readonly string[] Utf8Output = ["--encoding", "utf-8"];

    /// <summary>Options that apply to every yt-dlp call: where the helper tools are, and cookies.</summary>
    public sealed record CommonOptions(string? FfmpegFolder, string? DenoPath, CookieOptions? Cookies = null);

    public static List<string> ForInspect(string url, CommonOptions common)
    {
        List<string> args = ["-J", "--flat-playlist", "--no-warnings", "--color", "never", .. Utf8Output];
        AddCommon(args, common);
        args.Add("--");
        args.Add(url);
        return args;
    }

    public static List<string> ForDownload(string url, DownloadOptions o, CommonOptions common)
    {
        List<string> args =
        [
            "--newline", "--progress", "--color", "never", "--no-playlist", "--no-simulate", .. Utf8Output,
            "--progress-template", "download:" + ProgressMarker + "%(progress)j",
            "--progress-template", "postprocess:" + PostprocessMarker + "%(progress.postprocessor)s",
            "--print", "before_dl:" + MetaMarker + "%(.{title,uploader,channel,duration,thumbnail})j",
            "--print", "after_move:" + FileMarker + "%(filepath)s",
        ];

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
        if (TrimLengthFor(o.OutputFolder) is int trim)
        {
            args.Add("--trim-filenames");
            args.Add(trim.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // New title and artist. These run before the file name is decided, so the file is named after the new title.
        if (!string.IsNullOrWhiteSpace(o.TitleOverride))
        {
            var title = LiteralTemplate(o.TitleOverride.Trim());
            args.AddRange(["--parse-metadata", title + ":%(title)s", "--parse-metadata", title + ":%(track)s"]);
        }
        if (!string.IsNullOrWhiteSpace(o.ArtistOverride))
            args.AddRange(["--parse-metadata", LiteralTemplate(o.ArtistOverride.Trim()) + ":%(artist)s"]);

        if (o.EmbedMetadata) args.Add("--embed-metadata");
        if (o.EmbedChapters) args.Add("--embed-chapters");
        if (o.EmbedThumbnail) args.Add("--embed-thumbnail");
        if (o.EmbedSubtitles && o.Type == DownloadType.Video)
        {
            args.Add("--embed-subs");
            args.Add("--sub-langs");
            args.Add(string.IsNullOrWhiteSpace(o.SubtitleLanguages) ? "en.*" : o.SubtitleLanguages);
        }

        if (o.Section is { } s)
        {
            args.Add("--download-sections");
            args.Add($"*{Formatting.Seconds(s.Start)}-{Formatting.Seconds(s.End)}");
            if (o.ForceKeyframesAtCuts) args.Add("--force-keyframes-at-cuts");
        }
        if (o.SplitChapters) args.Add("--split-chapters");
        if (!string.IsNullOrWhiteSpace(o.SponsorBlockMark))
        {
            args.Add("--sponsorblock-mark");
            args.Add(o.SponsorBlockMark);
        }
        if (!string.IsNullOrWhiteSpace(o.SponsorBlockRemove))
        {
            args.Add("--sponsorblock-remove");
            args.Add(o.SponsorBlockRemove);
        }

        AddCommon(args, common);
        // Extra arguments come last so they override DlpForge's choices, but always before "--".
        args.AddRange(CommandLine.Split(o.ExtraArguments));
        args.Add("--");
        args.Add(url);
        return args;
    }

    /// <summary>
    /// Arguments typed in the terminal. DlpForge's tool and cookie options go first so the
    /// user's own arguments can override them.
    /// </summary>
    public static List<string> ForTerminal(IEnumerable<string> userArgs, CommonOptions? common)
    {
        var args = new List<string>(Utf8Output);
        if (common != null) AddCommon(args, common);
        args.AddRange(userArgs);
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

    /// <summary>
    /// How long the file name (with any subfolders from the template) may be so the full path stays under
    /// Windows' 260-character limit, which applies unless long paths are turned on. Leaves room for the
    /// extension and yt-dlp's temporary names such as ".f137.mp4.part" and fragment files.
    /// </summary>
    public static int? TrimLengthFor(string? outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder)) return null;
        const int maxPath = 259, reserve = 40;
        return Math.Clamp(maxPath - outputFolder.TrimEnd('\\', '/').Length - 1 - reserve, 40, 200);
    }

    /// <summary>A selector for one specific format from the format table; video-only formats get the best audio merged in.</summary>
    public static string SelectorFor(FormatInfo f) =>
        f.HasVideo && !f.HasAudio ? $"{f.FormatId}+ba/{f.FormatId}" : f.FormatId;

    [GeneratedRegex("^[a-zA-Z_]+$")]
    private static partial Regex BareFieldName();

    /// <summary>
    /// Makes text safe as the FROM side of --parse-metadata, so it is used as written:
    /// % becomes %%, : becomes \:, and a single plain word (which yt-dlp would read as a
    /// field name) gets an empty field appended.
    /// </summary>
    public static string LiteralTemplate(string text)
    {
        var escaped = text.Replace("%", "%%").Replace(":", "\\:");
        return BareFieldName().IsMatch(text) ? escaped + "%(dlpforge|)s" : escaped;
    }

    /// <summary>
    /// Adds a literal suffix before the extension of a file name template, e.g. for a cut part:
    /// "%(title)s.%(ext)s" + " (1-00 to 2-00)" gives "%(title)s (1-00 to 2-00).%(ext)s".
    /// </summary>
    public static string AppendToTemplate(string template, string suffix)
    {
        if (string.IsNullOrWhiteSpace(template)) template = Defaults.FilenameTemplate;
        var safe = suffix.Replace("%", "%%");
        foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '-');
        const string ext = ".%(ext)s";
        return template.EndsWith(ext, StringComparison.Ordinal)
            ? template[..^ext.Length] + safe + ext
            : template + safe;
    }

    private static void AddCommon(List<string> args, CommonOptions common)
    {
        if (common.FfmpegFolder != null)
        {
            args.Add("--ffmpeg-location");
            args.Add(common.FfmpegFolder);
        }
        if (common.DenoPath != null)
        {
            args.Add("--js-runtimes");
            args.Add("deno:" + common.DenoPath);
        }
        switch (common.Cookies)
        {
            case { Mode: CookieMode.Browser, Browser: { Length: > 0 } browser } c:
                args.Add("--cookies-from-browser");
                args.Add(string.IsNullOrWhiteSpace(c.Profile) ? browser : $"{browser}:{c.Profile.Trim()}");
                break;
            case { Mode: CookieMode.File, FilePath: { Length: > 0 } file }:
                args.Add("--cookies");
                args.Add(file);
                break;
        }
    }
}
