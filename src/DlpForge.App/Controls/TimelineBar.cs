using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using DlpForge.Core.Models;

namespace DlpForge.App.Controls;

/// <summary>
/// The cut timeline on the download card: the whole video as a bar, chapter starts as ticks, parts already
/// added in a lighter accent, and the part being edited with two handles to drag. Snaps to whole seconds.
/// </summary>
public sealed class TimelineBar : Control
{
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<TimelineBar, double>(nameof(Duration));

    public static readonly StyledProperty<double> StartProperty =
        AvaloniaProperty.Register<TimelineBar, double>(nameof(Start), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> EndProperty =
        AvaloniaProperty.Register<TimelineBar, double>(nameof(End), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<TimeRange>?> RangesProperty =
        AvaloniaProperty.Register<TimelineBar, IEnumerable<TimeRange>?>(nameof(Ranges));

    public static readonly StyledProperty<IEnumerable<double>?> MarksProperty =
        AvaloniaProperty.Register<TimelineBar, IEnumerable<double>?>(nameof(Marks));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(TrackBrush), Brushes.LightGray);

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(AccentBrush), Brushes.OrangeRed);

    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(MarkBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(ThumbBrush), Brushes.White);

    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double End { get => GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public IEnumerable<TimeRange>? Ranges { get => GetValue(RangesProperty); set => SetValue(RangesProperty, value); }
    public IEnumerable<double>? Marks { get => GetValue(MarksProperty); set => SetValue(MarksProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? MarkBrush { get => GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }
    public IBrush? ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }

    private const double ThumbRadius = 8;
    private const double TrackHeight = 8;
    private enum Handle { None, Start, End }
    private Handle _dragging;

    static TimelineBar()
    {
        AffectsRender<TimelineBar>(DurationProperty, StartProperty, EndProperty, RangesProperty, MarksProperty,
            TrackBrushProperty, AccentBrushProperty, MarkBrushProperty, ThumbBrushProperty);
        FocusableProperty.OverrideDefaultValue<TimelineBar>(true);
    }

    public TimelineBar()
    {
        Height = 30;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private double Left => ThumbRadius + 1;
    private double Width0 => Math.Max(1, Bounds.Width - 2 * Left);
    private double X(double seconds) => Left + Width0 * (Duration > 0 ? Math.Clamp(seconds / Duration, 0, 1) : 0);
    private double Seconds(double x) => Duration > 0 ? Math.Round(Math.Clamp((x - Left) / Width0, 0, 1) * Duration) : 0;

    public override void Render(DrawingContext context)
    {
        double mid = Bounds.Height / 2;
        double top = mid - TrackHeight / 2;
        context.DrawRectangle(TrackBrush, null, new RoundedRect(new Rect(Left, top, Width0, TrackHeight), TrackHeight / 2));
        if (Duration <= 0) return;

        if (AccentBrush is { } accent)
        {
            // Parts already added: the same accent, see-through.
            using (context.PushOpacity(0.45))
            {
                foreach (var r in Ranges ?? [])
                    context.DrawRectangle(accent, null, new Rect(X(r.Start), top, Math.Max(2, X(r.End) - X(r.Start)), TrackHeight));
            }
            context.DrawRectangle(accent, null, new Rect(X(Start), top, Math.Max(2, X(End) - X(Start)), TrackHeight));
        }

        var tick = new Pen(MarkBrush, 1.5);
        foreach (var m in Marks ?? [])
            context.DrawLine(tick, new Point(X(m), top - 4), new Point(X(m), top + TrackHeight + 4));

        var stroke = new Pen(AccentBrush, 2);
        context.DrawEllipse(ThumbBrush, stroke, new Point(X(Start), mid), ThumbRadius, ThumbRadius);
        context.DrawEllipse(ThumbBrush, stroke, new Point(X(End), mid), ThumbRadius, ThumbRadius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Duration <= 0) return;
        var x = e.GetPosition(this).X;
        // Grab the nearer handle; a click on the track moves that handle there.
        _dragging = Math.Abs(x - X(Start)) <= Math.Abs(x - X(End)) ? Handle.Start : Handle.End;
        if (Math.Abs(X(Start) - X(End)) < 1 && x > X(End)) _dragging = Handle.End;
        Move(x);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging != Handle.None) Move(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = Handle.None;
        e.Pointer.Capture(null);
    }

    private void Move(double x)
    {
        var s = Seconds(x);
        if (_dragging == Handle.Start) Start = Math.Min(s, Math.Max(0, End - 1));
        else if (_dragging == Handle.End) End = Math.Max(s, Math.Min(Duration, Start + 1));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Arrow keys nudge the end handle; with Shift, the start handle. One second per press.
        double step = e.Key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
        if (step == 0 || Duration <= 0) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Start = Math.Clamp(Start + step, 0, End - 1);
        else End = Math.Clamp(End + step, Start + 1, Duration);
        e.Handled = true;
    }
}
