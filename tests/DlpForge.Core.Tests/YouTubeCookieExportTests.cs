using System.Text;
using DlpForge.Core.YtDlp;

namespace DlpForge.Core.Tests;

public class YouTubeCookieExportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private static BrowserCookie Cookie(string domain, string name, DateTimeOffset? expires = null) =>
        new(domain, "/", name, "value", true, true, expires);

    [Fact]
    public void Export_roundtrips_sign_in_HttpOnly_and_session_cookies()
    {
        var text = YouTubeCookieExport.Serialize([
            Cookie(".youtube.com", "LOGIN_INFO", Now.AddDays(10)),
            Cookie(".youtube.com", "SAPISID"),
            Cookie("www.youtube.com", "host-only"),
        ], Now);
        var report = CookieFileCheck.Check(Encoding.UTF8.GetBytes(text), Now);
        Assert.True(report.Readable);
        Assert.True(report.HasYouTubeSignIn);
        Assert.Contains("#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\tvalue\n", text);
        Assert.Contains("#HttpOnly_www.youtube.com\tFALSE", text);
    }

    [Fact]
    public void Export_excludes_expired_unrelated_and_lookalike_domains()
    {
        var text = YouTubeCookieExport.Serialize([
            Cookie(".youtube.com", "expired", Now.AddSeconds(-1)),
            Cookie(".google.com", "google"),
            Cookie("evilyoutube.com", "lookalike"),
            Cookie("youtube.com.evil.test", "suffix"),
            Cookie(".youtube.com", "active"),
        ], Now);
        Assert.Contains("active", text);
        Assert.DoesNotContain("expired", text);
        Assert.DoesNotContain("google", text);
        Assert.DoesNotContain("lookalike", text);
        Assert.DoesNotContain("suffix", text);
    }

    [Theory]
    [InlineData("injected\tcolumn")]
    [InlineData("injected\nrow")]
    [InlineData("injected\rrow")]
    public void Export_rejects_delimiters(string value)
    {
        Assert.Throws<InvalidDataException>(() => YouTubeCookieExport.Serialize([
            Cookie(".youtube.com", "name") with { Value = value },
        ], Now));
    }

    [Fact]
    public void Sign_in_needs_login_info_and_a_live_session_cookie()
    {
        Assert.True(YouTubeCookieExport.HasSignIn([Cookie(".youtube.com", "LOGIN_INFO"), Cookie(".youtube.com", "__Secure-3PAPISID")], Now));
        Assert.False(YouTubeCookieExport.HasSignIn([Cookie(".youtube.com", "SAPISID")], Now));
        Assert.False(YouTubeCookieExport.HasSignIn([Cookie(".google.com", "LOGIN_INFO"), Cookie(".google.com", "SAPISID")], Now));
        Assert.False(YouTubeCookieExport.HasSignIn([Cookie(".youtube.com", "LOGIN_INFO"), Cookie(".youtube.com", "SAPISID") with { Value = "a\tb" }], Now));
        Assert.False(YouTubeCookieExport.HasSignIn([], Now));
    }

    [Theory]
    [InlineData("www.youtube.com", true)]
    [InlineData("accounts.youtube.com", true)]
    [InlineData("youtube.com", true)]
    [InlineData("accounts.google.com", true)]
    [InlineData("accounts.google.lk", true)]
    [InlineData("www.google.co.uk", true)]
    [InlineData("google.com.au", true)]
    [InlineData("ACCOUNTS.GOOGLE.COM.", true)]
    [InlineData("evilyoutube.com", false)]
    [InlineData("youtube.com.evil.test", false)]
    [InlineData("google.evil.com", false)]
    [InlineData("google.co.evil", false)]
    [InlineData("notgoogle.com", false)]
    [InlineData("login.microsoftonline.com", false)]
    [InlineData("google.c0", false)]
    public void Google_and_youtube_hosts(string host, bool expected) =>
        Assert.Equal(expected, YouTubeCookieExport.IsGoogleOrYouTubeHost(host));

    [Fact]
    public void Saved_files_are_recognised_and_old_ones_deleted()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"dlpforge-test-{Guid.NewGuid():N}");
        try
        {
            var first = YouTubeCookieExport.NewSavedFile(dataDir);
            var second = YouTubeCookieExport.NewSavedFile(dataDir);
            Assert.NotEqual(first, second);
            Directory.CreateDirectory(YouTubeCookieExport.SavedFolder(dataDir));
            var other = Path.Combine(YouTubeCookieExport.SavedFolder(dataDir), "mine.txt");
            foreach (var f in new[] { first, second, other }) File.WriteAllText(f, "x");

            Assert.True(YouTubeCookieExport.IsSavedFile(dataDir, first));
            Assert.True(YouTubeCookieExport.IsSavedFile(dataDir, " " + second.ToUpperInvariant() + " "));
            Assert.False(YouTubeCookieExport.IsSavedFile(dataDir, other));
            Assert.False(YouTubeCookieExport.IsSavedFile(dataDir, Path.Combine(dataDir, Path.GetFileName(first))));
            Assert.False(YouTubeCookieExport.IsSavedFile(dataDir, ""));

            YouTubeCookieExport.DeleteSavedFiles(dataDir, keep: second);
            Assert.False(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.True(File.Exists(other));   // only files the window saved are deleted

            YouTubeCookieExport.DeleteSavedFiles(dataDir, keep: null);
            Assert.False(File.Exists(second));
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Settings_keep_the_saved_time_and_not_the_computed_cookie_options()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dlpforge-settings-{Guid.NewGuid():N}.json");
        try
        {
            var saved = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            new Storage.AppSettings { CookieFile = @"C:\c.txt", YouTubeCookiesSavedAt = saved }.Save(path);
            Assert.DoesNotContain("\"Cookies\"", File.ReadAllText(path));
            Assert.Equal(saved, Storage.AppSettings.Load(path).YouTubeCookiesSavedAt);
            Assert.Null(new Storage.AppSettings().YouTubeCookiesSavedAt);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
