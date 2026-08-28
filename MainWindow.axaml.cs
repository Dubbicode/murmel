using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Murmel.Models;
using Murmel.Services;

namespace Murmel;

public partial class MainWindow : Window
{
    private bool _isRecording;
    private bool _isModelReady;
    private IntPtr _foregroundWindowBeforeRecording;

    // Tracked explicitly rather than reading the Window's own IsVisible property:
    // that property can lag a beat behind an immediately-preceding Hide()/Show() call,
    // which made the background indicator unreliable. We flip this ourselves at the
    // exact moment we hide/show the window, so it's always accurate.
    private bool _mainWindowVisible = true;

    // Separate from the above: whether the main window currently has focus. Dictating
    // into another app leaves Murmel's window open but not focused ("im Hintergrund"),
    // which is exactly when the small return-to-app indicator should show up too -
    // not just when the window is fully minimized or hidden.
    private bool _mainWindowFocused = true;

    private readonly GlobalHotkeyService _hotkey = new();
    private readonly HistoryStore _history = new();
    private readonly AppSettingsStore _settings = new();
    private readonly StatsStore _stats = new();
    private readonly ModelManager _models = new();
    private readonly AudioRecorder _audioRecorder = new();
    private ParakeetTranscriber? _transcriber;
    private readonly RecordingIndicatorWindow _indicator = new();

    // Drives the recorder card's idle "breathing" pulse and the two expanding ping
    // rings behind the mic button - a gentler, always-on echo of the design mockup's
    // CSS animation (a DispatcherTimer loop rather than XAML Animation/keyframes,
    // since we can't test real Avalonia keyframe behavior on the user's Windows PC
    // and this project has already been bitten once by an untested rendering approach).
    private readonly DispatcherTimer _micAnimTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private double _micAnimElapsed;
    private const double MicAnimPeriodSeconds = 2.4;

    // Refreshes the "vor X Minuten" relative-time text on the stat card periodically,
    // so it doesn't go stale while the Recorder page is just sitting open.
    private readonly DispatcherTimer _statsRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public MainWindow()
    {
        InitializeComponent();

        RecordBtn.Click += (_, _) => ToggleRecording();
        CopyBtn.Click += OnCopyClicked;
        ClearBtn.Click += (_, _) => TextEditor.Text = string.Empty;
        ThemeToggleBtn.Click += OnThemeToggleClicked;

        TabRecorderBtn.Click += (_, _) => ShowPage(RecorderPage, TabRecorderBtn, TabRecorderUnderline);
        TabHistoryBtn.Click += (_, _) => ShowPage(HistoryPage, TabHistoryBtn, TabHistoryUnderline);
        TabSettingsBtn.Click += (_, _) => ShowPage(SettingsPage, TabSettingsBtn, TabSettingsUnderline);
        ClearHistoryBtn.Click += (_, _) => { _history.Clear(); RefreshHistoryList(); UpdateStats(); };

        ResetIndicatorPositionBtn.Click += (_, _) =>
        {
            _settings.Data.IndicatorPositionX = null;
            _settings.Data.IndicatorPositionY = null;
            _settings.Save();
            SyncIndicatorVisibility();
        };

        // The registry entry itself is the source of truth (rather than a separate
        // settings.json flag), so it stays correct even if someone edits it by hand.
        AutostartToggle.IsChecked = AutostartService.IsEnabled();
        AutostartToggle.IsCheckedChanged += (_, _) =>
            AutostartService.SetEnabled(AutostartToggle.IsChecked ?? false);

        AutoPasteToggle.IsChecked = _settings.Data.AutoPasteIntoActiveWindow;
        AutoPasteToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.Data.AutoPasteIntoActiveWindow = AutoPasteToggle.IsChecked ?? true;
            _settings.Save();
        };

        // Hotkey preset dropdown: curated modifier combos only (no more free-form
        // single-key capture), since combos are far less likely to clash with
        // shortcuts other apps already use.
        HotkeyPresetCombo.ItemsSource = HotkeyPresetInfo.All.Select(HotkeyPresetInfo.GetDisplayName).ToList();
        HotkeyPresetCombo.SelectedIndex = Array.IndexOf(HotkeyPresetInfo.All, _settings.Data.Hotkey);
        HotkeyPresetCombo.SelectionChanged += (_, _) =>
        {
            int idx = HotkeyPresetCombo.SelectedIndex;
            if (idx < 0 || idx >= HotkeyPresetInfo.All.Length) return;
            var preset = HotkeyPresetInfo.All[idx];
            _settings.Data.Hotkey = preset;
            _settings.Save();
            _hotkey.Preset = preset;
            HotkeyBadgeText.Text = HotkeyPresetInfo.GetDisplayName(preset).ToUpperInvariant();
            if (!_isRecording) StatusText.Text = ReadyStatusText();
        };

        HotkeyBadgeText.Text = HotkeyPresetInfo.GetDisplayName(_settings.Data.Hotkey).ToUpperInvariant();
        StatusText.Text = ReadyStatusText();

        // First run of the lifetime word counter: backfill from whatever history is
        // already on disk (up to 200 entries) so existing users don't start at 0.
        _stats.SeedIfFresh(_history.Entries.Sum(e => CountWords(e.Text)));

        RefreshHistoryList();
        UpdateStats();

        _micAnimTimer.Tick += (_, _) => AnimateMic();
        _micAnimTimer.Start();

        _statsRefreshTimer.Tick += (_, _) => { if (RecorderPage.IsVisible) UpdateStats(); };
        _statsRefreshTimer.Start();

        // Global push-to-talk hotkey: works even while another app is focused.
        _hotkey.Preset = _settings.Data.Hotkey;
        _hotkey.HotkeyPressed += () => Dispatcher.UIThread.Post(StartRecording);
        _hotkey.HotkeyReleased += () => Dispatcher.UIThread.Post(() => _ = StopRecordingAndTranscribeAsync());
        _hotkey.Start();

        // Clicking the small background indicator brings Murmel back to the foreground.
        _indicator.Clicked += () => Dispatcher.UIThread.Post(RestoreMainWindow);

        // Dragging it to a new spot persists that position, so it stays there next time.
        _indicator.PositionChangedByUser += (x, y) => Dispatcher.UIThread.Post(() =>
        {
            _settings.Data.IndicatorPositionX = x;
            _settings.Data.IndicatorPositionY = y;
            _settings.Save();
        });

        Closing += (_, e) =>
        {
            // Minimize to tray instead of quitting, so the hotkey keeps working in the background.
            // Only a real "Beenden" from the tray menu (AllowRealClose) actually exits.
            if (!AllowRealClose)
            {
                e.Cancel = true;
                Hide();
                _mainWindowVisible = false;
                SyncIndicatorVisibility();
            }
        };

        // The normal Windows minimize button (as opposed to closing with X) doesn't fire
        // Closing at all - handle it the same way, so the background indicator shows up
        // no matter which way the user tucks the window away.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty || e.NewValue is not WindowState state) return;

            _mainWindowVisible = state != WindowState.Minimized;
            SyncIndicatorVisibility();
        };

        // Losing focus (e.g. clicking into whatever app you're dictating into) counts as
        // "im Hintergrund" too, even if the window is technically still open and not
        // minimized - that's the most common case while actually using push-to-talk.
        Activated += (_, _) => { _mainWindowFocused = true; SyncIndicatorVisibility(); };
        Deactivated += (_, _) => { _mainWindowFocused = false; SyncIndicatorVisibility(); };

        _audioRecorder.LevelChanged += level => _indicator.ReportLevel(level);

        _ = InitializeModelAsync();
    }

    /// <summary>Set right before the app is really shutting down (tray "Beenden"), so the
    /// Closing handler above doesn't intercept it and just hide the window instead.</summary>
    public bool AllowRealClose { get; set; }

    /// <summary>Called right after the window would normally appear when Murmel was
    /// launched via the "start with Windows" autostart entry - hides it again immediately
    /// so autostart is quiet (tray + small indicator only) instead of popping open.</summary>
    public void StartHiddenInBackground()
    {
        Hide();
        _mainWindowVisible = false;
        SyncIndicatorVisibility();
    }

    /// <summary>Brings the main window back to the foreground - called from the tray menu's
    /// "Fenster öffnen" and from clicking the small background indicator pill.</summary>
    public void RestoreMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _mainWindowVisible = true;
        _mainWindowFocused = true;
        SyncIndicatorVisibility();
    }

    /// <summary>Keeps the small floating indicator in sync: visible (as a full waveform)
    /// while actively recording, visible (as a small static bar) whenever the main window
    /// is hidden, minimized, or simply not focused (e.g. you clicked into another app to
    /// dictate into it), and hidden entirely while the main window is open and focused.</summary>
    private void SyncIndicatorVisibility()
    {
        bool shouldShow = _isRecording || !_mainWindowVisible || !_mainWindowFocused;
        if (shouldShow)
        {
            _indicator.SetRecordingState(_isRecording);

            // Respect a position the user dragged it to before; otherwise the default.
            if (_settings.Data.IndicatorPositionX is { } x && _settings.Data.IndicatorPositionY is { } y)
                _indicator.SetPosition(x, y);
            else
                _indicator.PositionBottomCenter(this);

            _indicator.Show();
        }
        else
        {
            _indicator.Hide();
        }
    }

    private async System.Threading.Tasks.Task InitializeModelAsync()
    {
        RecordBtn.IsEnabled = false;
        ModelProgressBar.IsVisible = true;
        StatusText.Text = "Parakeet-Modell wird vorbereitet...";

        var progress = new Progress<(double percent, string status)>(p =>
        {
            ModelProgressBar.Value = p.percent;
            StatusText.Text = p.status;
        });

        try
        {
            await _models.EnsureModelDownloadedAsync(progress);

            StatusText.Text = "Modell wird geladen...";
            // Loading the encoder into ONNX Runtime takes a moment - do it off the UI thread.
            _transcriber = await System.Threading.Tasks.Task.Run(() => new ParakeetTranscriber(_models));

            _isModelReady = true;
            ModelProgressBar.IsVisible = false;
            RecordBtn.IsEnabled = true;
            StatusText.Text = ReadyStatusText();
            ModelStatusBadgeText.Text = "Modell lokal geladen";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler beim Modell-Setup: " + ex.Message;
        }
    }

    private string ReadyStatusText() =>
        $"Bereit — {HotkeyPresetInfo.GetDisplayName(_settings.Data.Hotkey)} halten zum Sprechen";

    private void ShowPage(Control page, Button activeTabBtn, Border activeUnderline)
    {
        RecorderPage.IsVisible = ReferenceEquals(page, RecorderPage);
        HistoryPage.IsVisible = ReferenceEquals(page, HistoryPage);
        SettingsPage.IsVisible = ReferenceEquals(page, SettingsPage);

        foreach (var btn in new[] { TabRecorderBtn, TabHistoryBtn, TabSettingsBtn })
            btn.Classes.Remove("tabBtnActive");
        activeTabBtn.Classes.Add("tabBtnActive");

        foreach (var underline in new[] { TabRecorderUnderline, TabHistoryUnderline, TabSettingsUnderline })
            underline.Opacity = 0;
        activeUnderline.Opacity = 1;

        if (ReferenceEquals(page, HistoryPage))
            RefreshHistoryList();
        if (ReferenceEquals(page, RecorderPage))
            UpdateStats();
    }

    private void RefreshHistoryList()
    {
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = _history.Entries;
    }

    /// <summary>Updates the stat card: today's word count and how long ago the last
    /// dictation happened. Called after every new recording and periodically while
    /// the Recorder page is open, so "vor X Minuten" doesn't go stale.</summary>
    private void UpdateStats()
    {
        var today = DateTime.Today;
        int wordCount = _history.Entries
            .Where(e => e.Timestamp.Date == today)
            .Sum(e => CountWords(e.Text));
        WordCountText.Text = wordCount.ToString();

        TotalWordCountText.Text = _stats.Data.TotalWordsSpoken.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

        var last = _history.Entries.FirstOrDefault(); // newest is inserted at index 0
        LastRecordingText.Text = last is null ? "–" : FormatRelativeTime(last.Timestamp);
    }

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var span = DateTime.Now - timestamp;
        if (span.TotalSeconds < 45) return "gerade eben";
        if (span.TotalMinutes < 60)
        {
            var m = (int)span.TotalMinutes;
            return $"vor {m} Minute{(m == 1 ? "" : "n")}";
        }
        if (span.TotalHours < 24)
        {
            var h = (int)span.TotalHours;
            return $"vor {h} Stunde{(h == 1 ? "" : "n")}";
        }
        var d = (int)span.TotalDays;
        return $"vor {d} Tag{(d == 1 ? "" : "en")}";
    }

    /// <summary>Ticks the recorder card's idle animation: a gentle breathing scale on
    /// the mic button plus two staggered rings that expand and fade behind it - the
    /// same idea as the approved mockup's CSS pulse/ping, always running.</summary>
    private void AnimateMic()
    {
        _micAnimElapsed += 0.03;

        double buttonPhase = (1 - Math.Cos(2 * Math.PI * (_micAnimElapsed % MicAnimPeriodSeconds) / MicAnimPeriodSeconds)) / 2;
        double scale = 1 + 0.05 * buttonPhase;
        RecordBtn.RenderTransform = new ScaleTransform(scale, scale);

        AnimateRing(PingRing1, _micAnimElapsed);
        AnimateRing(PingRing2, _micAnimElapsed + MicAnimPeriodSeconds / 2);
    }

    private static void AnimateRing(Ellipse ring, double elapsed)
    {
        double phase = (elapsed % MicAnimPeriodSeconds) / MicAnimPeriodSeconds; // 0..1
        double size = 88 + phase * 88; // grows from 88 to 176
        double opacity = 0.55 * (1 - phase) * (1 - phase); // ease-out fade
        ring.Width = size;
        ring.Height = size;
        ring.Opacity = opacity;
    }

    private void ToggleRecording()
    {
        if (_isRecording) _ = StopRecordingAndTranscribeAsync();
        else StartRecording();
    }

    private void StartRecording()
    {
        if (_isRecording || !_isModelReady) return;
        _isRecording = true;

        // Capture which window was active BEFORE we do anything, so we know where to
        // paste the result later. When triggered by the hotkey while another app has
        // focus, this correctly points at that app (our own window never stole focus).
        _foregroundWindowBeforeRecording = TextInjector.CaptureCurrentForegroundWindow();

        RecordBtn.Classes.Add("isRecording");
        StatusText.Text = "Aufnahme läuft...";

        _indicator.ResetBars();
        SyncIndicatorVisibility();

        _audioRecorder.Start();
    }

    private async System.Threading.Tasks.Task StopRecordingAndTranscribeAsync()
    {
        if (!_isRecording) return;
        _isRecording = false;

        RecordBtn.Classes.Remove("isRecording");
        StatusText.Text = "Wird transkribiert...";
        SyncIndicatorVisibility();

        var samples = await _audioRecorder.StopAsync();

        string recognizedText;
        try
        {
            // CPU-bound ONNX inference - keep it off the UI thread.
            recognizedText = await System.Threading.Tasks.Task.Run(() => _transcriber!.Transcribe(samples));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler bei der Transkription: " + ex.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            StatusText.Text = "Nichts erkannt — bitte nochmal versuchen";
            return;
        }

        TextEditor.Text = string.IsNullOrEmpty(TextEditor.Text)
            ? recognizedText
            : TextEditor.Text + " " + recognizedText;

        _history.Add(recognizedText);
        _stats.AddWords(CountWords(recognizedText));
        if (HistoryPage.IsVisible) RefreshHistoryList();
        UpdateStats();

        if (_settings.Data.AutoPasteIntoActiveWindow)
        {
            // Add a trailing space so consecutive push-to-talk dictations don't run
            // straight into each other (e.g. "...Test.Next sentence" -> "...Test. Next sentence").
            var textToInject = recognizedText.EndsWith(' ') ? recognizedText : recognizedText + " ";
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            await TextInjector.InjectAsync(clipboard, textToInject, _foregroundWindowBeforeRecording);
        }

        StatusText.Text = ReadyStatusText();
    }

    private async void OnCopyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(TextEditor.Text ?? string.Empty);
            StatusText.Text = "Kopiert ✓";
        }
    }

    private async void OnHistoryItemCopyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryEntry entry }) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(entry.Text);
        StatusText.Text = "Kopiert ✓";
    }

    private void OnThemeToggleClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey.Dispose();
        base.OnClosed(e);
    }
}
