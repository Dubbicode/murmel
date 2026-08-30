using System;
using System.Collections.Generic;
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

    // Remembers exactly what text was last pasted into another app and where, so a
    // follow-up voice-correction command ("korrigiere X zu Y") can undo just that paste
    // (via backspaces) and re-inject the corrected version. Null once nothing has been
    // injected yet, or after the text no longer matches what a correction would expect.
    private string? _lastInjectedText;
    private IntPtr _lastInjectionTarget;

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
    private readonly DictionaryStore _dictionary = new();
    private readonly StatsStore _stats = new();
    private readonly ModelManager _models = new();
    private readonly AudioRecorder _audioRecorder = new();
    private ParakeetTranscriber? _transcriber;
    private readonly RecordingIndicatorWindow _indicator = new();

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

        TabRecorderBtn.Click += (_, _) => ShowPage(RecorderPage, TabRecorderBtn);
        TabHistoryBtn.Click += (_, _) => ShowPage(HistoryPage, TabHistoryBtn);
        TabDictionaryBtn.Click += (_, _) => ShowPage(DictionaryPage, TabDictionaryBtn);
        TabSettingsBtn.Click += (_, _) => ShowPage(SettingsPage, TabSettingsBtn);
        ClearHistoryBtn.Click += (_, _) => { _history.Clear(); RefreshHistoryList(); UpdateStats(); };
        HistorySearchBox.TextChanged += (_, _) => RefreshHistoryList();

        AddCorrectionBtn.Click += (_, _) =>
        {
            var wrong = NewCorrectionWrongBox.Text?.Trim();
            var right = NewCorrectionRightBox.Text?.Trim();
            if (string.IsNullOrEmpty(wrong) || string.IsNullOrEmpty(right)) return;

            _dictionary.Data.Corrections.Add(new CorrectionEntry { Wrong = wrong, Right = right });
            _dictionary.Save();
            NewCorrectionWrongBox.Text = "";
            NewCorrectionRightBox.Text = "";
            RefreshDictionaryLists();
        };

        AddSnippetBtn.Click += (_, _) =>
        {
            var trigger = NewSnippetTriggerBox.Text?.Trim();
            var value = NewSnippetValueBox.Text?.Trim();
            if (string.IsNullOrEmpty(trigger) || string.IsNullOrEmpty(value)) return;

            _dictionary.Data.Snippets.Add(new SnippetEntry { Trigger = trigger, Value = value });
            _dictionary.Save();
            NewSnippetTriggerBox.Text = "";
            NewSnippetValueBox.Text = "";
            RefreshDictionaryLists();
        };

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

        RecordingModeToggle.IsChecked = _settings.Data.RecordingMode == RecordingMode.Toggle;
        RecordingModeToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.Data.RecordingMode = RecordingModeToggle.IsChecked == true
                ? RecordingMode.Toggle
                : RecordingMode.PushToTalk;
            _settings.Save();
        };

        // First run of the lifetime word counter: backfill from whatever history is
        // already on disk (up to 200 entries) so existing users don't start at 0.
        _stats.SeedIfFresh(_history.Entries.Sum(e => CountWords(e.Text)));
        _stats.SeedDailyIfEmpty(_history.Entries.Select(e => (e.Timestamp, CountWords(e.Text))));

        // The chart needs the Canvas's actual pixel width to place points, which isn't
        // known yet on the very first UpdateStats() call above (layout hasn't run) -
        // redraw once it is.
        WeekChartCanvas.SizeChanged += (_, _) => UpdateWeekChart();

        RefreshHistoryList();
        UpdateStats();

        _statsRefreshTimer.Tick += (_, _) => { if (RecorderPage.IsVisible) UpdateStats(); };
        _statsRefreshTimer.Start();

        // Global hotkey: works even while another app is focused. Two modes (see
        // Settings): Push-to-Talk (hold = record, release = stop, the original
        // behavior) or Toggle (press once to start, press again to stop - the release
        // event is then ignored entirely).
        _hotkey.Preset = _settings.Data.Hotkey;
        _hotkey.HotkeyPressed += () => Dispatcher.UIThread.Post(() =>
        {
            if (_settings.Data.RecordingMode == RecordingMode.Toggle) ToggleRecording();
            else StartRecording();
        });
        _hotkey.HotkeyReleased += () => Dispatcher.UIThread.Post(() =>
        {
            if (_settings.Data.RecordingMode == RecordingMode.PushToTalk) _ = StopRecordingAndTranscribeAsync();
        });
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
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler beim Modell-Setup: " + ex.Message;
        }
    }

    private string ReadyStatusText() =>
        $"Bereit — {HotkeyPresetInfo.GetDisplayName(_settings.Data.Hotkey)} halten zum Sprechen";

    private void ShowPage(Control page, Button activeNavBtn)
    {
        RecorderPage.IsVisible = ReferenceEquals(page, RecorderPage);
        HistoryPage.IsVisible = ReferenceEquals(page, HistoryPage);
        DictionaryPage.IsVisible = ReferenceEquals(page, DictionaryPage);
        SettingsPage.IsVisible = ReferenceEquals(page, SettingsPage);

        foreach (var btn in new[] { TabRecorderBtn, TabHistoryBtn, TabDictionaryBtn, TabSettingsBtn })
            btn.Classes.Remove("navIconBtnActive");
        activeNavBtn.Classes.Add("navIconBtnActive");

        if (ReferenceEquals(page, HistoryPage))
            RefreshHistoryList();
        if (ReferenceEquals(page, DictionaryPage))
            RefreshDictionaryLists();
        if (ReferenceEquals(page, RecorderPage))
        {
            UpdateStats();
            RefreshHistoryPreview();
            RefreshDictionaryPreview();
        }
    }

    private void OnGoToHistoryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ShowPage(HistoryPage, TabHistoryBtn);

    private void OnGoToDictionaryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ShowPage(DictionaryPage, TabDictionaryBtn);

    private void RefreshDictionaryLists()
    {
        // A fresh list instance each time (not null-then-reassign) - Avalonia's
        // ItemsControl crashes with an ArgumentOutOfRangeException in some versions when
        // ItemsSource goes null -> populated in quick succession (confirmed via the
        // Windows crash log). A new List<T> reference is recognized as a real change
        // without ever passing through null.
        CorrectionsList.ItemsSource = _dictionary.Data.Corrections.ToList();
        SnippetsList.ItemsSource = _dictionary.Data.Snippets.ToList();
        RefreshDictionaryPreview();
    }

    /// <summary>Wörterbuch-Kachel on the Aufnahme home page - a few snippet-trigger chips,
    /// independent of whatever's currently on the full Wörterbuch page.</summary>
    private void RefreshDictionaryPreview()
    {
        DictionaryPreviewList.ItemsSource = _dictionary.Data.Snippets.Take(6).ToList();
    }

    // Fires when a Korrekturen/Snippets row's inline TextBox loses focus - the TwoWay
    // binding has already written the edited value straight into the underlying
    // CorrectionEntry/SnippetEntry object by then, so this just needs to persist it.
    private void OnDictionaryFieldEdited(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _dictionary.Save();
    }

    private void OnRemoveCorrectionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CorrectionEntry entry) return;
        _dictionary.Data.Corrections.Remove(entry);
        _dictionary.Save();
        RefreshDictionaryLists();
    }

    private void OnRemoveSnippetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SnippetEntry entry) return;
        _dictionary.Data.Snippets.Remove(entry);
        _dictionary.Save();
        RefreshDictionaryLists();
    }

    private void RefreshHistoryList()
    {
        var query = HistorySearchBox.Text?.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _history.Entries
            : _history.Entries.Where(e => e.Text.Contains(query, StringComparison.OrdinalIgnoreCase));

        // Entries are already newest-first, so a day header goes in right before the
        // first entry of each new (older) day encountered while walking the list.
        var items = new List<object>();
        DateTime? lastDate = null;
        foreach (var entry in filtered)
        {
            var date = entry.Timestamp.Date;
            if (date != lastDate)
            {
                items.Add(new HistoryDateHeader { Label = FormatHistoryDateHeader(date) });
                lastDate = date;
            }
            items.Add(entry);
        }

        // See RefreshDictionaryLists for why this is a fresh list, not null-then-reassign.
        HistoryList.ItemsSource = items;
        HistoryEmptySearchText.IsVisible = items.Count == 0 && !string.IsNullOrEmpty(query);
        RefreshHistoryPreview();
    }

    /// <summary>Verlauf-Kachel on the Aufnahme home page - always the most recent entries,
    /// independent of whatever search is active on the full Verlauf page.</summary>
    private void RefreshHistoryPreview()
    {
        HistoryPreviewList.ItemsSource = _history.Entries.Take(4).ToList();
    }

    private static string FormatHistoryDateHeader(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return "Heute";
        if (date == today.AddDays(-1)) return "Gestern";
        return date.ToString("dd.MM.yyyy");
    }

    /// <summary>Updates the stat card: today's word count, the lifetime total, how long
    /// ago the last dictation happened, and the 7-day chart. Called after every new
    /// recording and periodically while the Recorder page is open, so "vor X Minuten"
    /// doesn't go stale.</summary>
    private void UpdateStats()
    {
        // Read from StatsStore (persists independent of HistoryStore) rather than
        // summing today's history entries directly, so "Verlauf leeren" or the
        // 200-entry cap can never make today's count drop or look wrong.
        WordCountText.Text = _stats.GetWordsForDay(DateTime.Today).ToString();

        TotalWordCountText.Text = _stats.Data.TotalWordsSpoken.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

        var last = _history.Entries.FirstOrDefault(); // newest is inserted at index 0
        LastRecordingText.Text = last is null ? "–" : FormatRelativeTime(last.Timestamp);

        UpdateWeekChart();
    }

    private static readonly string[] GermanDayAbbreviations = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

    private void UpdateWeekChart()
    {
        // Canvas hasn't been laid out yet (e.g. the very first call from the
        // constructor) - it fires SizeChanged once it has a real width, which re-runs
        // this and draws correctly then.
        double width = WeekChartCanvas.Bounds.Width;
        if (width <= 0) return;

        var dots = new[] { DayDot0, DayDot1, DayDot2, DayDot3, DayDot4, DayDot5, DayDot6 };
        var countLabels = new[] { DayCount0, DayCount1, DayCount2, DayCount3, DayCount4, DayCount5, DayCount6 };
        var labels = new[] { DayLabel0, DayLabel1, DayLabel2, DayLabel3, DayLabel4, DayLabel5, DayLabel6 };

        var today = DateTime.Today;
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToArray(); // oldest..today, left to right
        var counts = days.Select(d => _stats.GetWordsForDay(d)).ToArray();
        int max = Math.Max(counts.Max(), 1);

        const double topPadding = 16;  // room for the value label above the highest point
        const double plotHeight = 40;  // vertical space the line itself moves within

        var points = new Avalonia.Points();
        for (int i = 0; i < 7; i++)
        {
            double x = i * (width / 6);
            double y = topPadding + plotHeight * (1 - (double)counts[i] / max);
            points.Add(new Point(x, y));

            Canvas.SetLeft(dots[i], x - dots[i].Width / 2);
            Canvas.SetTop(dots[i], y - dots[i].Height / 2);

            countLabels[i].Text = counts[i].ToString();
            Canvas.SetLeft(countLabels[i], x - 8);
            Canvas.SetTop(countLabels[i], y - 18);

            // ISO: Monday = 1 .. Sunday = 7, matching the Mo..So label order above.
            int dayIndex = ((int)days[i].DayOfWeek + 6) % 7;
            labels[i].Text = GermanDayAbbreviations[dayIndex];
            // Bold (rather than a swapped-in color) highlights today without needing a
            // resolved-once brush that would go stale until the next refresh if the
            // user switches Light/Dark mode in between.
            labels[i].FontWeight = days[i] == today ? FontWeight.Bold : FontWeight.Normal;
        }
        WeekChartLine.Points = points;
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

        // Personal dictionary: fix reliably-mis-heard words and resolve any snippet
        // triggers first, so every check below (self-correction, correction commands,
        // history) already sees the final text.
        recognizedText = DictionaryProcessor.ApplyCorrections(recognizedText, _dictionary.Data);
        recognizedText = DictionaryProcessor.ApplySnippets(recognizedText, _dictionary.Data);

        // Self-correction WITHIN the same recording - "Wir treffen uns am Montag. Nein,
        // am Dienstag." said in one continuous take - is by far the most common way
        // people actually correct themselves out loud. Detect and resolve it before
        // anything else so the cleaned-up text is what gets added/pasted below.
        var embeddedCorrection = CorrectionCommandParser.TryParseEmbeddedCorrection(recognizedText);
        if (embeddedCorrection is not null)
        {
            recognizedText = embeddedCorrection.Value.CleanedText;
            StatusText.Text = $"Selbstkorrektur erkannt: „{embeddedCorrection.Value.Command.Find}“ → „{embeddedCorrection.Value.Command.Replace}“";
        }
        else
        {
            // A correction command ("korrigiere X zu Y") as its OWN separate recording is
            // about the PREVIOUS dictation, not new text of its own - handle it separately
            // and stop here rather than adding it to the transcript/history/word count
            // like a normal dictation.
            var correction = CorrectionCommandParser.TryParse(recognizedText, _history.Entries.FirstOrDefault()?.Text);
            if (correction is not null)
            {
                await ApplyCorrectionAsync(correction);
                return;
            }
        }

        TextEditor.Text = string.IsNullOrEmpty(TextEditor.Text)
            ? recognizedText
            : TextEditor.Text + " " + recognizedText;

        _history.Add(recognizedText);
        _stats.AddWords(CountWords(recognizedText));
        // Always refresh (not just when HistoryPage is visible) - the Aufnahme home page
        // now also shows a live Verlauf-Vorschau tile.
        RefreshHistoryList();
        UpdateStats();

        if (_settings.Data.AutoPasteIntoActiveWindow)
        {
            // Add a trailing space so consecutive push-to-talk dictations don't run
            // straight into each other (e.g. "...Test.Next sentence" -> "...Test. Next sentence").
            var textToInject = recognizedText.EndsWith(' ') ? recognizedText : recognizedText + " ";
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            await TextInjector.InjectAsync(clipboard, textToInject, _foregroundWindowBeforeRecording);
            _lastInjectedText = textToInject;
            _lastInjectionTarget = _foregroundWindowBeforeRecording;
        }
        else
        {
            _lastInjectedText = null;
        }

        StatusText.Text = ReadyStatusText();
    }

    /// <summary>
    /// Applies a spoken "korrigiere X zu Y" / "ersetze X durch Y" / "ändere X zu Y" command
    /// to the most recently dictated text: in the in-app editor, in the saved history entry,
    /// and - best-effort - in whatever app the previous dictation was pasted into.
    /// </summary>
    private async System.Threading.Tasks.Task ApplyCorrectionAsync(CorrectionCommand correction)
    {
        var lastEntry = _history.Entries.FirstOrDefault();
        if (lastEntry is null || string.IsNullOrEmpty(lastEntry.Text))
        {
            StatusText.Text = "Keine vorherige Diktion zum Korrigieren gefunden";
            return;
        }

        var original = lastEntry.Text;
        int idx = original.LastIndexOf(correction.Find, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            StatusText.Text = $"„{correction.Find}“ wurde im letzten Text nicht gefunden";
            return;
        }

        var corrected = original[..idx] + correction.Replace + original[(idx + correction.Find.Length)..];

        // 1) Saved history entry
        lastEntry.Text = corrected;
        _history.Save();
        RefreshHistoryList();

        // 2) In-app transcript editor - replace the same occurrence at the tail of the
        // current text (the editor may hold several concatenated dictations already).
        if (!string.IsNullOrEmpty(TextEditor.Text))
        {
            int editorIdx = TextEditor.Text.LastIndexOf(original, StringComparison.OrdinalIgnoreCase);
            if (editorIdx >= 0)
            {
                TextEditor.Text = TextEditor.Text[..editorIdx] + corrected + TextEditor.Text[(editorIdx + original.Length)..];
            }
            else
            {
                int wordIdx = TextEditor.Text.LastIndexOf(correction.Find, StringComparison.OrdinalIgnoreCase);
                if (wordIdx >= 0)
                    TextEditor.Text = TextEditor.Text[..wordIdx] + correction.Replace + TextEditor.Text[(wordIdx + correction.Find.Length)..];
            }
        }

        // 3) Whatever app the previous dictation was pasted into - only attempted if we
        // know exactly what we pasted there and it still matches the uncorrected text
        // (best-effort: relies on the cursor still sitting right after that paste).
        if (_settings.Data.AutoPasteIntoActiveWindow
            && _lastInjectedText is not null
            && _lastInjectedText.TrimEnd() == original.TrimEnd())
        {
            var correctedInjected = corrected.EndsWith(' ') ? corrected : corrected + " ";
            await TextInjector.SendBackspacesAsync(_lastInjectedText.Length, _lastInjectionTarget);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            await TextInjector.InjectAsync(clipboard, correctedInjected, _lastInjectionTarget);
            _lastInjectedText = correctedInjected;
        }

        StatusText.Text = $"Korrigiert: „{correction.Find}“ → „{correction.Replace}“";
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
