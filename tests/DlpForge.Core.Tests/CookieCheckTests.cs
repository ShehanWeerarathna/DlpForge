using System.Text;
using DlpForge.Core.Models;
using DlpForge.Core.YtDlp;

namespace DlpForge.Core.Tests;

/// <summary>Cases checked against yt-dlp 2026.10.21 with real files.</summary>
public class CookieCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private const string Header = "# Netscape HTTP Cookie File";
    private const long Future = 1_900_000_000;   // 2030
    private const long Past = 1_700_000_000;     // 2023

    private static string Row(string domain, string name, long expires = Future, string? flag = null) =>
        string.Join('\t', domain, flag ?? (domain.StartsWith('.') ? "TRUE" : "FALSE"), "/", "TRUE", expires.ToString(), name, "v");

    private static CookieFileReport Check(params string[] lines) =>
        CookieFileCheck.Check(Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"), Now);

    private static string[] SignedInYouTube(long expires = Future) =>
    [
        Header,
        Row(".youtube.com", "LOGIN_INFO", expires),
        Row(".youtube.com", "SAPISID", expires),
        Row(".youtube.com", "__Secure-3PAPISID", expires),
        Row(".google.com", "SID", expires),
    ];

    private static void OnlyError(CookieFileReport r, string containing)
    {
        Assert.False(r.Readable);
        Assert.False(r.HasYouTubeSignIn);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Error && n.Text.Contains(containing, StringComparison.OrdinalIgnoreCase));
        Assert.All(r.Notes, n => Assert.Equal(NoteLevel.Error, n.Level));
    }

    [Fact]
    public void A_signed_in_youtube_export_is_readable_and_worth_testing_online()
    {
        var r = Check(SignedInYouTube());
        Assert.True(r.Readable);
        Assert.True(r.HasYouTubeSignIn);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Ok && n.Text.Contains("4 cookies for youtube.com and google.com"));
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Ok && n.Text.StartsWith("YouTube sign-in cookies found, valid until"));
        Assert.DoesNotContain(r.Notes, n => n.Level is NoteLevel.Warning or NoteLevel.Error);
    }

    [Fact]
    public void Windows_line_endings_httponly_rows_and_the_short_header_are_fine()
    {
        var text = "# HTTP Cookie File\r\n#HttpOnly_" + Row(".youtube.com", "LOGIN_INFO") + "\r\n" + Row(".youtube.com", "SAPISID") + "\r\n";
        var r = CookieFileCheck.Check(Encoding.UTF8.GetBytes(text), Now);
        Assert.True(r.Readable);
        Assert.True(r.HasYouTubeSignIn);
    }

    [Fact]
    public void Missing_header_is_refused()
    {
        OnlyError(Check(Row(".example.com", "a")), "# Netscape HTTP Cookie File");
        // A blank first line counts as the first line too.
        OnlyError(Check(["", Header, Row(".example.com", "a")]), "# Netscape HTTP Cookie File");
    }

    [Fact]
    public void Json_exports_are_refused()
    {
        OnlyError(Check("[{\"domain\":\".youtube.com\",\"name\":\"SID\",\"value\":\"x\"}]"), "JSON");
        OnlyError(Check("[", "  {", "    \"domain\": \".youtube.com\",", "  }", "]"), "JSON");
    }

    [Fact]
    public void Byte_order_marks_and_utf16_are_refused()
    {
        var utf8 = Encoding.UTF8.GetBytes(Header + "\n" + Row(".example.com", "a") + "\n");
        var bom = CookieFileCheck.Check([0xEF, 0xBB, 0xBF, .. utf8], Now);
        OnlyError(bom, "byte order mark");
        Assert.Single(bom.Notes);   // the header is there once the mark is removed

        OnlyError(CookieFileCheck.Check(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Header)).ToArray(), Now), "UTF-16");
        OnlyError(CookieFileCheck.Check([0x23, 0x20, 0xE9, 0x0A], Now), "isn't UTF-8");
    }

    [Fact]
    public void A_subdomain_flag_that_disagrees_with_the_dot_is_refused()
    {
        var r = Check(Header, Row(".example.com", "a"), Row(".example.com", "b", flag: "FALSE"), Row("example.com", "c", flag: "TRUE"));
        OnlyError(r, "Line 3 (and 1 more)");
    }

    [Fact]
    public void Empty_files_and_files_without_cookies()
    {
        OnlyError(CookieFileCheck.Check([], Now), "empty");
        OnlyError(Check("  ", ""), "empty");

        var noCookies = Check(Header, "# just a comment");
        Assert.True(noCookies.Readable);
        Assert.Contains(noCookies.Notes, n => n.Level == NoteLevel.Error && n.Text.Contains("no cookies"));
    }

    [Fact]
    public void Rows_yt_dlp_skips_are_a_warning()
    {
        var r = Check(Header,
            ".example.com TRUE / TRUE 1900000000 spaces v",    // spaces, not tabs
            Row(".example.com", "ok"),
            Row(".example.com", "neg").Replace(Future.ToString(), "-1"),
            Row(".example.com", "exp").Replace(Future.ToString(), "1e9"),
            Row(".example.com", "dec").Replace(Future.ToString(), "1900000000.5"),
            "\t\t");                                           // blank to yt-dlp
        Assert.True(r.Readable);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Ok && n.Text.Contains("2 cookies for example.com"));
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Warning && n.Text.StartsWith("3 lines aren't in cookies.txt format (lines 2, 4 and 5)"));
    }

    [Fact]
    public void Youtube_cookies_from_a_signed_out_browser_warn()
    {
        var r = Check(Header, Row(".youtube.com", "VISITOR_INFO1_LIVE"), Row(".youtube.com", "SAPISID"));
        Assert.True(r.Readable);
        Assert.False(r.HasYouTubeSignIn);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Warning && n.Text.Contains("signed-out"));
    }

    [Fact]
    public void Expired_youtube_sign_in_is_an_error()
    {
        var r = Check(SignedInYouTube(Past));
        Assert.True(r.Readable);
        Assert.False(r.HasYouTubeSignIn);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Error && n.Text.Contains("expired on"));
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Info && n.Text.StartsWith("4 cookies have expired"));
    }

    [Fact]
    public void Session_cookies_count_as_live()
    {
        var r = Check(Header, Row(".youtube.com", "LOGIN_INFO", 0), Row(".youtube.com", "__Secure-1PAPISID", 0));
        Assert.True(r.HasYouTubeSignIn);
        Assert.Contains(r.Notes, n => n.Text == "YouTube sign-in cookies found.");
    }

    [Fact]
    public void Files_for_other_sites_say_so()
    {
        var r = Check(Header, Row(".example.com", "a"), Row("www.bbc.co.uk", "b"), Row(".vimeo.com", "c"), Row(".vimeo.com", "d"), Row(".x.com", "e"));
        Assert.True(r.Readable);
        Assert.False(r.HasYouTubeSignIn);
        Assert.Contains(r.Notes, n => n.Text.Contains("5 cookies for vimeo.com, bbc.co.uk, example.com and 1 more site"));
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Info && n.Text.Contains("no YouTube cookies"));
    }

    [Theory]
    [InlineData("accounts.google.com", "google.com")]
    [InlineData("www.bbc.co.uk", "bbc.co.uk")]
    [InlineData("youtube.com", "youtube.com")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    public void Site_names(string domain, string site) => Assert.Equal(site, CookieFileCheck.Site(domain));

    [Fact]
    public void A_missing_file_is_reported_not_created()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dlpforge-missing-{Guid.NewGuid():N}.txt");
        OnlyError(CookieFileCheck.CheckFile(path, Now), "doesn't exist");
        Assert.False(File.Exists(path));
        OnlyError(CookieFileCheck.CheckFile("  ", Now), "Choose");
    }

    [Fact]
    public void Sign_in_test_results()
    {
        Assert.Equal(NoteLevel.Ok, CookieFileCheck.InterpretSignInTest(0, []).Level);

        var rotated = CookieFileCheck.InterpretSignInTest(1,
        [
            "WARNING: [youtube:tab] The provided YouTube account cookies are no longer valid. They have likely been rotated in the browser as a security measure.",
            "WARNING: [youtube:tab] YouTube said: The playlist does not exist.",
            "ERROR: [youtube:tab] WL: YouTube said: The playlist does not exist.",
        ]);
        Assert.Equal(NoteLevel.Error, rotated.Level);
        Assert.Contains("rejected", rotated.Text);

        var loggedOut = CookieFileCheck.InterpretSignInTest(1, ["ERROR: [youtube:tab] Login details are needed to download this content."]);
        Assert.Equal(NoteLevel.Error, loggedOut.Level);

        var offline = CookieFileCheck.InterpretSignInTest(1, ["ERROR: [youtube:tab] WL: Unable to download webpage: timed out"]);
        Assert.Equal(NoteLevel.Warning, offline.Level);
        Assert.Contains("Unable to download webpage", offline.Text);
    }

    [Fact]
    public void Sign_in_test_opens_watch_later_without_its_videos()
    {
        var args = ArgumentBuilder.ForSignInTest(new ArgumentBuilder.CommonOptions(null, null,
            new CookieOptions { Mode = CookieMode.File, FilePath = @"C:\t\copy.txt" }));
        Assert.Equal("0", args[args.IndexOf("--playlist-items") + 1]);
        Assert.Equal(@"C:\t\copy.txt", args[args.IndexOf("--cookies") + 1]);
        Assert.DoesNotContain("--no-warnings", args);
        Assert.Equal(new[] { "--", CookieFileCheck.SignInTestUrl }, args.TakeLast(2));
    }

    [Fact]
    public void Download_errors_from_a_bad_cookie_file_point_to_verify()
    {
        Assert.Contains("Verify", ErrorHints.HintFor("'C:\\c.txt' does not look like a Netscape format cookies file"));
        Assert.Contains("Verify", ErrorHints.HintFor("Cookies file must be Netscape formatted, not JSON. See  https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp"));
        Assert.Contains("Verify", ErrorHints.HintFor("invalid Netscape format cookies file 'c.txt': '.a.com\\tFALSE'"));
    }
}
