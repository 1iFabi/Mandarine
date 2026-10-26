using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mandarine;

/// <summary>What the guide's demo shows for one wheel: its key, the real entries it offers, and the one it picks.</summary>
public sealed record DemoStep(string Key, string Name, string What, IReadOnlyList<string> Options, int Pick, string? Output, string Notice);

public static class DemoScript
{
    const int MaxOptions = 8;
    static readonly string[] Sample = { "mandarina.jpg" };

    static readonly DemoStep?[] Built = new DemoStep?[3];

    /// <summary>Taken from the catalogue, so the demo follows the real wheel. Built once: the demo asks every frame.</summary>
    public static DemoStep For(int step) => Built[Math.Clamp(step, 0, 2)] ??= step switch
    {
        0 => Make("Shift", "Convertir", "Apunta a un formato y suelta", Labels(Catalog.TargetsFor(Sample)), "PNG", "mandarina.png"),
        1 => Make("Ctrl", "Herramientas", "Comprimir, recortar, quitar el fondo…", Labels(Catalog.ToolsFor(Sample)), "Sin fondo", "mandarina-sin-fondo.png"),
        _ => Make("Alt", "Acciones", "Copiar, mover, compartir…", Labels(Catalog.ActionsFor(Sample)), "Copiar", null),
    };

    static IEnumerable<string> Labels(IEnumerable<Format> formats) => formats.Select(f => f.Label);

    /// <summary>When the wheel lacks the entry (no background model, say), the demo picks the first and promises no file.</summary>
    public static DemoStep Make(string key, string name, string what, IEnumerable<string> labels, string pick, string? output)
    {
        var options = labels.Take(MaxOptions).ToList();
        var found = options.Contains(pick);
        if (!found) output = null;
        var notice = output != null ? $"{output} en Práctica" : found ? "Copiado: pégalo donde quieras" : $"{options[0]}: listo";
        return new(key, name, what, options, IndexOr0(options, pick), output, notice);
    }

    public static int IndexOr0(IReadOnlyList<string> options, string label) => Math.Max(0, options.ToList().IndexOf(label));
}

/// <summary>A moment of the demo. <see cref="Cursor"/> is the offset from the photo's centre, in DIPs.</summary>
public readonly record struct DemoPose(Vector Cursor, bool Pressed, bool Dragging, bool KeyLit, bool WheelOpen, bool Hot, bool Result);

/// <summary>The demo as a pure function of time: take the photo, drag, tap the key, open the wheel, point, let go.</summary>
public static class DemoTimeline
{
    public const double Loop = 6.0;
    public static readonly Vector Rest = new(300, 150);
    public static readonly Vector Wheel = new(180, -20);

    public static DemoPose At(double t, DemoStep step)
    {
        t %= Loop;
        var pick = PickPoint(step);
        var cursor =
            t < 0.5 ? Rest :
            t < 1.2 ? Lerp(Rest, default, (t - 0.5) / 0.7) :
            t < 1.4 ? default :
            t < 2.2 ? Lerp(default, Wheel, (t - 1.4) / 0.8) :
            t < 2.95 ? Wheel :
            t < 3.4 ? Lerp(Wheel, pick, (t - 2.95) / 0.45) :
            t < 4.3 ? pick :
            t < 5.2 ? Lerp(pick, Rest, (t - 4.3) / 0.9) :
            Rest;
        var held = t >= 1.2 && t < 4.1;
        return new(cursor, held, held && t >= 1.4, t >= 2.45 && t < 2.85, t >= 2.6 && t < 4.1, t >= 3.4 && t < 4.1, t >= 4.3);
    }

    /// <summary>The still shown when Windows animations are off: the wheel open on the pick, key down.</summary>
    public static DemoPose KeyFrame(DemoStep step) => new(PickPoint(step), true, true, true, true, true, false);

    /// <summary>Over the picked segment, with the real wheel's layout: entry i centred at -π/2 + i·2π/n.</summary>
    public static Vector PickPoint(DemoStep step)
    {
        var a = -Math.PI / 2 + step.Pick * 2 * Math.PI / step.Options.Count;
        return Wheel + new Vector(64 * Math.Cos(a), 64 * Math.Sin(a));
    }

    static Vector Lerp(Vector a, Vector b, double k)
    {
        k = k < 0.5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2; // ease in-out
        return a + (b - a) * k;
    }
}

/// <summary>
/// Draws the demo over the guide's stage: a pointer takes the photo, the key goes down, the wheel springs open,
/// an entry lights up, and the result appears. Uses the real wheel's geometry and colours.
/// </summary>
public sealed class GuideDemo : Canvas
{
    static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)));
    static readonly Brush KeyPressed = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xE8)));
    static readonly Brush Orange = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x00)));

    readonly SpringClock _clock = new();
    readonly Path _cursor;
    readonly ScaleTransform _press = new();
    readonly Border _drag;
    readonly StackPanel _made;
    readonly TextBlock _madeName = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    readonly ScaleTransform _madeScale = new(0.4, 0.4);
    readonly Canvas _wheel = new() { Width = WheelWindow.Size, Height = WheelWindow.Size, RenderTransformOrigin = new Point(0.5, 0.5) };
    readonly ScaleTransform _wheelScale = new();
    readonly Border _notice;
    readonly TextBlock _noticeText = new() { Foreground = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7))), FontWeight = FontWeights.SemiBold };
    readonly TextBlock _noticeLink = new() { Text = "Abrir carpeta", Foreground = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x24))), Margin = new Thickness(12, 0, 0, 0) };
    readonly TranslateTransform _noticeMove = new(0, 80);
    readonly Spring _open, _lift, _result, _toast;
    readonly System.Diagnostics.Stopwatch _time = new();

    FrameworkElement? _photo;
    Border[] _keys = Array.Empty<Border>();
    (Brush Fill, Brush Line, Thickness Edge)[] _keyRest = Array.Empty<(Brush, Brush, Thickness)>();
    DemoStep? _built;
    UIElement? _hotLayer;
    TextBlock _hub = null!;
    Image _logo = null!;
    int _step;
    bool _ready; // SpringClock.Add paints at once, before every spring exists

    public bool Playing { get; private set; }

    public GuideDemo()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _cursor = new Path
        {
            Data = Geometry.Parse("M0,0 L0,20 L5,15 L9,24 L12,22.5 L8,14 L15,14 Z"),
            Fill = Brushes.White, Stroke = Ink, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = _press,
        };
        _drag = new Border
        {
            Width = 96, Height = 72, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Orange,
            // The one shadow in the guide: the copy is lifted off the stage.
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Opacity = 0.22, Direction = 270 },
        };
        _made = new StackPanel { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _madeScale };
        _made.Children.Add(new Border { Width = 96, Height = 72, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Orange });
        _made.Children.Add(_madeName);
        var row = new DockPanel();
        var strip = new Border { Width = 4, Background = Orange, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(strip, Dock.Left);
        row.Children.Add(strip);
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 9, 12, 9), Children = { _noticeText, _noticeLink } });
        _notice = new Border
        {
            Background = Frozen(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22))), CornerRadius = new CornerRadius(6), ClipToBounds = true,
            Child = row, RenderTransform = _noticeMove,
        };
        _wheel.RenderTransform = _wheelScale;
        Children.Add(_made);
        Children.Add(_drag);
        Children.Add(_wheel);
        Children.Add(_notice);
        Children.Add(_cursor);

        // A little overshoot on the wheel and the new file: they pop, as in the real app.
        _open = _clock.Add(new Spring(0) { DampingRatio = 0.6, Response = 0.35 }, _ => Paint());
        _lift = _clock.Add(new Spring(0) { Response = 0.2 }, _ => Paint());
        _result = _clock.Add(new Spring(0) { DampingRatio = 0.6, Response = 0.35 }, _ => Paint());
        _toast = _clock.Add(new Spring(0) { DampingRatio = 0.75, Response = 0.4 }, _ => Paint());
        _ready = true;
        Hide();
    }

    /// <summary>The real photo (dimmed while its copy is dragged), the three key caps (pressed in turn) and the photo's picture.</summary>
    public void Attach(FrameworkElement photo, Border[] keys, ImageSource? thumb = null)
    {
        _photo = photo;
        _keys = keys;
        _keyRest = keys.Select(k => (k.Background, k.BorderBrush, k.BorderThickness)).ToArray();
        foreach (var k in keys) k.RenderTransform = new TranslateTransform();
        if (thumb == null) return;
        _drag.Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill };
        ((Border)_made.Children[0]).Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill };
    }

    public void Play(int step)
    {
        _step = step;
        Playing = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            var s = DemoScript.For(step);
            Apply(DemoTimeline.KeyFrame(s), s, snap: true);
            return;
        }
        _time.Restart();
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop()
    {
        CompositionTarget.Rendering -= OnFrame;
        _time.Stop();
        Playing = false;
        Hide();
    }

    /// <summary>Paints one moment. Outside playback (tests, the still) the springs jump straight to it.</summary>
    public void ShowPose(DemoPose pose, DemoStep step) => Apply(pose, step, snap: !_time.IsRunning);

    void OnFrame(object? sender, EventArgs e)
    {
        var s = DemoScript.For(_step);
        Apply(DemoTimeline.At(_time.Elapsed.TotalSeconds, s), s, snap: false);
    }

    void Hide()
    {
        foreach (var sp in new[] { _open, _lift, _result, _toast }) { sp.Target = 0; sp.Snap(); }
        Paint();
        _cursor.Visibility = _drag.Visibility = Visibility.Hidden;
        if (_photo != null) _photo.Opacity = 1;
        for (var i = 0; i < _keys.Length; i++) PressKey(i, false);
    }

    void Apply(DemoPose pose, DemoStep step, bool snap)
    {
        if (_built != step) Build(step);
        var origin = new Point(ActualWidth / 2, ActualHeight / 2 - 11); // the photo sits above its 22-DIP name
        var at = origin + pose.Cursor;
        _cursor.Visibility = Visibility.Visible;
        SetLeft(_cursor, at.X - 1);
        SetTop(_cursor, at.Y - 1);
        _press.ScaleX = _press.ScaleY = pose.Pressed ? 0.85 : 1;
        // The open wheel covers the copy; peeking round its edges it only reads as noise.
        _drag.Visibility = pose.Dragging && !pose.WheelOpen ? Visibility.Visible : Visibility.Hidden;
        SetLeft(_drag, at.X - 40);
        SetTop(_drag, at.Y - 30);
        if (_photo != null) _photo.Opacity = pose.Dragging ? 0.35 : 1;
        var wheel = origin + DemoTimeline.Wheel;
        SetLeft(_wheel, wheel.X - WheelWindow.Center);
        SetTop(_wheel, wheel.Y - WheelWindow.Center);
        _made.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_made, origin.X + 120 - _made.DesiredSize.Width / 2);
        SetTop(_made, origin.Y - 36);
        SetRight(_notice, 14);
        SetBottom(_notice, 14);
        for (var i = 0; i < _keys.Length; i++) PressKey(i, pose.KeyLit && i == _step);

        _open.Target = pose.WheelOpen ? 1 : 0;
        _lift.Target = pose.Hot ? 1 : 0;
        _result.Target = pose.Result && step.Output != null ? 1 : 0;
        _toast.Target = pose.Result ? 1 : 0;
        if (!snap) { _clock.Kick(); return; }
        foreach (var sp in new[] { _open, _lift, _result, _toast }) sp.Snap();
        Paint();
    }

    void Paint()
    {
        if (!_ready) return;
        _wheelScale.ScaleX = _wheelScale.ScaleY = 0.55 + 0.45 * _open.Value;
        _wheel.Opacity = Math.Clamp(_open.Value, 0, 1);
        var hot = Math.Clamp(_lift.Value, 0, 1);
        if (_hotLayer != null) { _hotLayer.Opacity = _hub.Opacity = hot; _logo.Opacity = 1 - hot; }
        _madeScale.ScaleX = _madeScale.ScaleY = 0.4 + 0.6 * _result.Value;
        _made.Opacity = Math.Clamp(_result.Value, 0, 1);
        _noticeMove.Y = 80 * (1 - _toast.Value);
        _notice.Opacity = Math.Clamp(_toast.Value * 2, 0, 1);
    }

    void PressKey(int i, bool down)
    {
        var k = _keys[i];
        var rest = _keyRest[i];
        k.Background = down ? KeyPressed : rest.Fill;
        k.BorderBrush = down ? Orange : rest.Line;
        k.BorderThickness = down ? new Thickness(rest.Edge.Left, rest.Edge.Top, rest.Edge.Right, 2) : rest.Edge;
        ((TranslateTransform)k.RenderTransform).Y = down ? 2 : 0;
    }

    /// <summary>The wheel for this step: one frosted segment per entry, the picked one with an orange layer that rises out of the ring.</summary>
    void Build(DemoStep step)
    {
        _built = step;
        _wheel.Children.Clear();
        var n = step.Options.Count;
        var span = 2 * Math.PI / n;
        var veil = Frozen(new SolidColorBrush(WheelWindow.Light.Veil));
        var ink = Frozen(new SolidColorBrush(WheelWindow.Light.Ink));
        var edge = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)));
        var r = (WheelWindow.Outer + WheelWindow.Inner) / 2;
        TextBlock Label(string text, Brush fill, double mid, Transform? move)
        {
            var t = new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = text.Length > 7 ? 9 : 11, Foreground = fill, Width = 70, TextAlignment = TextAlignment.Center, RenderTransform = move };
            SetLeft(t, WheelWindow.Center + r * Math.Cos(mid) - 35);
            SetTop(t, WheelWindow.Center + r * Math.Sin(mid) - 8);
            return t;
        }
        for (var i = 0; i < n; i++)
        {
            var mid = -Math.PI / 2 + i * span;
            var shape = WheelWindow.SegmentGeometry(mid - span / 2, mid + span / 2);
            _wheel.Children.Add(new Path { Data = shape, Fill = Brushes.White });
            _wheel.Children.Add(new Path { Data = shape, Fill = veil, Stroke = edge, StrokeThickness = 0.6 });
            _wheel.Children.Add(Label(step.Options[i], ink, mid, null));
        }
        // The pick: orange layer and white name on top, pushed outward like the real hot segment.
        var pickMid = -Math.PI / 2 + step.Pick * span;
        var push = new TranslateTransform(WheelWindow.Push * Math.Cos(pickMid), WheelWindow.Push * Math.Sin(pickMid));
        var hot = new Canvas { RenderTransform = push };
        hot.Children.Add(new Path { Data = WheelWindow.SegmentGeometry(pickMid - span / 2, pickMid + span / 2), Fill = WheelWindow.Accent });
        hot.Children.Add(Label(step.Options[step.Pick], Brushes.White, pickMid, null));
        _wheel.Children.Add(hot);

        var d = WheelWindow.CenterRadius * 2;
        var disc = new Grid { Width = d, Height = d };
        disc.Children.Add(new Ellipse { Fill = Brushes.White, Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0))), StrokeThickness = 0.5 });
        _logo = new Image { Source = WheelWindow.Logo, Width = d * 0.62, Height = d * 0.62 };
        var name = step.Options[step.Pick].ToUpperInvariant();
        _hub = new TextBlock
        {
            Text = name, FontWeight = FontWeights.Bold, FontSize = name.Length > 6 ? 8 : 11, Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        disc.Children.Add(_logo);
        disc.Children.Add(_hub);
        SetLeft(disc, WheelWindow.Center - WheelWindow.CenterRadius);
        SetTop(disc, WheelWindow.Center - WheelWindow.CenterRadius);
        _wheel.Children.Add(disc);
        _hotLayer = hot;
        _madeName.Text = step.Output ?? "";
        _noticeText.Text = step.Notice;
        _noticeLink.Visibility = step.Output != null ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
