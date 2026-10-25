using System.Text;
using DlpForge.Core.YtDlp;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DlpForge.App.Services;

public enum YouTubeLoginMode
{
    /// <summary>Sign in, or open YouTube when already signed in. The user clicks Save cookies.</summary>
    SignIn,
    /// <summary>Open YouTube so Google can renew the session, then save the cookies and close. Asks to sign in again when the session has ended.</summary>
    Refresh,
    /// <summary>Sign out of Google and delete the browser's data.</summary>
    SignOut,
}

/// <param name="SavedPath">The new cookies file, or null when none was saved.</param>
/// <param name="SignedOut">The browser signed out and deleted its data.</param>
public sealed record YouTubeLoginResult(string? SavedPath, bool SignedOut);

/// <summary>
/// DlpForge's own browser window for YouTube: WebView2 hosted in Windows Forms on its own STA thread.
/// </summary>
/// <remarks>
/// The browser profile (data\webview2) stays signed in, so cookies can be refreshed later without a password.
/// Cookies are saved the way yt-dlp's guide recommends: from youtube.com/robots.txt, where no YouTube script runs
/// that could replace them, and right before the window closes, so nothing uses that session afterwards. Opening
/// YouTube here can renew the session and make the last saved file stale, so when an account is already in use the
/// window saves fresh cookies again as it closes.
/// </remarks>
public static class YouTubeLogin
{
    private const string Home = "https://www.youtube.com/";
    private const string Robots = "https://www.youtube.com/robots.txt";
    private const string SignInPage = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";
    private const string SignOutPage = "https://accounts.google.com/Logout";
    private const string ProfileName = "youtube";

    /// <param name="keepInSync">An account from this window is in use, so save fresh cookies whenever pages were opened.</param>
    public static Task<YouTubeLoginResult> ShowAsync(string dataDir, YouTubeLoginMode mode, bool keepInSync)
    {
        var completion = new TaskCompletionSource<YouTubeLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Forms.Application.EnableVisualStyles();
                using var form = new LoginForm(dataDir, mode, keepInSync);
                Forms.Application.Run(form);
                completion.TrySetResult(new YouTubeLoginResult(form.SavedPath, form.SignedOut));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "YouTube account" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class LoginForm : Forms.Form
    {
        private readonly string _dataDir;
        private readonly YouTubeLoginMode _mode;
        private readonly bool _keepInSync;
        private readonly WebView2 _browser = new() { Dock = Forms.DockStyle.Fill };
        private readonly Forms.Label _status = new() { AutoSize = true, Margin = new Forms.Padding(3, 0, 3, 8) };
        private readonly Forms.Button _save = new() { Text = "Save cookies", AutoSize = true, Enabled = false };
        private readonly Forms.Button _home = new() { Text = "YouTube home", AutoSize = true, Enabled = false };
        private readonly Forms.Button _close = new() { Text = "Close", AutoSize = true };

        private bool _signedIn;   // the profile holds a YouTube sign-in
        private bool _used;       // pages other than robots.txt were opened, which may renew the session
        private bool _working;    // saving or signing out: the buttons wait
        private bool _syncTried;  // the save-on-close already ran once
        private bool _done;       // close without saving
        private bool _awaitSignIn;   // waiting for the user to sign in: save and close as soon as they have
        private bool _handoffTried;  // already sent a stalled Google sign-in on to YouTube
        private bool _polling;
        private DateTime _lastNavigation = DateTime.UtcNow;
        private readonly Forms.Timer _poll = new() { Interval = 1000 };

        // The navigation NavigateAsync waits for.
        private string? _awaitUrl;
        private ulong? _awaitId;
        private TaskCompletionSource<bool>? _navigation;

        public string? SavedPath { get; private set; }
        public bool SignedOut { get; private set; }
        private CoreWebView2? Core => _browser.CoreWebView2;

        public LoginForm(string dataDir, YouTubeLoginMode mode, bool keepInSync)
        {
            _dataDir = dataDir;
            _mode = mode;
            _keepInSync = keepInSync;

            SuspendLayout();
            AutoScaleDimensions = new Drawing.SizeF(96F, 96F);
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            Text = mode == YouTubeLoginMode.SignOut ? "Signing out of YouTube - DlpForge" : "YouTube account - DlpForge";
            ClientSize = new Drawing.Size(1080, 760);
            MinimumSize = new Drawing.Size(640, 480);
            StartPosition = Forms.FormStartPosition.CenterScreen;
            try { Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""); }
            catch (Exception ex) when (ex is ArgumentException or IOException) { }

            var footer = new Forms.FlowLayoutPanel
            {
                Dock = Forms.DockStyle.Bottom, AutoSize = true, Padding = new Forms.Padding(12, 10, 12, 10),
                FlowDirection = Forms.FlowDirection.TopDown, WrapContents = false,
            };
            var actions = new Forms.FlowLayoutPanel { AutoSize = true, Margin = Forms.Padding.Empty };
            _save.Click += async (_, _) => { if (!_working) await SaveAndCloseAsync(); };
            _home.Click += (_, _) => Core?.Navigate(Home);
            _close.Click += (_, _) => Close();
            actions.Controls.AddRange([_save, _home, _close]);
            footer.Controls.Add(_status);
            footer.Controls.Add(actions);
            Controls.Add(_browser);
            Controls.Add(footer);
            ResumeLayout(false);
            PerformLayout();

            Status(mode switch
            {
                YouTubeLoginMode.SignOut => "Signing out of Google and deleting this window's browser data…",
                YouTubeLoginMode.Refresh => "Opening YouTube to refresh the cookies…",
                _ => "Opening the browser…",
            });
            Resize += (_, _) => FitStatus();
            Shown += async (_, _) => await StartAsync();
            FormClosing += OnFormClosing;
            FormClosed += (_, _) => _poll.Stop();
            _poll.Tick += async (_, _) => await PollSignInAsync();
            FitStatus();
        }

        private async Task StartAsync()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_dataDir, "webview2"));
                if (IsDisposed) return;
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.ProfileName = ProfileName;
                await _browser.EnsureCoreWebView2Async(environment, options);
                if (IsDisposed) return;
                Configure(_browser.CoreWebView2);

                switch (_mode)
                {
                    case YouTubeLoginMode.SignOut: await SignOutAsync(); break;
                    case YouTubeLoginMode.Refresh: await RefreshAsync(); break;
                    default: await OpenAsync(); break;
                }
            }
            catch (Exception ex)
            {
                // Browser exceptions can contain page or session data, so only the missing runtime is named.
                if (IsDisposed) return;
                _working = false;
                Status(ex is WebView2RuntimeNotFoundException
                    ? "This window needs the Microsoft Edge WebView2 Runtime. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and try again."
                    : "Something went wrong in the browser. Close this window and try again.");
                UpdateButtons();
            }
        }

        private void Configure(CoreWebView2 core)
        {
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Allowed(e.Uri)) core.Navigate(e.Uri);
            };
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.ProcessFailed += (_, _) => Status("The browser stopped working. Close this window and try again.");
        }

        // ---- The three modes ----

        private string SignedInMessage => _keepInSync
            ? "You're signed in. DlpForge saves fresh cookies when you close this window."
            : "You're signed in. Click Save cookies to use this account for downloads.";

        private async Task OpenAsync()
        {
            if (await CheckSignInAsync())
            {
                Status(SignedInMessage);
                Core!.Navigate(Home);
            }
            else
            {
                WaitForSignIn("Sign in with your Google account. When you're done, DlpForge saves the YouTube cookies and closes this window. " +
                    "It stays signed in here, so you can refresh the cookies later without your password.");
                Core!.Navigate(SignInPage);
            }
        }

        /// <summary>From now on, save and close as soon as the user has signed in, like the end of a sign-in should.</summary>
        private void WaitForSignIn(string message)
        {
            _awaitSignIn = true;
            _handoffTried = false;
            Status(message);
            _poll.Start();
        }

        private async Task FinishSignInAsync()
        {
            _awaitSignIn = false;
            _poll.Stop();
            Status("You're signed in. Saving YouTube cookies…");
            // Let the last sign-in redirects settle before leaving the page.
            await Task.Delay(1000);
            if (!IsDisposed && !_working) await SaveAndCloseAsync();
        }

        /// <summary>
        /// Runs once a second while waiting for a sign-in, so finishing doesn't depend on page events. When Google has
        /// signed in but the page stalls before reaching YouTube, it sends the window on to YouTube's sign-in once.
        /// </summary>
        private async Task PollSignInAsync()
        {
            if (_polling || !_awaitSignIn || _working || Core == null || IsDisposed) return;
            _polling = true;
            try
            {
                if (await CheckSignInAsync())
                {
                    await FinishSignInAsync();
                    return;
                }
                if (!_handoffTried && DateTime.UtcNow - _lastNavigation > TimeSpan.FromSeconds(2) && await HasGoogleSessionAsync())
                {
                    _handoffTried = true;
                    Status("Signed in to Google. Finishing the sign-in on YouTube…");
                    Core.Navigate(SignInPage);
                }
            }
            catch (Exception)
            {
                // The window closed while checking.
            }
            finally
            {
                _polling = false;
            }
        }

        private async Task<bool> HasGoogleSessionAsync()
        {
            var cookies = await Core!.CookieManager.GetCookiesAsync("https://accounts.google.com/");
            return cookies.Any(c => c.Name is "SID" or "__Secure-1PSID" or "__Secure-3PSID");
        }

        private async Task RefreshAsync()
        {
            _working = true;
            UpdateButtons();
            bool loaded = await NavigateAsync(Home);
            if (IsDisposed) return;
            if (!loaded)
            {
                _working = false;
                Status("YouTube didn't load. Check your connection, then click YouTube home or Close.");
                UpdateButtons();
                return;
            }
            if (!await CheckSignInAsync())
            {
                _working = false;
                UpdateButtons();
                WaitForSignIn("Your Google session has ended. Sign in again; DlpForge saves the cookies and closes this window when you're done.");
                Core!.Navigate(SignInPage);
                return;
            }
            // Give YouTube's page a moment to renew the session before saving.
            await Task.Delay(3000);
            if (!IsDisposed) await SaveAndCloseAsync();
        }

        private async Task SignOutAsync()
        {
            _working = true;
            UpdateButtons();
            // Ends the session at Google too, so copies of the cookies stop working.
            await NavigateAsync(SignOutPage, 15_000);
            if (IsDisposed) return;
            await Core!.Profile.ClearBrowsingDataAsync();
            SignedOut = true;
            _done = true;
            Close();
        }

        /// <summary>Saves the YouTube cookies from robots.txt and closes the window.</summary>
        private async Task SaveAndCloseAsync()
        {
            _working = true;
            UpdateButtons();
            Status("Saving YouTube cookies…");
            try
            {
                await NavigateAsync(Robots);
                await Task.Delay(500);
                if (IsDisposed) return;
                var now = DateTimeOffset.UtcNow;
                var cookies = (await Core!.CookieManager.GetCookiesAsync(Home)).Select(ToBrowserCookie).ToList();
                if (IsDisposed) return;
                var text = YouTubeCookieExport.Serialize(cookies, now);
                if (!CookieFileCheck.Check(Encoding.UTF8.GetBytes(text), now).HasYouTubeSignIn)
                {
                    _working = false;
                    _signedIn = false;
                    UpdateButtons();
                    WaitForSignIn("You're not signed in to YouTube in this window. Sign in; DlpForge saves the cookies when you're done.");
                    Core.Navigate(SignInPage);
                    return;
                }
                var path = YouTubeCookieExport.NewSavedFile(_dataDir);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
                SavedPath = path;
                _done = true;
                Close();
            }
            catch (Exception)
            {
                // Browser exceptions can contain page or session data, so they are not shown or logged.
                if (IsDisposed) return;
                _working = false;
                Status("DlpForge couldn't save the cookies. Check that its data folder can be written to, then try again.");
                UpdateButtons();
            }
        }

        // ---- Navigation ----

        /// <summary>
        /// Until YouTube is signed in, any HTTPS page may open: Google's sign-in passes through country domains such as
        /// accounts.google.lk, and work or school accounts through their own sign-in page. After that, only Google and YouTube.
        /// </summary>
        private bool Allowed(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (!_signedIn || YouTubeCookieExport.IsGoogleOrYouTubeHost(uri.Host));

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _lastNavigation = DateTime.UtcNow;
            if (!Allowed(e.Uri))
            {
                e.Cancel = true;
                var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ? uri.Host : "";
                Status(host.Length > 0
                    ? $"This window only opens Google and YouTube pages, so it didn't open {host}."
                    : "This window only opens Google and YouTube pages.");
                return;
            }
            if (!e.Uri.Equals(Robots, StringComparison.OrdinalIgnoreCase)) _used = true;
            if (_awaitUrl != null && _awaitId == null && e.Uri.StartsWith(_awaitUrl, StringComparison.OrdinalIgnoreCase))
                _awaitId = e.NavigationId;
        }

        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _lastNavigation = DateTime.UtcNow;
            if (_awaitId == e.NavigationId)
            {
                _awaitId = null;
                _awaitUrl = null;
                _navigation?.TrySetResult(e.IsSuccess);
            }
            if (_working || IsDisposed) return;

            var source = Core?.Source ?? "";
            // Google shows this page when it won't allow sign-in from an embedded browser.
            if (source.Contains("/signin/rejected", StringComparison.OrdinalIgnoreCase))
            {
                Status("Google didn't allow signing in from this window. Close it and use cookies from a browser (Firefox works best) or a cookies.txt file instead.");
                return;
            }
            if (!e.IsSuccess)
            {
                if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    Status("The page didn't load. Check your connection, then click YouTube home.");
                return;
            }
            try
            {
                bool before = _signedIn;
                bool now = await CheckSignInAsync();
                if (now && _awaitSignIn) await FinishSignInAsync();
                else if (now && !before) Status(SignedInMessage);
                else if (!now && before) WaitForSignIn("You're signed out. Sign in again; DlpForge saves the cookies when you're done.");
            }
            catch (Exception)
            {
                // The window closed while checking.
            }
        }

        /// <summary>Navigates and waits until that page has loaded. False when it failed or took too long.</summary>
        private async Task<bool> NavigateAsync(string url, int timeoutMs = 30_000)
        {
            var navigation = _navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaitUrl = url;
            _awaitId = null;
            Core!.Navigate(url);
            var first = await Task.WhenAny(navigation.Task, Task.Delay(timeoutMs));
            return first == navigation.Task && navigation.Task.Result;
        }

        private async Task<bool> CheckSignInAsync()
        {
            var cookies = await Core!.CookieManager.GetCookiesAsync(Home);
            _signedIn = YouTubeCookieExport.HasSignIn(cookies.Select(ToBrowserCookie), DateTimeOffset.UtcNow);
            UpdateButtons();
            return _signedIn;
        }

        private static BrowserCookie ToBrowserCookie(CoreWebView2Cookie c) => new(
            c.Domain, c.Path, c.Name, c.Value, c.IsSecure, c.IsHttpOnly,
            c.IsSession ? null : new DateTimeOffset(c.Expires.ToUniversalTime()));

        // ---- Window ----

        private void OnFormClosing(object? sender, Forms.FormClosingEventArgs e)
        {
            if (_done || e.CloseReason is Forms.CloseReason.WindowsShutDown or Forms.CloseReason.TaskManagerClosing) return;
            if (_working && _mode == YouTubeLoginMode.SignOut) { e.Cancel = true; return; }
            // Pages opened here may have renewed the session, which makes the file in use stale: save again first.
            if (_keepInSync && _signedIn && _used && !_syncTried && Core != null)
            {
                e.Cancel = true;
                _syncTried = true;
                if (!_working) _ = SaveAndCloseAsync();
                return;
            }
            _navigation?.TrySetResult(false);
        }

        private void UpdateButtons()
        {
            if (IsDisposed) return;
            bool ready = Core != null && !_working && _mode != YouTubeLoginMode.SignOut;
            _save.Enabled = ready && _signedIn;
            _home.Enabled = ready;
            _close.Enabled = !(_working && _mode == YouTubeLoginMode.SignOut);
        }

        private void Status(string text)
        {
            if (!IsDisposed) _status.Text = text;
        }

        private void FitStatus() => _status.MaximumSize = new Drawing.Size(Math.Max(200, ClientSize.Width - 30), 0);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _poll.Dispose();
                _browser.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
