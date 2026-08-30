using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Murmel;

/// <summary>
/// A small floating pill, similar to Spokenly/WisprFlow. At rest (Murmel is running in
/// the background, minimized, or just not the focused window) it's a plain, static,
/// slightly thicker bar you can click to bring the app back - no motion, so it doesn't
/// catch your eye while you're working in another app. As soon as a recording starts it
/// swaps to the animated 5-bar waveform reacting to the live microphone level, so you
/// get visual confirmation that Murmel is actually hearing you.
///
/// NOTE: the window is a FIXED size, always. Earlier revisions tried to resize it
/// between an "idle dot" and a "full pill" size, which made the whole window fail to
/// render reliably on Windows (transparent/topmost windows there don't like being
/// resized before their first Show()). Never resize this window - only Position and
/// which of the two inner visuals (idle bar vs. waveform) is shown should change.
/// </summary>
public class RecordingIndicatorWindow : Window
{
    private const int BarCount = 5;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly StackPanel _barsPanel;
    private readonly Rectangle _idleBar;
    private readonly Border _pill;
    private readonly Random _rng = new();

    // Smoothed per-bar heights so the animation looks like a natural waveform
    // rather than every bar jumping to the exact same value at once.
    private readonly double[] _targetHeights = new double[BarCount];
    private readonly double[] _currentHeights = new double[BarCount];

    // Continuous idle "breathing" wave so the dots never look frozen, even
    // during silence - each bar has its own speed/offset so they ripple
    // rather than pulse in lockstep.
    private readonly double[] _wavePhase = new double[BarCount];
    private readonly double[] _waveSpeed = new double[BarCount];

    private const double PillWidth = 60;
    private const double PillHeight = 20;
    private const double MaxBarHeight = 11;
    private const double MinBarHeight = 3;
    private const double IdleWaveAmplitude = 1.5;
    private const double IdleOpacity = 0.75;
    private const double DragThreshold = 4; // pixels of movement before a press counts as a drag, not a click

    /// <summary>Raised when the pill is clicked (not dragged) - used to bring the main
    /// window back to the foreground when Murmel is running in the background.</summary>
    public event Action? Clicked;

    /// <summary>Raised after the user drags the pill to a new spot, with the new
    /// top-left screen position - so it can be remembered in Settings.</summary>
    public event Action<double, double>? PositionChangedByUser;

    private bool _isDragging;
    private PixelPoint _dragStartScreenPoint;
    private PixelPoint _dragStartWindowPos;

    public RecordingIndicatorWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        // Never steal keyboard focus from whatever app the user is dictating into.
        ShowActivated = false;
        Focusable = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Width = PillWidth;
        Height = PillHeight;

        _barsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false // starts idle - see SetRecordingState
        };

        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 3,
                Height = MinBarHeight,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = new SolidColorBrush(Color.Parse("#F0B429")),
                VerticalAlignment = VerticalAlignment.Center
            };
            _bars[i] = bar;
            _barsPanel.Children.Add(bar);

            // Stagger each bar's wave so they ripple left-to-right-ish instead
            // of moving as one identical block.
            _wavePhase[i] = i * 0.9;
            _waveSpeed[i] = 0.18 + _rng.NextDouble() * 0.10;
        }

        // The idle look: one plain, motionless, slightly thicker bar (WisprFlow-style) -
        // deliberately not animated, so it reads as a quiet "click here to go back"
        // handle rather than something demanding attention while you're focused elsewhere.
        _idleBar = new Rectangle
        {
            Width = 20,
            Height = 3,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Fill = new SolidColorBrush(Color.Parse("#10B981")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = true
        };

        var content = new Panel();
        content.Children.Add(_barsPanel);
        content.Children.Add(_idleBar);

        _pill = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E6111318")), // near-opaque dark pill
            CornerRadius = new CornerRadius(PillHeight / 2),
            Child = content,
            Opacity = IdleOpacity,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        _pill.PointerPressed += OnPillPointerPressed;
        _pill.PointerMoved += OnPillPointerMoved;
        _pill.PointerReleased += OnPillPointerReleased;

        Content = _pill;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => AnimateTowardsTarget();
        timer.Start();
    }

    /// <summary>Call from the audio callback (any thread) with a 0..1 level.</summary>
    public void ReportLevel(float level)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = 0; i < BarCount; i++)
            {
                // Each bar gets a slightly different sensitivity/jitter so the
                // pill looks like a lively waveform instead of one flat block.
                var jitter = 0.75 + _rng.NextDouble() * 0.5;
                var h = MinBarHeight + level * jitter * (MaxBarHeight - MinBarHeight);
                _targetHeights[i] = Math.Clamp(h, MinBarHeight, MaxBarHeight);
            }
        });
    }

    /// <summary>Switches between the static idle bar (the low-opacity background
    /// indicator you can click to reopen Murmel) and the full-opacity animated waveform
    /// shown while actively recording. The window itself is never resized - only which
    /// inner visual is shown, and the pill's overall opacity, change.</summary>
    public void SetRecordingState(bool isRecording)
    {
        _pill.Opacity = isRecording ? 1.0 : IdleOpacity;
        _barsPanel.IsVisible = isRecording;
        _idleBar.IsVisible = !isRecording;
    }

    private void OnPillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _isDragging = false;
        _dragStartScreenPoint = this.PointToScreen(e.GetPosition(this));
        _dragStartWindowPos = Position;
        e.Pointer.Capture(_pill);
    }

    private void OnPillPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, _pill)) return;

        var current = this.PointToScreen(e.GetPosition(this));
        var dx = current.X - _dragStartScreenPoint.X;
        var dy = current.Y - _dragStartScreenPoint.Y;

        if (!_isDragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
            _isDragging = true;

        if (_isDragging)
            Position = new PixelPoint(_dragStartWindowPos.X + dx, _dragStartWindowPos.Y + dy);
    }

    private void OnPillPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(e.Pointer.Captured, _pill))
            e.Pointer.Capture(null);

        if (_isDragging)
        {
            _isDragging = false;
            PositionChangedByUser?.Invoke(Position.X, Position.Y);
        }
        else
        {
            Clicked?.Invoke();
        }
    }

    /// <summary>Moves the pill to a specific saved screen position (from Settings),
    /// bypassing the default bottom-center placement.</summary>
    public void SetPosition(double x, double y) => Position = new PixelPoint((int)x, (int)y);

    private void AnimateTowardsTarget()
    {
        for (int i = 0; i < BarCount; i++)
        {
            // Ease towards the level-driven target for smooth reaction to volume...
            _currentHeights[i] += (_targetHeights[i] - _currentHeights[i]) * 0.35;

            // ...then layer a small continuous wave on top so the dots keep
            // gently moving even while you take a breath mid-sentence.
            _wavePhase[i] += _waveSpeed[i];
            var wiggle = Math.Sin(_wavePhase[i]) * IdleWaveAmplitude;

            var finalHeight = Math.Clamp(_currentHeights[i] + wiggle, MinBarHeight, MaxBarHeight + IdleWaveAmplitude);
            _bars[i].Height = finalHeight;
        }
    }

    /// <summary>Positions the pill bottom-center on the given window's primary screen -
    /// a small, unobtrusive button-like element rather than something sitting in the
    /// middle of whatever the user is looking at.</summary>
    public void PositionBottomCenter(Window owner)
    {
        var screen = owner.Screens.Primary ?? (owner.Screens.All.Count > 0 ? owner.Screens.All[0] : null);
        if (screen is null) return;

        var area = screen.WorkingArea;
        var x = area.X + (area.Width - (int)PillWidth) / 2;
        var y = area.Y + area.Height - (int)PillHeight - 40;
        Position = new PixelPoint(x, y);
    }

    public void ResetBars()
    {
        for (int i = 0; i < BarCount; i++)
        {
            _targetHeights[i] = MinBarHeight;
            _currentHeights[i] = MinBarHeight;
            _bars[i].Height = MinBarHeight;
        }
    }
}
