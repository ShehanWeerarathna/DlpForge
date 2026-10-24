using DlpForge.Core.Models;
using DlpForge.Core.Storage;
using DlpForge.Core.YtDlp;

namespace DlpForge.Core.Tests;

public class ArgumentBuilderTests
{
    private static readonly ArgumentBuilder.ToolOptions NoTools = new(null, null);

    [Fact]
    public void Video_download_uses_height_limited_selector_and_merge_format()
    {
        var args = ArgumentBuilder.ForDownload("https://x/v", new DownloadOptions
        {
            Quality = QualityPreset.P1080,
            VideoContainer = "mkv",
            OutputFolder = @"C:\Videos",
        }, NoTools);

        Assert.Equal("bv*[height<=1080]+ba/b[height<=1080]/bv*+ba/b", args[args.IndexOf("-f") + 1]);
        Assert.Equal("mkv", args[args.IndexOf("--merge-output-format") + 1]);
        Assert.Equal(@"C:\Videos", args[args.IndexOf("-P") + 1]);
        Assert.Equal("https://x/v", args[^1]);
        Assert.Equal("--", args[^2]);
        Assert.DoesNotContain("-x", args);
    }

    [Fact]
    public void Audio_download_extracts_audio_and_skips_subtitles()
    {
        var args = ArgumentBuilder.ForDownload("u", new DownloadOptions
        {
            Type = DownloadType.Audio,
            AudioFormat = "m4a",
            EmbedSubtitles = true,
        }, NoTools);

        Assert.Contains("-x", args);
        Assert.Equal("m4a", args[args.IndexOf("--audio-format") + 1]);
        Assert.DoesNotContain("--embed-subs", args);
        Assert.DoesNotContain("--merge-output-format", args);
    }

    [Fact]
    public void Best_audio_format_keeps_the_source_codec()
    {
        var args = ArgumentBuilder.ForDownload("u", new DownloadOptions { Type = DownloadType.Audio, AudioFormat = "best" }, NoTools);
        Assert.DoesNotContain("--audio-format", args);
    }

    [Fact]
    public void Tool_paths_are_passed_to_yt_dlp()
    {
        var args = ArgumentBuilder.ForInspect("u", new(@"C:\t\ffmpeg", @"C:\t\deno.exe"));
        Assert.Equal(@"C:\t\ffmpeg", args[args.IndexOf("--ffmpeg-location") + 1]);
        Assert.Equal(@"deno:C:\t\deno.exe", args[args.IndexOf("--js-runtimes") + 1]);
    }

    [Fact]
    public void Video_only_format_gets_best_audio_merged()
    {
        var f = new FormatInfo { FormatId = "137", VideoCodec = "avc1", AudioCodec = "none" };
        Assert.Equal("137+ba/137", ArgumentBuilder.SelectorFor(f));
        var combined = new FormatInfo { FormatId = "18", VideoCodec = "avc1", AudioCodec = "mp4a" };
        Assert.Equal("18", ArgumentBuilder.SelectorFor(combined));
    }
}

public class OutputParserTests
{
    [Fact]
    public void Parses_progress_json()
    {
        var line = ArgumentBuilder.ProgressMarker +
                   """{"status": "downloading", "downloaded_bytes": 512, "total_bytes": 1024, "speed": 2048.5, "eta": 3}""";
        var p = Assert.IsType<OutputLine.Progress>(OutputParser.Parse(line)).Value;
        Assert.Equal(50, p.Percent);
        Assert.Equal(2048.5, p.SpeedBytesPerSecond);
        Assert.Equal(3, p.EtaSeconds);
    }

    [Fact]
    public void Uses_estimate_and_tolerates_nulls()
    {
        var line = ArgumentBuilder.ProgressMarker +
                   """{"status": "downloading", "downloaded_bytes": 25, "total_bytes": null, "total_bytes_estimate": 100, "speed": null, "eta": null}""";
        var p = Assert.IsType<OutputLine.Progress>(OutputParser.Parse(line)).Value;
        Assert.Equal(25, p.Percent);
        Assert.Null(p.SpeedBytesPerSecond);
    }

    [Fact]
    public void Parses_postprocessor_final_path_and_error()
    {
        var pp = Assert.IsType<OutputLine.Progress>(OutputParser.Parse(ArgumentBuilder.PostprocessMarker + "Merger")).Value;
        Assert.Equal("processing", pp.Stage);
        Assert.Equal("Merger", pp.Postprocessor);

        var file = Assert.IsType<OutputLine.FinalPath>(OutputParser.Parse(ArgumentBuilder.FileMarker + @"C:\v\a b.mp4"));
        Assert.Equal(@"C:\v\a b.mp4", file.Path);

        var err = Assert.IsType<OutputLine.Error>(OutputParser.Parse("ERROR: [youtube] abc: Video unavailable"));
        Assert.Equal("[youtube] abc: Video unavailable", err.Message);

        Assert.IsType<OutputLine.Text>(OutputParser.Parse("[youtube] Extracting URL"));
    }
}

public class MediaInfoTests
{
    [Fact]
    public void Parses_single_video_with_formats()
    {
        const string json = """
        {"id": "abc", "title": "Test video", "uploader": "Someone", "duration": 125.0,
         "webpage_url": "https://example.com/watch?v=abc", "thumbnail": "https://img/abc.jpg",
         "subtitles": {"en": [], "live_chat": []}, "chapters": [{"title": "a"}, {"title": "b"}],
         "formats": [
            {"format_id": "sb0", "ext": "mhtml", "vcodec": "none", "acodec": "none"},
            {"format_id": "140", "ext": "m4a", "vcodec": "none", "acodec": "mp4a.40.2", "filesize": 2000000},
            {"format_id": "137", "ext": "mp4", "vcodec": "avc1", "acodec": "none", "width": 1920, "height": 1080, "fps": 30, "filesize_approx": 50000000}
         ]}
        """;
        var info = MediaInfo.Parse(json, "src");
        Assert.False(info.IsPlaylist);
        Assert.Equal("Test video", info.Title);
        Assert.Equal(2, info.Formats.Count);
        Assert.Equal(["en"], info.SubtitleLanguages);
        Assert.Equal(2, info.ChapterCount);
        var video = info.Formats.Single(f => f.FormatId == "137");
        Assert.Equal("1920x1080", video.Resolution);
        Assert.Equal("video only", video.Kind);
        Assert.StartsWith("~", video.SizeText);
    }

    [Fact]
    public void Parses_flat_playlist()
    {
        const string json = """
        {"_type": "playlist", "id": "PL1", "title": "My list",
         "entries": [
            {"_type": "url", "id": "a", "url": "https://example.com/a", "title": "First", "duration": 60,
             "thumbnails": [{"url": "https://img/a-small.jpg"}, {"url": "https://img/a-big.jpg"}]},
            null,
            {"_type": "url", "id": "c", "url": "https://example.com/c", "title": "Third"}
         ]}
        """;
        var info = MediaInfo.Parse(json, "src");
        Assert.True(info.IsPlaylist);
        Assert.Equal(2, info.Entries.Count);
        Assert.Equal(3, info.Entries[1].Index);
        Assert.Equal("https://img/a-big.jpg", info.Entries[0].Thumbnail);
    }
}

public class TemplatePreviewTests
{
    [Fact]
    public void Renders_common_fields()
    {
        Assert.Equal("Big Buck Bunny [aqz-KE-bpKQ].mp4", TemplatePreview.Render("%(title)s [%(id)s].%(ext)s"));
        Assert.Equal("NA.mp4", TemplatePreview.Render("%(nothing)s.%(ext)s"));
        Assert.Equal("fallback.mp4", TemplatePreview.Render("%(nothing|fallback)s.%(ext)s"));
        Assert.Equal("Blender/003.mp4", TemplatePreview.Render("%(uploader)s/%(playlist_index)03d.%(ext)s"));
    }

    [Fact]
    public void Fills_playlist_fields_as_literal_text()
    {
        var t = TemplatePreview.FillPlaylistFields("%(playlist_title)s/%(playlist_index)03d - %(title)s.%(ext)s", "Best: 100%", 7);
        Assert.Equal("Best_ 100%%/007 - %(title)s.%(ext)s", t);
    }
}

public class LinkParserTests
{
    [Fact]
    public void Extracts_links_from_messy_text()
    {
        var links = LinkParser.Extract("see https://a.com/x?v=1, and (https://b.org/y).\nhttps://a.com/x?v=1");
        Assert.Equal(["https://a.com/x?v=1", "https://b.org/y"], links);
    }

    [Fact]
    public void Recognises_urls_and_searches()
    {
        Assert.True(LinkParser.LooksLikeInput(" https://x.com "));
        Assert.True(LinkParser.LooksLikeInput("ytsearch5:lofi beats"));
        Assert.False(LinkParser.LooksLikeInput("hello"));
    }

    [Fact]
    public void Reads_url_shortcut_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dlpforge-{Guid.NewGuid():N}.url");
        File.WriteAllText(path, "[InternetShortcut]\r\nURL=https://example.com/v\r\n");
        try { Assert.Equal(["https://example.com/v"], LinkParser.FromFile(path)); }
        finally { File.Delete(path); }
    }
}

public class StoreTests
{
    [Fact]
    public void Round_trips_a_record()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dlpforge-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new DownloadStore(path);
            var r = new DownloadRecord
            {
                Id = "1",
                Url = "https://x",
                Title = "T",
                Options = new DownloadOptions { Type = DownloadType.Audio, AudioFormat = "flac" },
                Status = DownloadStatus.Completed,
                CreatedAt = DateTimeOffset.Now,
                CompletedAt = DateTimeOffset.Now,
                SizeBytes = 42,
            };
            store.Upsert(r);
            store.Upsert(r with { Title = "T2" });
            var loaded = Assert.Single(store.LoadAll());
            Assert.Equal("T2", loaded.Title);
            Assert.Equal("flac", loaded.Options.AudioFormat);
            Assert.Equal(DownloadType.Audio, loaded.Options.Type);
            Assert.Equal(42, loaded.SizeBytes);
            store.Delete("1");
            Assert.Empty(store.LoadAll());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
