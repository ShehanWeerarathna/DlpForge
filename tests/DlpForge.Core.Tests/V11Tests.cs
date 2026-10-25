using DlpForge.Core.Models;
using DlpForge.Core.Queue;
using DlpForge.Core.Storage;
using DlpForge.Core.Tools;
using DlpForge.Core.YtDlp;
using Microsoft.Data.Sqlite;

namespace DlpForge.Core.Tests;

public class V11ArgumentTests
{
    private static readonly ArgumentBuilder.CommonOptions NoTools = new(null, null);

    private static string After(List<string> args, string flag) => args[args.IndexOf(flag) + 1];

    [Fact]
    public void Every_call_asks_for_utf8_output()
    {
        Assert.Equal("utf-8", After(ArgumentBuilder.ForDownload("u", new DownloadOptions(), NoTools), "--encoding"));
        Assert.Equal("utf-8", After(ArgumentBuilder.ForInspect("u", NoTools), "--encoding"));
    }

    [Fact]
    public void Every_download_prints_metadata_without_simulating()
    {
        var args = ArgumentBuilder.ForDownload("u", new DownloadOptions(), NoTools);
        Assert.Contains("--no-simulate", args);
        Assert.Contains(args, a => a.StartsWith("before_dl:" + ArgumentBuilder.MetaMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void Cookies_from_a_browser_profile_or_a_file()
    {
        var browser = ArgumentBuilder.ForInspect("u", NoTools with
        {
            Cookies = new CookieOptions { Mode = CookieMode.Browser, Browser = "firefox", Profile = "work" },
        });
        Assert.Equal("firefox:work", After(browser, "--cookies-from-browser"));

        var file = ArgumentBuilder.ForDownload("u", new DownloadOptions(), NoTools with
        {
            Cookies = new CookieOptions { Mode = CookieMode.File, FilePath = @"C:\c\cookies.txt" },
        });
        Assert.Equal(@"C:\c\cookies.txt", After(file, "--cookies"));
        Assert.DoesNotContain("--cookies-from-browser", file);

        var none = ArgumentBuilder.ForInspect("u", NoTools with { Cookies = new CookieOptions { Mode = CookieMode.None } });
        Assert.DoesNotContain("--cookies", none);
        Assert.DoesNotContain("--cookies-from-browser", none);
    }

    [Fact]
    public void Section_uses_seconds_and_precise_cuts_only_with_a_section()
    {
        var args = ArgumentBuilder.ForDownload("u", new DownloadOptions
        {
            Section = new TimeRange(83.5, 120),
            ForceKeyframesAtCuts = true,
        }, NoTools);
        Assert.Equal("*83.5-120", After(args, "--download-sections"));
        Assert.Contains("--force-keyframes-at-cuts", args);

        var whole = ArgumentBuilder.ForDownload("u", new DownloadOptions { ForceKeyframesAtCuts = true }, NoTools);
        Assert.DoesNotContain("--download-sections", whole);
        Assert.DoesNotContain("--force-keyframes-at-cuts", whole);
    }

    [Fact]
    public void Metadata_overrides_are_escaped_literals()
    {
        var args = ArgumentBuilder.ForDownload("u", new DownloadOptions
        {
            TitleOverride = "  My Song: Live 100%  ",
            ArtistOverride = "Solo",
        }, NoTools);
        var parse = args.Select((a, i) => (a, i)).Where(x => x.a == "--parse-metadata").Select(x => args[x.i + 1]).ToList();
        Assert.Equal(
        [
            @"My Song\: Live 100%%:%(title)s",
            @"My Song\: Live 100%%:%(track)s",
            "Solo%(dlpforge|)s:%(artist)s",
        ], parse);
    }

    [Fact]
    public void SponsorBlock_split_chapters_and_extra_arguments()
    {
        var args = ArgumentBuilder.ForDownload("https://x/v", new DownloadOptions
        {
            SplitChapters = true,
            SponsorBlockMark = "selfpromo,interaction",
            SponsorBlockRemove = "sponsor",
            ExtraArguments = "--limit-rate 2M -o \"%(id)s.%(ext)s\"",
        }, new(@"C:\ff", null));

        Assert.Contains("--split-chapters", args);
        Assert.Equal("selfpromo,interaction", After(args, "--sponsorblock-mark"));
        Assert.Equal("sponsor", After(args, "--sponsorblock-remove"));
        // Extra arguments come after DlpForge's (including tool paths) and before the "--" guard.
        int extra = args.IndexOf("--limit-rate");
        Assert.True(extra > args.IndexOf("--ffmpeg-location"));
        Assert.Equal(["--limit-rate", "2M", "-o", "%(id)s.%(ext)s", "--", "https://x/v"], args[extra..]);
    }

    [Fact]
    public void Terminal_puts_app_options_before_the_users()
    {
        var args = ArgumentBuilder.ForTerminal(["-F", "https://x"], new(@"C:\ff", @"C:\deno.exe"));
        Assert.Equal(["--encoding", "utf-8", "--ffmpeg-location", @"C:\ff", "--js-runtimes", @"deno:C:\deno.exe", "-F", "https://x"], args);
        Assert.Equal(["--encoding", "utf-8", "-F", "https://x"], ArgumentBuilder.ForTerminal(["-F", "https://x"], null));
    }

    [Fact]
    public void Suffix_goes_before_the_extension()
    {
        Assert.Equal("%(title)s [%(id)s] (1-00–2-00).%(ext)s",
            ArgumentBuilder.AppendToTemplate("%(title)s [%(id)s].%(ext)s", " (1:00–2:00)"));
        Assert.Equal("%(title)s - 50%% off", ArgumentBuilder.AppendToTemplate("%(title)s", " - 50% off"));
        Assert.Equal("a-b", ArgumentBuilder.AppendToTemplate("a", "-b"));
    }
}

public class CommandLineTests
{
    [Fact]
    public void Splits_on_spaces_and_keeps_quoted_parts_and_windows_paths()
    {
        Assert.Equal(["-o", "%(title)s.%(ext)s", "-P", @"C:\My Videos\x", "--flag"],
            CommandLine.Split("""-o '%(title)s.%(ext)s'  -P "C:\My Videos\x" --flag"""));
        Assert.Equal(["say \"hi\""], CommandLine.Split("\"say \\\"hi\\\"\""));
        Assert.Equal([""], CommandLine.Split("\"\""));
        Assert.Empty(CommandLine.Split("   "));
    }

    [Fact]
    public void Join_quotes_what_needs_it()
    {
        Assert.Equal("-P \"C:\\My Videos\" -F", CommandLine.Join(["-P", @"C:\My Videos", "-F"]));
        Assert.Equal(CommandLine.Split(CommandLine.Join(["a b", "c"])), ["a b", "c"]);
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData("83", 83)]
    [InlineData("1:23", 83)]
    [InlineData("1:02:03", 3723)]
    [InlineData(" 0:07.5 ", 7.5)]
    public void Parses_clock_text(string text, double seconds) => Assert.Equal(seconds, Formatting.ParseClock(text));

    [Theory]
    [InlineData("")]
    [InlineData("1:60")]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    public void Rejects_bad_clock_text(string text) => Assert.Null(Formatting.ParseClock(text));

    [Fact]
    public void Formats_positions_and_seconds()
    {
        Assert.Equal("0:00", Formatting.Clock(0));
        Assert.Equal("1:02:03", Formatting.Clock(3723));
        Assert.Equal("83.5", Formatting.Seconds(83.5));
        Assert.Equal("120", Formatting.Seconds(120));
        Assert.Equal("0:12–1:45", new TimeRange(12, 105).Label);
    }
}

public class ImportListTests
{
    [Fact]
    public void Reads_links_playlists_searches_and_plain_words()
    {
        var list = LinkParser.ParseImportList("""
            # my list
            https://www.youtube.com/watch?v=abc
            https://www.youtube.com/playlist?list=PL1

            ytsearch3:lofi beats
            Sea waves drone video
            youtu.be/xyz
            // comment
            https://www.youtube.com/watch?v=abc
            """);
        Assert.Equal(
        [
            "https://www.youtube.com/watch?v=abc",
            "https://www.youtube.com/playlist?list=PL1",
            "ytsearch3:lofi beats",
            "ytsearch1:Sea waves drone video",
            "https://youtu.be/xyz",
        ], list);
    }

    [Fact]
    public void Knows_search_prefixes_and_bare_links()
    {
        Assert.True(LinkParser.IsSearch("ytsearchdate5:news"));
        Assert.True(LinkParser.IsSearch("scsearch:song"));
        Assert.True(LinkParser.IsSearch("ytsearchall:x"));
        Assert.False(LinkParser.IsSearch("hello world"));
        Assert.Equal("https://youtube.com/watch?v=1", LinkParser.AsBareLink("youtube.com/watch?v=1"));
        Assert.Null(LinkParser.AsBareLink("just words"));
        Assert.Null(LinkParser.AsBareLink("file.txt"));
        Assert.Equal("ytsearch20:sea waves", LinkParser.ToSearch(" sea waves ", 20));
    }
}

public class V11ParsingTests
{
    [Fact]
    public void Parses_metadata_line_and_ignores_NA()
    {
        var line = ArgumentBuilder.MetaMarker +
                   """{"title": "Sea waves", "uploader": "NA", "channel": "Free HD", "duration": 202, "thumbnail": "https://i/x.jpg"}""";
        var meta = Assert.IsType<OutputLine.Meta>(OutputParser.Parse(line)).Value;
        Assert.Equal("Sea waves", meta.Title);
        Assert.Equal("Free HD", meta.Uploader);
        Assert.Equal(202, meta.DurationSeconds);
        Assert.IsType<OutputLine.Text>(OutputParser.Parse(ArgumentBuilder.MetaMarker + "not json"));
    }

    [Fact]
    public void Parses_chapters_and_artist()
    {
        const string json = """
        {"id": "v", "title": "Album", "duration": 300, "artists": ["Band", "Guest"],
         "chapters": [{"title": "Intro", "start_time": 0, "end_time": 30.5},
                      {"title": "Song", "start_time": 30.5, "end_time": 300}]}
        """;
        var info = MediaInfo.Parse(json, "src");
        Assert.Equal("Band", info.Artist);
        Assert.Equal(2, info.ChapterCount);
        Assert.Equal(new ChapterInfo(2, "Song", 30.5, 300), info.Chapters[1]);
        Assert.Equal(new TimeRange(0, 30.5), info.Chapters[0].Range);
    }

    [Fact]
    public void SponsorBlock_actions_become_comma_lists()
    {
        var (mark, remove) = SponsorBlock.ToArguments(new Dictionary<string, SponsorBlockAction>
        {
            ["sponsor"] = SponsorBlockAction.Remove,
            ["intro"] = SponsorBlockAction.Mark,
            ["outro"] = SponsorBlockAction.Off,
            // Highlights can't be removed, only marked.
            ["poi_highlight"] = SponsorBlockAction.Remove,
            ["made_up"] = SponsorBlockAction.Remove,
        });
        Assert.Equal("intro,poi_highlight", mark);
        Assert.Equal("sponsor", remove);
    }

    [Fact]
    public void Templates_export_import_and_merge_by_name()
    {
        var json = CommandTemplate.Export([new("A", "--a"), new("B", "--b")]);
        var back = CommandTemplate.Import(json);
        Assert.Equal([new CommandTemplate("A", "--a"), new CommandTemplate("B", "--b")], back);

        var merged = CommandTemplate.Merge([new("A", "--old"), new("C", "--c")], [new("a", "--new"), new("D", "--d")]);
        Assert.Equal(["--new", "--c", "--d"], merged.Select(t => t.Arguments));
        Assert.Empty(CommandTemplate.Import("""[{"Name": "", "Arguments": "--x"}, {"Name": "n", "Arguments": " "}]"""));
    }

    [Fact]
    public void Quick_options_reuse_last_choices_without_one_off_parts()
    {
        var s = new AppSettings
        {
            FilenameTemplate = "%(title)s.%(ext)s",
            PlaylistFilenameTemplate = "%(playlist_title)s/%(title)s.%(ext)s",
            LastOptions = new DownloadOptions
            {
                Type = DownloadType.Audio,
                AudioFormat = "flac",
                Quality = QualityPreset.Custom,
                CustomFormat = "137+ba",
                OutputFolder = @"D:\Music",
                Section = new TimeRange(1, 2),
                TitleOverride = "x",
                ArtistOverride = "y",
                SponsorBlockRemove = "sponsor",
            },
        };
        var single = s.QuickOptions(playlist: false);
        Assert.Equal(DownloadType.Audio, single.Type);
        Assert.Equal("flac", single.AudioFormat);
        Assert.Equal(QualityPreset.Best, single.Quality);
        Assert.Null(single.CustomFormat);
        Assert.Null(single.Section);
        Assert.Null(single.TitleOverride);
        Assert.Null(single.ArtistOverride);
        Assert.Equal("sponsor", single.SponsorBlockRemove);
        Assert.Equal(@"D:\Music", single.OutputFolder);
        Assert.Equal("%(title)s.%(ext)s", single.FilenameTemplate);
        Assert.Equal("%(playlist_title)s/%(title)s.%(ext)s", s.QuickOptions(playlist: true).FilenameTemplate);

        var fresh = new AppSettings { DefaultType = DownloadType.Video };
        Assert.Equal(DownloadType.Video, fresh.QuickOptions(false).Type);
    }
}

public class SchedulingTests
{
    /// <summary>Holds posted callbacks until the test runs them, like a UI thread that is busy.</summary>
    private sealed class ManualContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlpforge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Old_database_gets_the_schedule_column_and_round_trips_it()
    {
        var dir = TempDir();
        var db = Path.Combine(dir, "old.db");
        try
        {
            // A 0.1 database: no scheduled_at column.
            using (var c = new SqliteConnection($"Data Source={db}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE downloads (id TEXT PRIMARY KEY, url TEXT NOT NULL, title TEXT NOT NULL, uploader TEXT,
                        thumbnail TEXT, duration REAL, options TEXT NOT NULL, status TEXT NOT NULL, file_path TEXT,
                        size_bytes INTEGER, error TEXT, log_path TEXT, created_at TEXT NOT NULL, completed_at TEXT,
                        hidden INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO downloads (id, url, title, options, status, created_at) VALUES ('old', 'u', 't', '{}', 'Queued', '2026-10-01T10:00:00+05:30');
                    """;
                cmd.ExecuteNonQuery();
            }

            var store = new DownloadStore(db);
            var old = Assert.Single(store.LoadAll());
            Assert.Null(old.ScheduledAt);

            var at = new DateTimeOffset(2026, 12, 24, 22, 30, 0, TimeSpan.FromHours(5.5));
            store.Upsert(old with { ScheduledAt = at });
            Assert.Equal(at, Assert.Single(new DownloadStore(db).LoadAll()).ScheduledAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Scheduled_item_waits_until_its_time()
    {
        var dir = TempDir();
        try
        {
            var paths = new AppPaths(dir);
            var store = new DownloadStore(paths.DatabasePath);
            var queue = new DownloadQueue(new YtDlpClient(new ToolLocator(paths)), store, paths, new ManualContext());
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            queue.Now = () => now;

            var item = queue.Enqueue("https://example.invalid/v", "v", new DownloadOptions(), scheduledAt: now.AddHours(2));
            Assert.Equal(DownloadStatus.Queued, item.Status);
            Assert.True(item.IsScheduled);
            Assert.Equal("Scheduled", item.StatusText);

            // A time in the past means "now": it is not kept.
            var item2 = queue.Enqueue("https://example.invalid/w", "w", new DownloadOptions { OutputFolder = dir }, scheduledAt: now.AddMinutes(-1));
            Assert.Null(item2.ScheduledAt);

            // Pause, then schedule again: back to queued with the new time.
            queue.Pause(item);
            Assert.Equal(DownloadStatus.Paused, item.Status);
            queue.Schedule(item, now.AddHours(3));
            Assert.Equal(DownloadStatus.Queued, item.Status);
            Assert.Equal(now.AddHours(3), item.ScheduledAt);

            // Pausing keeps the start time; resuming waits for it again.
            queue.Pause(item);
            queue.Resume(item);
            Assert.Equal(DownloadStatus.Queued, item.Status);
            Assert.Equal(now.AddHours(3), item.ScheduledAt);

            // When the time comes the schedule is cleared and the download starts.
            now = now.AddHours(3);
            queue.OnScheduleDue();
            Assert.Null(item.ScheduledAt);
            Assert.NotEqual(DownloadStatus.Queued, item.Status);

            queue.StopAllForExit();
            await queue.FlushAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}

public class LongPathAndHintTests
{
    [Fact]
    public void File_names_are_trimmed_to_fit_the_folder()
    {
        var folder = @"C:\" + new string('f', 117); // 120 characters
        var args = ArgumentBuilder.ForDownload("u", new DownloadOptions { OutputFolder = folder }, new(null, null));
        Assert.Equal("98", args[args.IndexOf("--trim-filenames") + 1]);
        Assert.Equal(186, ArgumentBuilder.TrimLengthFor(@"C:\Users\Someone\Videos\DlpForge\"));
        Assert.Equal(40, ArgumentBuilder.TrimLengthFor(@"C:\" + new string('x', 250)));
        Assert.Null(ArgumentBuilder.TrimLengthFor(""));
        Assert.DoesNotContain("--trim-filenames", ArgumentBuilder.ForDownload("u", new DownloadOptions(), new(null, null)));
    }

    [Fact]
    public void Explains_long_paths_and_sign_in_errors()
    {
        var longPath = @"C:\\" + string.Join(@"\\", Enumerable.Repeat("folder-name", 25)) + ".webp";
        var explained = ErrorHints.Explain($"[Errno 2] No such file or directory: '{longPath}'");
        Assert.StartsWith("The file path is longer than Windows allows", explained);
        Assert.EndsWith(".webp'", explained);

        Assert.StartsWith("This video needs a signed-in session",
            ErrorHints.Explain("[youtube] x: Sign in to confirm your age. This video may be inappropriate for some users."));
        Assert.Equal("[Errno 2] No such file or directory: 'C:\\a.mp4'", ErrorHints.Explain("[Errno 2] No such file or directory: 'C:\\a.mp4'"));
        Assert.Equal("Video unavailable", ErrorHints.Explain("Video unavailable"));
        Assert.StartsWith("DlpForge couldn't read the browser's cookies",
            ErrorHints.Explain(@"could not find firefox cookies database in 'C:\\Users\\x'"));
    }
}
