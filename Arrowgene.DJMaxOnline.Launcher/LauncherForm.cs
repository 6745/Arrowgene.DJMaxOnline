using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using Arrowgene.DJMaxOnline.Updater;

namespace Arrowgene.DJMaxOnline.Launcher;

/// <summary>
/// The launcher: a flat dark panel with an account card, a news card, and an updater that
/// checks the configured site's checksum list before letting the client start.
/// Everything is owner-drawn with GDI+, so there are no assets to ship alongside it.
///
/// The login flow underneath is unchanged - same fields, same config file, same ticket
/// handshake.
/// </summary>
internal sealed class LauncherForm : Form
{
    private const int TitleBarHeight = 38;

    private static Rectangle AccountCard = new(16, 52, 320, 316);
    private static Rectangle NewsCard = new(348, 52, 316, 316);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly string _configPath = LauncherConfig.DefaultPath;

    /// <summary>
    /// The config exactly as it was loaded. Saving mutates THIS and writes it back, rather
    /// than building a fresh one from a hand-written field list - that list silently reset
    /// every setting missing from it, so a hand-edited loginUrl was erased on exit.
    /// </summary>
    private readonly LauncherConfig _config;
    private readonly string _loginUrl;
    private readonly string _updateSource;
    private readonly string _manifestName;
    private readonly string _newsFileName;

    /// <summary>Why the configured update site is unusable, or null when it is fine.</summary>
    private readonly string? _updateSourceError;
    private readonly string _localeEmulator;
    private readonly string _localeEmulatorArgs;
    private readonly int _localeCodePage;
    private readonly string _localeName;
    private readonly string _localeProfile;
    private readonly bool _passTicketArgs;

    private readonly ModernField _account = new();
    private readonly ModernField _password = new(password: true);
    private readonly ModernField _gamePath = new();
    private readonly ModernCheck _saveCredentials = new();
    private readonly ModernCheck _showPassword = new();
    private readonly ModernCheck _windowed = new();
    private readonly ModernCombo _resolution = new();
    /// <summary>Field captions, placed by PositionControls and drawn by OnPaint.</summary>
    private readonly List<(string Caption, Rectangle Bounds)> _captions = [];
    private readonly ModernButton _browseButton = new(ButtonKind.Neutral);
    private readonly ModernButton _updateButton = new(ButtonKind.Ghost);
    private readonly ModernButton _loginButton = new(ButtonKind.Accent);
    private readonly ModernButton _cancelButton = new(ButtonKind.Ghost);
    private readonly ModernProgress _overallProgress = new();
    private readonly ModernProgress _fileProgress = new();
    private readonly NewsPanel _news = new();
    private readonly Label _statusLabel = new();
    private readonly System.Windows.Forms.Timer _pulse = new();

    private CancellationTokenSource? _work;
    private UpdatePlan? _pending;
    private bool _busy;

    public LauncherForm(LauncherStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        LauncherConfig config;
        string? configError = null;
        try
        {
            config = LauncherConfig.Load(_configPath);
        }
        catch (Exception ex)
        {
            config = new LauncherConfig();
            configError = $"Could not read {LauncherConfig.FileName}: {ex.Message}";
        }

        _config = config;
        _loginUrl = !string.IsNullOrWhiteSpace(options.LoginUrl)
            ? options.LoginUrl.Trim()
            : config.LoginUrl.Trim();
        _manifestName = config.UpdateManifest;
        _localeEmulator = config.LocaleEmulator;
        _localeEmulatorArgs = config.LocaleEmulatorArgs;
        _localeCodePage = config.LocaleCodePage;
        _localeName = config.LocaleName;
        _localeProfile = config.LocaleProfile;
        _passTicketArgs = config.PassTicketArgs;
        _newsFileName = config.UpdateNews;
        // Web only: no local-folder fallback. Updates come from the server so every
        // player checks the same published hashes against the same published files.
        _updateSource = !string.IsNullOrWhiteSpace(options.UpdateSource)
            ? options.UpdateSource.Trim()
            : config.UpdateUrl.Trim();
        _updateSourceError = LauncherConfig.ValidateUpdateSource(_updateSource);

        Text = "DJMAX Online Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;   // HTCAPTION would otherwise maximise on double-click
        // The chrome is hand-painted at fixed pixel coordinates, so auto-scaling would
        // move the controls out from under the cards drawn behind them.
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(680, 486);   // height is recomputed by PositionControls
        BackColor = ModernTheme.Ground;
        ForeColor = ModernTheme.Text;
        Font = ModernTheme.Ui();
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        KeyPreview = true;

        BuildLayout();

        _account.Box.Text = options.AccountId ?? config.AccountId;
        _password.Box.Text = config.Password;
        _saveCredentials.Checked = config.SaveCredentials;
        _windowed.Checked = config.Windowed;
        _gamePath.Box.TextChanged += (_, _) => RefreshResolutionOptions();
        _gamePath.Box.Text =
            ResolveInitialGamePath(options.GamePath, config.GamePath) ?? string.Empty;
        SetStatus(configError ?? "Ready.",
            configError == null ? ModernTheme.TextDim : ModernTheme.Alert);

        AcceptButton = _loginButton;
        CancelButton = _cancelButton;
        // Borderless forms swallow Escape unless the form itself watches for it.
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && !_busy)
            {
                Close();
            }
        };

        _pulse.Interval = 33;
        _pulse.Tick += (_, _) =>
        {
            _overallProgress.Step();
            _fileProgress.Step();
        };
        _pulse.Start();

        Shown += async (_, _) =>
        {
            if (_account.Box.TextLength == 0)
            {
                _account.Box.Focus();
            }
            else if (_password.Box.TextLength == 0)
            {
                _password.Box.Focus();
            }
            else
            {
                _loginButton.Focus();
            }

            // News first and unawaited: the update check hashes the whole install, and
            // the card should not sit on placeholder text for the length of that.
            _ = RefreshNewsAsync();
            await CheckForUpdatesAsync(quiet: true);
        };
        FormClosing += (_, _) =>
        {
            _pulse.Stop();
            _work?.Cancel();
            SaveCurrentConfig(showError: false);
        };
    }

    // ------------------------------------------------------------------- layout

    private void BuildLayout()
    {
        _password.Box.MaxLength = LoginClient.MaximumPasswordLength;
        _account.Box.MaxLength = LoginClient.MaximumAccountLength;

        _browseButton.Text = "Browse";
        _browseButton.Font = ModernTheme.Ui(8.5F);
        _browseButton.BackColor = ModernTheme.Card;   // this one sits inside a card
        _browseButton.Click += BrowseForGame;

        _showPassword.Text = "Show password";
        _showPassword.CheckedChanged += (_, _) =>
            _password.Box.UseSystemPasswordChar = !_showPassword.Checked;

        _saveCredentials.Text = "Remember me (saved as plain text)";
        _windowed.Text = "Windowed mode";
        _resolution.SelectedIndexChanged += (_, _) =>
            _config.Resolution = SelectedResolution();

        _updateButton.Text = "Check for Updates";
        _updateButton.Font = ModernTheme.Ui(8.5F, FontStyle.Bold);
        _updateButton.Click += UpdateButtonClickAsync;

        _loginButton.Text = "Start Game";
        _loginButton.Click += LoginAndLaunchAsync;

        _cancelButton.Text = "Exit";
        _cancelButton.Click += (_, _) => Close();

        _statusLabel.AutoSize = false;
        // Opaque for the same reason the buttons are: the text changes constantly, and a
        // transparent label leaves the previous message behind it.
        _statusLabel.BackColor = ModernTheme.Ground;
        _statusLabel.Font = ModernTheme.Ui(8.5F);
        _statusLabel.ForeColor = ModernTheme.TextDim;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        _fileProgress.Fill = ModernTheme.AccentDim;
        _news.SetEntries(LoadNews());

        Controls.AddRange(
        [
            _account, _password, _gamePath, _showPassword, _windowed, _resolution,
            _saveCredentials,
            _browseButton, _updateButton, _loginButton, _cancelButton, _statusLabel,
            _overallProgress, _fileProgress, _news
        ]);

        PositionControls();
        RefreshResolutionOptions();
    }


    /// <summary>
    /// Scaled window sizes offered in the drop-down, all 4:3 so the client's own 800x600
    /// layout is never letterboxed or distorted.
    ///
    /// Non-integer multiples are included because a whole 2x does not fit a 1080p desktop.
    /// They do scale less evenly - the picture is stretched with point sampling, so at 1.6x
    /// some source pixels cover two screen pixels and some one - but 1.6x reads far better
    /// than 1.5x, whose 3/2 ratio doubles every other pixel and produces a hard stripe.
    /// </summary>
    private static readonly (string Label, int Width, int Height)[] ScaleOptions =
    [
        ("Native 800 x 600", 800, 600),
        ("1024 x 768  (128%)", 1024, 768),
        ("1280 x 960  (160%, recommended)", 1280, 960),
        ("1440 x 1080  (180%)", 1440, 1080),
        ("1600 x 1200  (200%, exact 2x)", 1600, 1200),
        ("1920 x 1440  (240%)", 1920, 1440),
        ("2048 x 1536  (256%)", 2048, 1536),
        ("2560 x 1920  (320%)", 2560, 1920),
        ("2880 x 2160  (360%, fills 4K height)", 2880, 2160),
        ("3200 x 2400  (400%, exact 4x)", 3200, 2400)
    ];

    /// <summary>
    /// Whether dinput.dll sits beside the game.
    ///
    /// Windowed mode and the upscale are both implemented by that DLL, not by the client.
    /// It normally arrives with an update, but if it is missing the launcher must not pass
    /// -windowed or -scale: - the client would receive flags nothing in the process
    /// understands.
    /// </summary>
    private static bool HasClientDll(string gamePath)
    {
        string? directory = Path.GetDirectoryName(gamePath);
        return directory != null && File.Exists(Path.Combine(directory, "dinput.dll"));
    }

    /// <summary>
    /// Fills the resolution list, hiding sizes that would not fit on screen, and disables
    /// the whole thing when the DLL that implements scaling is not present.
    /// </summary>
    private void RefreshResolutionOptions()
    {
        string gamePath = _gamePath.Box.Text.Trim();
        bool available = gamePath.Length != 0 && File.Exists(gamePath) &&
                         HasClientDll(gamePath);
        // TEMPORARY: listing every size regardless of what the monitor can show, so
        // oversized windows can be tested deliberately. Set this back to true to hide
        // sizes that do not fit - without it the launcher will happily open a window
        // larger than the screen.
        const bool filterToWorkArea = false;
        Rectangle work = Screen.FromControl(this).WorkingArea;
        string wanted = _resolution.SelectedItem?.ToString() ?? _config.Resolution;

        _resolution.BeginUpdate();
        _resolution.Items.Clear();
        foreach ((string label, int width, int height) in ScaleOptions)
        {
            // Allow native always; otherwise only what actually fits, with room for the
            // title bar and border, so the launcher cannot produce an off-screen window.
            if (!filterToWorkArea || width == 800 ||
                (width <= work.Width && height + 60 <= work.Height))
            {
                _resolution.Items.Add(label);
            }
        }
        _resolution.EndUpdate();

        int index = 0;
        for (int i = 0; i < _resolution.Items.Count; i++)
        {
            string label = _resolution.Items[i]?.ToString() ?? string.Empty;
            if (label == wanted || LabelMatchesSize(label, _config.Resolution))
            {
                index = i;
                break;
            }
        }
        _resolution.SelectedIndex = _resolution.Items.Count == 0 ? -1 : index;

        _resolution.Enabled = available;
        _windowed.Enabled = available;
    }

    private static bool LabelMatchesSize(string label, string size)
    {
        if (size.Length == 0)
        {
            return false;
        }
        foreach ((string candidate, int width, int height) in ScaleOptions)
        {
            if (candidate == label)
            {
                return string.Equals(size, $"{width}x{height}",
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    /// <summary>The selected size as WIDTHxHEIGHT, or empty for native.</summary>
    private string SelectedResolution()
    {
        string label = _resolution.SelectedItem?.ToString() ?? string.Empty;
        foreach ((string candidate, int width, int height) in ScaleOptions)
        {
            if (candidate == label)
            {
                return width == 800 ? string.Empty : $"{width}x{height}";
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Absolute placement rather than nested layout panels: the panels paint their own
    /// backgrounds and would punch opaque rectangles through the drawn cards.
    /// </summary>
    /// <summary>
    /// Places every control in one downward pass and sizes the cards and the window from
    /// where it ends up.
    ///
    /// This used to be a list of absolute coordinates, with the field captions carrying a
    /// second, separate set inside OnPaint. Adding the resolution row meant editing both
    /// and adjusting six unrelated numbers, and missing one left "Check for Updates"
    /// painting its card-coloured rectangle behind the Remember me checkbox. A running
    /// cursor removes the class of bug: a row can be inserted or resized and everything
    /// below - including the card, the buttons and the window itself - follows.
    /// </summary>
    private void PositionControls()
    {
        const int left = 32;
        const int width = 288;
        const int field = 30;      // text well
        const int check = 18;      // checkbox row
        const int caption = 18;    // label above a well
        const int afterCaption = 4;
        const int betweenRows = 14;
        const int cardPadding = 16;

        _captions.Clear();
        int y = AccountCard.Y + 40;   // clear of the "ACCOUNT" section title

        void Caption(string text)
        {
            _captions.Add((text, new Rectangle(left, y, width, caption)));
            y += caption + afterCaption;
        }

        Caption("ID");
        _account.SetBounds(left, y, width, field);
        y += field + betweenRows;

        Caption("Password");
        _password.SetBounds(left, y, width, field);
        y += field + 8;

        _showPassword.SetBounds(left, y, 126, check);
        _windowed.SetBounds(left + 130, y, width - 130, check);
        y += check + betweenRows;

        Caption("Game");
        _gamePath.SetBounds(left, y, width - 98, field);
        _browseButton.SetBounds(left + width - 90, y, 90, field);
        y += field + betweenRows;

        Caption("Resolution");
        _resolution.SetBounds(left, y, width, 26);
        y += 26 + betweenRows;

        _saveCredentials.SetBounds(left, y, width, check);
        y += check;

        // The card ends below the last row, and the news card matches it.
        AccountCard = AccountCard with { Height = y + cardPadding - AccountCard.Y };
        NewsCard = NewsCard with { Height = AccountCard.Height };
        _news.SetBounds(NewsCard.X + 16, NewsCard.Y + 40, NewsCard.Width - 32,
            NewsCard.Height - 56);

        // Everything below is measured from the bottom of the cards, not from a constant.
        int belowCards = AccountCard.Bottom + 18;
        _updateButton.SetBounds(16, belowCards, 176, 36);
        _loginButton.SetBounds(ClientSize.Width - 148, belowCards, 132, 36);
        _cancelButton.SetBounds(ClientSize.Width - 148 - 104, belowCards, 96, 36);

        int belowButtons = belowCards + 36 + 12;
        _statusLabel.SetBounds(16, belowButtons, ClientSize.Width - 130, 18);
        _overallProgress.SetBounds(16, belowButtons + 24, ClientSize.Width - 32, 6);
        _fileProgress.SetBounds(16, belowButtons + 34, ClientSize.Width - 32, 6);

        ClientSize = new Size(ClientSize.Width, belowButtons + 34 + 6 + 12);
    }

    // -------------------------------------------------------------------- paint

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        ModernTheme.Clear(g, ClientRectangle, ModernTheme.Ground);
        ModernTheme.Smooth(g);

        PaintTitleBar(g);
        ModernTheme.Surface(g, AccountCard, 10F, ModernTheme.Card, ModernTheme.Line);
        ModernTheme.Surface(g, NewsCard, 10F, ModernTheme.Card, ModernTheme.Line);

        ModernTheme.SectionTitle(g, "Account", new Point(AccountCard.X + 16, AccountCard.Y + 16));
        ModernTheme.SectionTitle(g, "News", new Point(NewsCard.X + 16, NewsCard.Y + 16));

        // Captions come from the same pass that placed the controls. They used to be
        // four hardcoded rectangles here, which meant every layout change had to be made
        // in two places and silently drifted when it was not.
        foreach ((string caption, Rectangle bounds) in _captions)
        {
            ModernTheme.Label(g, caption, bounds);
        }


        using Pen frame = new(ModernTheme.Line);
        g.DrawRectangle(frame, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private void PaintTitleBar(Graphics g)
    {
        using (SolidBrush accent = new(ModernTheme.Accent))
        {
            g.FillEllipse(accent, 18, TitleBarHeight / 2 - 4, 8, 8);
        }

        const int titleLeft = 32;
        const string title = "DJMAX Online";

        using Font font = ModernTheme.Ui(9F, FontStyle.Bold);
        TextRenderer.DrawText(g, title, font,
            new Rectangle(titleLeft, 0, 300, TitleBarHeight), ModernTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // The version sits immediately after the title. Its x is MEASURED rather
        // than hardcoded, so changing the title or the font moves it correctly
        // instead of leaving it overlapping or floating.
        Size titleSize = TextRenderer.MeasureText(g, title, font,
            new Size(300, TitleBarHeight), TextFormatFlags.NoPadding);
        using Font versionFont = ModernTheme.Ui(8F);
        string version =
            typeof(LauncherForm).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        TextRenderer.DrawText(g, $"v{version}", versionFont,
            new Rectangle(titleLeft + titleSize.Width + 8, 0, 80, TitleBarHeight),
            ModernTheme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        PaintGlyph(g, MinimiseButton, minimise: true);
        PaintGlyph(g, CloseGlyphButton, minimise: false);
    }

    private Rectangle MinimiseButton => new(ClientSize.Width - 64, 10, 20, 18);
    private Rectangle CloseGlyphButton => new(ClientSize.Width - 36, 10, 20, 18);

    private void PaintGlyph(Graphics g, Rectangle area, bool minimise)
    {
        bool hot = area.Contains(PointToClient(MousePosition));
        Color ink = hot ? ModernTheme.Text : ModernTheme.TextFaint;
        using Pen pen = new(ink, 1.4F) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        float cx = area.X + area.Width / 2F;
        float cy = area.Y + area.Height / 2F;
        if (minimise)
        {
            g.DrawLine(pen, cx - 5, cy + 3, cx + 5, cy + 3);
            return;
        }

        g.DrawLine(pen, cx - 4.5F, cy - 4.5F, cx + 4.5F, cy + 4.5F);
        g.DrawLine(pen, cx + 4.5F, cy - 4.5F, cx - 4.5F, cy + 4.5F);
    }

    // ------------------------------------------------------------------ updates

    private UpdateService CreateUpdateService() =>
        new(UpdateService.CreateTransport(_updateSource, Http), _manifestName);

    private string? GameDirectory()
    {
        string path = _gamePath.Box.Text.Trim();
        return path.Length == 0 ? null : Path.GetDirectoryName(Path.GetFullPath(path));
    }

    private async void UpdateButtonClickAsync(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pending is { UpToDate: false })
        {
            await DownloadUpdatesAsync(_pending);
            return;
        }

        await CheckForUpdatesAsync(quiet: false);
    }

    /// <summary>
    /// Fetches the checksum list and hashes the local files against it. A quiet check is
    /// the one that runs at start-up: it never pops a dialog, because a missing update
    /// source is the normal case for someone running purely offline.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool quiet)
    {
        if (_updateSourceError != null)
        {
            // Misconfiguration, not a transient failure - say so plainly rather than
            // letting it surface later as an obscure download error.
            SetStatus(_updateSourceError, quiet ? ModernTheme.TextFaint : ModernTheme.Alert);
            if (!quiet)
            {
                ShowError(_updateSourceError);
            }
            return;
        }

        string? gameDirectory = GameDirectory();
        if (gameDirectory == null || !Directory.Exists(gameDirectory))
        {
            if (!quiet)
            {
                ShowError("Select DJMax.exe first so the updater knows which folder to patch.");
            }
            return;
        }

        SetBusy(true);
        _updateButton.Text = "Checking...";
        _overallProgress.Marquee = true;
        SetStatus($"Checking for updates from {_updateSource}", ModernTheme.TextDim);

        try
        {
            _work = new CancellationTokenSource();
            UpdateService service = CreateUpdateService();
            UpdateManifest manifest = await service.FetchManifestAsync(_work.Token);
            // Hashing every listed file is slow enough to matter; keep it off the UI thread.
            UpdatePlan plan = await Task.Run(
                () => UpdateService.Plan(manifest, gameDirectory), _work.Token);

            _pending = plan;
            if (plan.UpToDate)
            {
                _updateButton.Text = "Check for Updates";
                SetStatus($"Up to date - {plan.Examined} file(s) verified.", ModernTheme.Ok);
            }
            else
            {
                _updateButton.Kind = ButtonKind.Neutral;
                _updateButton.Text = $"Download {plan.Actions.Count} file(s)";
                SetStatus(
                    $"{plan.Actions.Count} of {plan.Examined} file(s) need updating.",
                    ModernTheme.Accent);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Update check cancelled.", ModernTheme.TextDim);
        }
        catch (Exception ex)
        {
            _updateButton.Text = "Check for Updates";
            // No manifest at all is the normal offline case, not a failure worth shouting
            // about on the quiet start-up check.
            bool missingSource = ex is FileNotFoundException or DirectoryNotFoundException;
            if (quiet)
            {
                SetStatus(
                    missingSource
                        ? "No update source configured."
                        : $"Update check failed: {Describe(ex)}",
                    ModernTheme.TextFaint);
            }
            else
            {
                ShowError($"Update check failed: {Describe(ex)}");
            }
        }
        finally
        {
            _overallProgress.Marquee = false;
            _overallProgress.Value = 0F;
            _overallProgress.Invalidate();
            SetBusy(false);
        }
    }

    private async Task DownloadUpdatesAsync(UpdatePlan plan)
    {
        string? gameDirectory = GameDirectory();
        if (gameDirectory == null)
        {
            return;
        }

        SetBusy(true);
        _updateButton.Text = "Downloading...";
        Progress<UpdateProgress> progress = new(report =>
        {
            _overallProgress.Value = report.OverallFraction;
            _overallProgress.Invalidate();
            _fileProgress.Value = report.FileFraction;
            _fileProgress.Invalidate();
            SetStatus(
                $"[{Math.Min(report.FileIndex + 1, report.FileCount)}/{report.FileCount}] " +
                report.Path,
                ModernTheme.TextDim);
        });

        try
        {
            _work = new CancellationTokenSource();
            UpdateService service = CreateUpdateService();
            await service.ApplyAsync(plan, gameDirectory, progress, _work.Token);

            // Each file was checksummed as it streamed in, but that only proves the
            // transfer was clean. Re-fetch the list and re-hash the whole install so the
            // launcher can say the game matches what the server publishes - which is the
            // claim that actually matters before letting someone log in.
            SetStatus("Verifying files...", ModernTheme.TextDim);
            _overallProgress.Marquee = true;
            UpdateManifest manifest = await service.FetchManifestAsync(_work.Token);
            UpdatePlan verified = await Task.Run(
                () => UpdateService.Plan(manifest, gameDirectory), _work.Token);
            _overallProgress.Marquee = false;

            _updateButton.Kind = ButtonKind.Ghost;
            _updateButton.Text = "Check for Updates";
            _overallProgress.Value = 1F;
            _fileProgress.Value = 1F;

            if (verified.UpToDate)
            {
                _pending = null;
                SetStatus(
                    $"Updated {plan.Actions.Count} file(s) - {verified.Examined} verified.",
                    ModernTheme.Ok);
            }
            else
            {
                // Something is rewriting these files, or the site changed mid-download.
                // Leaving the plan in place means the next click retries exactly these.
                _pending = verified;
                _updateButton.Kind = ButtonKind.Neutral;
                _updateButton.Text = $"Retry {verified.Actions.Count} file(s)";
                SetStatus(
                    $"{verified.Actions.Count} file(s) still do not match after updating.",
                    ModernTheme.Alert);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Download cancelled.", ModernTheme.TextDim);
        }
        catch (Exception ex)
        {
            _updateButton.Text = $"Download {plan.Actions.Count} file(s)";
            ShowError($"Update failed: {Describe(ex)}");
        }
        finally
        {
            _overallProgress.Invalidate();
            _fileProgress.Invalidate();
            SetBusy(false);
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException =>
            "the update source does not have a checksum list.",
        HttpRequestException http => $"the update site could not be reached ({http.Message}).",
        _ => ex.Message
    };

    // --------------------------------------------------------------------- news

    /// <summary>
    /// Replaces the placeholder with what the update site publishes, or says plainly that
    /// the site could not be reached.
    ///
    /// The card must never keep showing cheerful stand-in text when the fetch failed:
    /// "Server online - SEOUL and TOKYO are up" is a claim, and displaying it while the
    /// server is unreachable tells the player the opposite of the truth. What went wrong
    /// is the news, so it goes on the card.
    /// </summary>
    private async Task RefreshNewsAsync()
    {
        if (_updateSourceError != null)
        {
            ShowNews(Notice("Update site not configured", _updateSourceError));
            return;
        }
        if (string.IsNullOrWhiteSpace(_newsFileName))
        {
            ShowNews(Notice("News is switched off",
                $"No news file is configured. Set updateNews in {LauncherConfig.FileName} " +
                "to show announcements here."));
            return;
        }

        try
        {
            IReadOnlyList<NewsItem> items = await NewsFeed.FetchAsync(
                UpdateService.CreateTransport(_updateSource, Http), _newsFileName);

            ShowNews(items.Count > 0
                ? [.. items.Select(item =>
                    new NewsPanel.Entry(item.Headline, item.Date, item.Body))]
                // The site answered, so it IS up - it just has nothing posted.
                : Notice("No news posted",
                    "The server is reachable but has not published any announcements."));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A 404 is still an answer: something is serving, the file is just absent.
            ShowNews(Notice("No news file",
                $"The server is reachable but is not serving {_newsFileName}."));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not fetch news from {_updateSource}: {ex}");
            ShowNews(Notice("Cannot reach the server",
                $"No answer from {_updateSource}. Is the server online? " +
                "Check that it is running and that updateUrl points at it."));
        }
    }

    /// <summary>Pushes entries onto the card unless the form is already gone.</summary>
    private void ShowNews(IReadOnlyList<NewsPanel.Entry> entries)
    {
        if (!IsDisposed)
        {
            _news.SetEntries(entries);
        }
    }

    /// <summary>A single-item card - a status message rather than actual news.</summary>
    private static NewsPanel.Entry[] Notice(string headline, string body) =>
        [new NewsPanel.Entry(headline, string.Empty, body)];

    /// <summary>
    /// What the card shows for the moment before the fetch returns.
    ///
    /// Deliberately says nothing about the state of the server: it is replaced either way,
    /// and until then the honest answer is that we do not know yet.
    /// </summary>
    private static IReadOnlyList<NewsPanel.Entry> LoadNews() =>
        Notice("Loading news", "Contacting the server...");

    // ---------------------------------------------------- borderless behaviour

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        if (CloseGlyphButton.Contains(e.Location))
        {
            Close();
            return;
        }
        if (MinimiseButton.Contains(e.Location))
        {
            WindowState = FormWindowState.Minimized;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Y < TitleBarHeight)
        {
            Invalidate(new Rectangle(ClientSize.Width - 80, 0, 80, TitleBarHeight));
        }

        base.OnMouseMove(e);
    }

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtCaption = 2;

    /// <summary>
    /// Reports the caption strip as the window caption, which hands dragging to Windows -
    /// so snapping, shake and multi-monitor all behave exactly like a normal window,
    /// without any P/Invoke. The glyphs stay client area so they keep their clicks.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmNcHitTest || m.Result != HtClient)
        {
            return;
        }

        Point point = PointToClient(new Point(m.LParam.ToInt32()));
        if (point.Y < TitleBarHeight &&
            !MinimiseButton.Contains(point) &&
            !CloseGlyphButton.Contains(point))
        {
            m.Result = HtCaption;
        }
    }

    // --------------------------------------------------------------- behaviour

    private async void LoginAndLaunchAsync(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string accountId = _account.Box.Text.Trim();
        string password = _password.Box.Text;
        string gamePath;
        try
        {
            if (accountId.Length == 0)
            {
                throw new InvalidOperationException("Enter an account ID.");
            }
            if (password.Length == 0)
            {
                throw new InvalidOperationException("Enter a password.");
            }
            gamePath = Path.GetFullPath(_gamePath.Box.Text.Trim());
            if (!File.Exists(gamePath))
            {
                throw new FileNotFoundException("DJMax.exe was not found at the selected path.");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }

        if (_pending is { UpToDate: false } outstanding &&
            MessageBox.Show(
                this,
                $"{outstanding.Actions.Count} file(s) are out of date. Start anyway?",
                "DJMAX Launcher",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        bool closeAfterLaunch = false;
        SetBusy(true);
        _overallProgress.Marquee = true;
        SetStatus("Authenticating with the server...", ModernTheme.TextDim);
        try
        {
            LoginResponse response = await LoginClient.AuthenticateAsync(
                Http, _loginUrl, accountId, password);
            if (!response.Success ||
                response.Token is not { Length: LoginClient.TokenLength } token)
            {
                ShowError(response.Error ?? "Login was rejected.");
                return;
            }

            _gamePath.Box.Text = gamePath;
            SaveCurrentConfig(showError: true);
            StartGame(gamePath, token, _windowed.Checked, SelectedResolution(),
                _localeEmulator, _localeEmulatorArgs,
                _localeCodePage, _localeName, _localeProfile, _passTicketArgs);
            SetStatus($"Connected. Ticket expires in {response.ExpiresInSeconds}s.",
                ModernTheme.Ok);
            closeAfterLaunch = true;
        }
        catch (OperationCanceledException)
        {
            ShowError("The login server did not respond. Start the DJMAX server first.");
        }
        catch (TimeoutException)
        {
            ShowError("The login server did not respond. Start the DJMAX server first.");
        }
        catch (IOException ex)
        {
            ShowError($"Could not contact the DJMAX server: {ex.Message}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _overallProgress.Marquee = false;
            _overallProgress.Invalidate();
            SetBusy(false);
        }

        if (closeAfterLaunch)
        {
            Close();
        }
    }

    private void BrowseForGame(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select DJMax.exe",
            Filter = "DJMAX executable (DJMax.exe)|DJMax.exe|Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        string current = _gamePath.Box.Text.Trim();
        if (File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(current));
            dialog.FileName = Path.GetFileName(current);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _gamePath.Box.Text = dialog.FileName;
            // A different install means the previous plan no longer describes this folder.
            _pending = null;
            _updateButton.Kind = ButtonKind.Ghost;
            _updateButton.Text = "Check for Updates";
        }
    }

    private void SaveCurrentConfig(bool showError)
    {
        try
        {
            // Only the fields this window actually edits. Everything else - loginUrl,
            // updateNews, the locale settings - keeps whatever was loaded, so hand-editing
            // the file survives a launcher run.
            _config.SaveCredentials = _saveCredentials.Checked;
            _config.AccountId =
                _saveCredentials.Checked ? _account.Box.Text.Trim() : string.Empty;
            _config.Password = _saveCredentials.Checked ? _password.Box.Text : string.Empty;
            _config.GamePath = _gamePath.Box.Text.Trim();
            _config.Windowed = _windowed.Checked;
            _config.Resolution = SelectedResolution();
            _config.Save(_configPath);
        }
        catch (Exception ex) when (!showError)
        {
            Debug.WriteLine($"Could not save launcher config: {ex}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"The game can still launch, but {LauncherConfig.FileName} could not be saved:\n\n" +
                ex.Message,
                "DJMAX Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Starts the client, optionally through a locale emulator.
    ///
    /// The Korean client is a pure ANSI application: it converts its CP949 text with the
    /// SYSTEM ANSI codepage (GetACP / MultiByteToWideChar), so on a non-Korean Windows the
    /// Korean strings come out as mojibake. Windows has no way to give a child process a
    /// different ANSI codepage - the manifest only offers UTF-8 or Legacy - so the only
    /// per-process fix is to start the game under a tool that hooks those conversions.
    /// </summary>
    private static void StartGame(
        string gamePath, string token, bool windowed, string resolution,
        string localeEmulator, string localeEmulatorArgs,
        int localeCodePage, string localeName, string localeProfile,
        bool passTicketArgs)
    {
        // sub_4B0C30 retains the third lpCmdLine argument when ConnectFromNM=0. The SNDA
        // (china260) client has no such handling - passing it there makes WSAConnect fail
        // (WSAEADDRNOTAVAIL) before any packet reaches the server. See PassTicketArgs.
        List<string> arguments = passTicketArgs ? ["local", "ticket", token] : [];

        // -windowed and -scale: are both handled by dinput.dll, not by the client. It
        // ships with the update, but if it is absent neither flag would be read by
        // anything, so add neither rather than leaving stray arguments on the command
        // line of a client that never asked for them.
        if (HasClientDll(gamePath))
        {
            if (windowed)
            {
                arguments.Add("-windowed");
            }
            if (resolution.Length != 0)
            {
                arguments.Add("-scale:" + resolution);
            }
        }

        if (string.IsNullOrWhiteSpace(localeEmulator))
        {
            ProcessStartInfo direct = new(gamePath)
            {
                WorkingDirectory = Path.GetDirectoryName(gamePath)!,
                UseShellExecute = true
            };
            foreach (string argument in arguments)
            {
                direct.ArgumentList.Add(argument);
            }
            _ = Process.Start(direct) ??
                throw new InvalidOperationException("DJMax.exe did not start.");
            return;
        }

        string emulator = Path.GetFullPath(localeEmulator);
        if (!File.Exists(emulator))
        {
            throw new FileNotFoundException(
                "The configured locale emulator was not found:" +
                Environment.NewLine + Environment.NewLine + emulator +
                Environment.NewLine + Environment.NewLine +
                $"Clear localeEmulator in {LauncherConfig.FileName} to launch the game " +
                "directly.");
        }

        // A {profile} with nothing to put in it would produce "-runas  <game> ...", which
        // Locale Emulator answers by exiting 0 and starting nothing at all - the least
        // debuggable failure there is. Say what is missing instead.
        if (localeEmulatorArgs.Contains("{profile}", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(localeProfile))
        {
            throw new InvalidOperationException(
                "localeEmulatorArgs uses {profile} but localeProfile is empty." +
                Environment.NewLine + Environment.NewLine +
                "Locale Emulator takes the game's arguments from a saved profile, so it " +
                "needs one to run with. Open LEGUI.exe, create a KOREAN (ko-KR) profile, " +
                $"and copy its Guid from LEConfig.xml into localeProfile in {LauncherConfig.FileName}.");
        }

        // The template decides the quoting, because each tool spells its command line
        // differently - LEProc takes "-runas <guid> <exe> <args>", ntleas takes
        // "<exe> <args> -cp:949".
        string commandLine = localeEmulatorArgs
            .Replace("{profile}", localeProfile.Trim(), StringComparison.Ordinal)
            .Replace("{game}", Quote(gamePath), StringComparison.Ordinal)
            .Replace("{args}", string.Join(' ', arguments.Select(Quote)),
                StringComparison.Ordinal)
            .Replace("{codepage}", localeCodePage.ToString(
                System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{locale}", localeName, StringComparison.Ordinal);

        ProcessStartInfo through = new(emulator)
        {
            // The emulator launches the game, so the game still has to see its own folder.
            WorkingDirectory = Path.GetDirectoryName(gamePath)!,
            Arguments = commandLine,
            UseShellExecute = true
        };
        _ = Process.Start(through) ??
            throw new InvalidOperationException(
                $"{Path.GetFileName(emulator)} did not start.");
    }

    /// <summary>Quotes only when needed, and never doubles an existing pair.</summary>
    internal static string Quote(string value)
    {
        const char quote = '"';
        if (value.Length != 0 && !value.Contains(' '))
        {
            return value;
        }
        return quote + value.Trim(quote) + quote;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loginButton.Enabled = !busy;
        _updateButton.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _account.Box.Enabled = !busy;
        _password.Box.Enabled = !busy;
        UseWaitCursor = busy;
        if (!busy)
        {
            _fileProgress.Value = 0F;
            _fileProgress.Invalidate();
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.ForeColor = color;
        _statusLabel.Text = text;
    }

    private void ShowError(string message)
    {
        SetStatus(message, ModernTheme.Alert);
        MessageBox.Show(
            this,
            message,
            "DJMAX Launcher",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string? ResolveInitialGamePath(string? option, string configured)
    {
        string? value = string.IsNullOrWhiteSpace(option)
            ? string.IsNullOrWhiteSpace(configured) ? FindGameExecutable() : configured
            : option;
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }

    private static string? FindGameExecutable()
    {
        foreach (string start in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (DirectoryInfo? directory = new(start); directory != null;
                 directory = directory.Parent)
            {
                foreach (string candidate in new[]
                         {
                             Path.Combine(directory.FullName, "DJMax.exe"),
                             Path.Combine(directory.FullName, "client", "DJMax.exe")
                         })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        return null;
    }
}
