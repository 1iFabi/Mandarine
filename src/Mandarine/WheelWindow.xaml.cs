using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Mandarine;

public enum WheelMode { Convert, Tools, Actions }

public enum WheelTheme { Light, Dark }

public partial class WheelWindow : Window
{
    // Geometry in DIPs: a ring of frosted segments around a centre disc.
    internal const double Size = 210, Center = Size / 2, Outer = 90, Inner = 42, CenterRadius = 32, Gap = 3, Corner = 5, Push = 5;

    /// <summary>Frosted glass: the blurred desktop under a milky veil, ink that reads on it.</summary>
    internal sealed record Palette(Color Veil, Color CenterVeil, Color Ink);

    internal static readonly Palette Light = new(Color.FromArgb(0xC8, 0xF7, 0xF7, 0xF8), Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), Color.FromRgb(0x3A, 0x3A, 0x3C));
    static readonly Palette Dark = new(Color.FromArgb(0xC0, 0x2C, 0x2C, 0x2E), Color.FromArgb(0xE8, 0x3A, 0x3A, 0x3C), Color.FromRgb(0xF2, 0xF2, 0xF7));

    internal static readonly Brush Accent = Frozen(new LinearGradientBrush(Color.FromRgb(0xFF, 0x6A, 0x1F), Color.FromRgb(0xF2, 0x48, 0x00), 90));
    static readonly FontFamily Face = new("Segoe UI Variable Text, Segoe UI");
    static readonly FontFamily IconFace = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    internal static readonly ImageSource? Logo = LoadLogo();

    readonly DispatcherTimer _releaseWatch = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly SpringClock _clock = new();
    readonly List<Segment> _segments = new();
    Spring _presence = new(0); // 0 = gone, 1 = fully materialised
    Spring _swap = new(0);     // centre: 0 = logo, 1 = the hot entry's name
    Brush _frost = Brushes.Transparent;
    Palette _palette = Light;
    double _dpi = 1;
    string[]? _files;
    int _releasedTicks, _waitTicks, _hot = -1;
    bool _leaving, _waiting;
    TextBlock? _centerLabel;

    /// <summary>Hide when the mouse button is released. Off only for previews, where no drag exists.</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>True while the wheel sits unseen under the pointer, waiting for the drag to bring files.</summary>
    public bool IsWaitingForFiles => _waiting;

    /// <summary>Convert (Shift) shows target formats, Tools (Ctrl) file tools, Actions (Alt) file actions.</summary>
    public WheelMode Mode { get; set; }

    /// <summary>Light by default; applies from the next time the wheel opens.</summary>
    public WheelTheme Theme { get; set; }

    /// <summary>Raised after a drop: files and chosen entry.</summary>
    public event Action<string[], Format>? Converted;

    public WheelWindow()
    {
        InitializeComponent();
        DragEnter += (_, e) => { Prepare(e); Track(e); };
        DragOver += (_, e) => Track(e);
        DragLeave += (_, _) => PointAt(null);
        Drop += (_, e) =>
        {
            e.Handled = true;
            var hot = _hot;
            var files = _files;
            e.Effects = hot >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
            HideWheel();
            if (hot >= 0 && files != null) Converted?.Invoke(files, _segments[hot].Format);
        };
        // Hide once the mouse button has stayed up for ~300 ms, after OLE has delivered any drop.
        _releaseWatch.Tick += (_, _) =>
        {
            _releasedTicks = KeyboardHook.LeftButtonDown ? 0 : _releasedTicks + 1;
            // A held button with a key is not always a file drag (selecting text, moving a window): no files in 2 s, no wheel.
            if (_releasedTicks >= 6 || (_waiting && ++_waitTicks >= 40)) HideWheel();
        };
        _clock.Settled += () => { if (_leaving) { _leaving = false; Hide(); } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never steal focus from the app the drag started in.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void ShowAtCursor(WheelMode mode)
    {
        if (IsVisible && !_leaving)
        {
            // The other key mid-drag swaps the segments in place.
            if (mode != Mode) { Mode = mode; Rebuild(); }
            return;
        }
        Mode = mode;
        new WindowInteropHelper(this).EnsureHandle();
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var p = System.Windows.Forms.Cursor.Position;
        var side = (int)Math.Round(Size * _dpi);
        // Capture what is behind the wheel before it exists; the frost is made from it.
        ShowAt(Backdrop.Capture(p.X - side / 2, p.Y - side / 2, side, side), p.X - side / 2, p.Y - side / 2);
    }

    /// <summary>Shows the wheel over a given backdrop (physical px). Separate from the capture for previews.</summary>
    public void ShowAt(Backdrop backdrop, int left, int top)
    {
        new WindowInteropHelper(this).EnsureHandle();
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Windows "Animation effects" switch (Settings > Accessibility).
        _clock.ReduceMotion = !SystemParameters.ClientAreaAnimation;
        _palette = Theme == WheelTheme.Dark ? Dark : Light;
        // Frost: the desktop blurred well past legibility; the veil on top does the rest.
        _frost = Frozen(new ImageBrush(backdrop.Blurred((int)Math.Round(14 * _dpi)).ToBitmap(_dpi))
        {
            Viewport = new Rect(0, 0, Size, Size), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
        });

        _leaving = false;
        _clock.Clear();
        _segments.Clear();
        Petals.Children.Clear();
        _files = null;
        _hot = -1;
        Left = left / _dpi;
        Top = top / _dpi;
        BuildCenter();

        // Materialise: blur and scale arrive together with the opacity, critically damped.
        _presence = _clock.Add(new Spring(0) { Response = 0.28 }, v =>
        {
            Root.Opacity = Math.Clamp(v, 0, 1);
            RootScale.ScaleX = RootScale.ScaleY = 0.88 + 0.12 * v;
            RootBlur.Radius = Math.Max(0, (1 - v) * 10);
        });
        if (AutoHide)
        {
            // Unseen but still a drop target (the root's near-invisible fill needs full opacity): it appears once the drag brings files.
            _waiting = true;
            Root.Opacity = 1;
            RootScale.ScaleX = RootScale.ScaleY = 1;
            RootBlur.Radius = 0;
            Petals.Visibility = CenterDrop.Visibility = Visibility.Hidden;
        }
        else Reveal();

        _releasedTicks = _waitTicks = 0;
        Show();
        if (AutoHide) _releaseWatch.Start();
    }

    /// <summary>Leaves the way it came: the material dissolves back into the pointer.</summary>
    public void HideWheel()
    {
        _releaseWatch.Stop();
        if (!IsVisible || _leaving) return;
        if (_waiting) { _waiting = false; Hide(); return; } // never seen: nothing to dissolve
        _leaving = true;
        _presence.Response = 0.18;
        _presence.Target = 0;
        if (_clock.ReduceMotion) { _leaving = false; Hide(); return; }
        _clock.Kick();
    }

    void Prepare(DragEventArgs e)
    {
        if (_files != null) return; // segments already built for this drag
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) ShowTargets(files);
    }

    /// <summary>Builds the segments for the dragged files. Public so the preview test can render it.</summary>
    public void ShowTargets(string[] files)
    {
        _files = files;
        if (_waiting) Reveal();
        var entries = Mode switch
        {
            WheelMode.Tools => Catalog.ToolsFor(files),
            WheelMode.Actions => Catalog.ActionsFor(files),
            _ => Catalog.TargetsFor(files),
        };
        for (var i = 0; i < entries.Count; i++) AddSegment(entries[i], i, entries.Count);
        // Nothing to offer: say so in the centre instead of the logo.
        SetCenter(entries.Count == 0 ? "Sin opciones" : null);
        _clock.Kick();
    }

    void Reveal()
    {
        _waiting = false;
        Petals.Visibility = CenterDrop.Visibility = Visibility.Visible;
        _presence.Target = 1;
        _clock.Kick();
    }

    void Rebuild()
    {
        foreach (var s in _segments)
            foreach (var spring in new[] { s.Appear, s.Lift, s.Tint }) _clock.Remove(spring);
        _segments.Clear();
        Petals.Children.Clear();
        _hot = -1;
        if (_files != null) ShowTargets(_files);
    }

    void Track(DragEventArgs e)
    {
        e.Handled = true;
        PointAt(e.GetPosition(Root));
        e.Effects = _hot >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>
    /// Radial selection: the direction from the centre picks a segment, so the whole sector is the
    /// target (Fitts). Inside the centre disc nothing is chosen, which is how a drop is cancelled.
    /// Public so the preview test can render the hot state.
    /// </summary>
    public void PointAt(Point? pointer)
    {
        var n = _segments.Count;
        var hot = -1;
        if (pointer is { } p && n > 0)
        {
            double dx = p.X - Center, dy = p.Y - Center;
            if (Math.Sqrt(dx * dx + dy * dy) > CenterRadius)
                hot = (int)Math.Round(Mod(Math.Atan2(dy, dx) + Math.PI / 2, 2 * Math.PI) / (2 * Math.PI / n)) % n;
        }
        if (hot == _hot) return;
        for (var i = 0; i < n; i++)
        {
            var s = _segments[i];
            s.Lift.Target = i == hot ? 1 : 0;
            s.Tint.Target = i == hot ? 1 : 0;
            s.SetInk(i == hot ? Brushes.White : Frozen(new SolidColorBrush(_palette.Ink)));
        }
        SetCenter(hot >= 0 ? _segments[hot].Format.Label : null);
        _hot = hot;
        _clock.Kick();
    }

    // ---- Centre: the Mandarine slice at rest, the hot entry's name while pointing. ----

    void BuildCenter()
    {
        CenterDrop.Children.Clear();
        var d = CenterRadius * 2;
        var disc = new Grid { Width = d, Height = d };
        disc.Children.Add(new Ellipse { Fill = _frost });
        disc.Children.Add(new Ellipse { Fill = Frozen(new SolidColorBrush(_palette.CenterVeil)) });
        disc.Children.Add(new Ellipse { Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0))), StrokeThickness = 0.5 });
        var logo = new Image { Source = Logo, Width = d * 0.62, Height = d * 0.62 };
        _centerLabel = new TextBlock
        {
            FontFamily = Face, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Frozen(new SolidColorBrush(_palette.Ink)),
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        disc.Children.Add(logo);
        disc.Children.Add(_centerLabel);
        Canvas.SetLeft(disc, Center - CenterRadius);
        Canvas.SetTop(disc, Center - CenterRadius);
        CenterDrop.Children.Add(disc);
        // Logo and name cross-fade, with a slight shrink of the one leaving.
        _swap = _clock.Add(new Spring(0) { Response = 0.16 }, v =>
        {
            logo.Opacity = 1 - v;
            logo.RenderTransformOrigin = new Point(0.5, 0.5);
            logo.RenderTransform = new ScaleTransform(1 - 0.15 * v, 1 - 0.15 * v);
            _centerLabel.Opacity = v;
        });
    }

    void SetCenter(string? text)
    {
        if (_centerLabel == null) return;
        if (text != null)
        {
            _centerLabel.Text = text.ToUpper(CultureInfo.CurrentCulture);
            _centerLabel.FontSize = Fit(_centerLabel.Text, Face, FontWeights.Bold, 11, CenterRadius * 1.7, 7);
        }
        _swap.Target = text != null ? 1 : 0;
    }

    // ---- Segments ----

    void AddSegment(Format format, int index, int count)
    {
        var step = 2 * Math.PI / count;
        var mid = -Math.PI / 2 + index * step;
        var shape = SegmentGeometry(mid - step / 2, mid + step / 2);
        Path Layer(Brush fill, double opacity = 1) => new() { Data = shape, Fill = fill, Opacity = opacity };
        var tint = Layer(Accent, 0);
        var ink = Frozen(new SolidColorBrush(_palette.Ink));

        // Contents sit at the middle of the band: icon over name for tools, the name alone for formats.
        var room = (Inner + Outer) / 2 * step - 14;
        var label = new TextBlock
        {
            Text = format.Icon != null ? format.Label.ToUpper(CultureInfo.CurrentCulture) : format.Label,
            FontFamily = Face, FontWeight = format.Icon != null ? FontWeights.SemiBold : FontWeights.Bold,
            Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.FontSize = format.Icon != null
            ? Fit(label.Text, Face, FontWeights.SemiBold, 8.5, room, 6.5)
            : Fit(label.Text, Face, FontWeights.Bold, 12, room, 8);
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        TextBlock? icon = null;
        if (format.Icon != null)
        {
            icon = new TextBlock { Text = format.Icon, FontFamily = IconFace, FontSize = 15, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
            content.Children.Add(icon);
        }
        // Crowded tool wheels show icons only; the centre names the one pointed at.
        if (format.Icon == null || count <= 8) content.Children.Add(label);
        else icon!.Margin = new Thickness(0);
        var box = new Grid { Width = Outer - Inner, Height = Outer - Inner, Children = { content } };
        var r = (Inner + Outer) / 2;
        Canvas.SetLeft(box, Center + r * Math.Cos(mid) - box.Width / 2);
        Canvas.SetTop(box, Center + r * Math.Sin(mid) - box.Height / 2);

        var scale = new ScaleTransform();
        var move = new TranslateTransform();
        var root = new Canvas
        {
            Width = Size, Height = Size, IsHitTestVisible = false, // the window resolves the target by direction
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, move } },
            Children = { Layer(_frost), Layer(Frozen(new SolidColorBrush(_palette.Veil))), tint, box },
        };
        Petals.Children.Add(root);

        var segment = new Segment(format, label, icon);
        double appear = 0, lift = 0;
        void Apply()
        {
            // Segments unfold from the centre; the hot one rises out of the ring toward the pointer.
            scale.ScaleX = scale.ScaleY = 0.72 + 0.28 * appear;
            move.X = Push * lift * Math.Cos(mid);
            move.Y = Push * lift * Math.Sin(mid);
            root.Opacity = Math.Clamp(appear * 1.4, 0, 1);
        }
        // Entrance: a hint of overshoot so it feels alive; everything else is critically damped.
        segment.Appear = _clock.Add(new Spring(0) { DampingRatio = 0.8, Response = 0.36, Delay = index * 0.02, Target = 1 }, v => { appear = v; Apply(); });
        segment.Lift = _clock.Add(new Spring(0) { Response = 0.2 }, v => { lift = v; Apply(); });
        segment.Tint = _clock.Add(new Spring(0) { Response = 0.14 }, v => tint.Opacity = Math.Clamp(v, 0, 1));
        _segments.Add(segment);
    }

    sealed class Segment
    {
        public Segment(Format format, TextBlock label, TextBlock? icon) { Format = format; _label = label; _icon = icon; }
        public readonly Format Format;
        readonly TextBlock _label;
        readonly TextBlock? _icon;
        public Spring Appear = null!, Lift = null!, Tint = null!;

        public void SetInk(Brush ink)
        {
            _label.Foreground = ink;
            if (_icon != null) _icon.Foreground = ink;
        }
    }

    /// <summary>An annular sector from a0 to a1 (radians), inset by the gap and the corner radius.</summary>
    internal static Geometry SegmentGeometry(double a0, double a1)
    {
        double ro = Outer - Corner, ri = Inner + Corner, inset = Gap / 2 + Corner;
        double o0 = a0 + Math.Asin(inset / ro), o1 = a1 - Math.Asin(inset / ro);
        double i0 = a0 + Math.Asin(inset / ri), i1 = a1 - Math.Asin(inset / ri);
        static Point At(double r, double a) => new(Center + r * Math.Cos(a), Center + r * Math.Sin(a));
        var figure = new PathFigure { StartPoint = At(ri, i0), IsClosed = true };
        figure.Segments.Add(new LineSegment(At(ro, o0), true));
        figure.Segments.Add(new ArcSegment(At(ro, o1), new Size(ro, ro), 0, o1 - o0 > Math.PI, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(ri, i1), true));
        figure.Segments.Add(new ArcSegment(At(ri, i0), new Size(ri, ri), 0, i1 - i0 > Math.PI, SweepDirection.Counterclockwise, true));
        // Rounded corners: grow the shrunk shape back by its round-joined outline, as one fill so translucent layers don't double up at the edge.
        var core = new PathGeometry(new[] { figure });
        var rim = core.GetWidenedPathGeometry(new Pen(Brushes.Black, 2 * Corner) { LineJoin = PenLineJoin.Round });
        return Frozen(Geometry.Combine(core, rim, GeometryCombineMode.Union, null));
    }

    /// <summary>The font size, at most <paramref name="size"/> and at least <paramref name="min"/>, at which text fits a width.</summary>
    double Fit(string text, FontFamily face, FontWeight weight, double size, double width, double min)
    {
        var measured = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(face, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, _dpi).Width;
        return measured <= width ? size : Math.Max(min, size * width / measured);
    }

    internal static ImageSource? LoadLogo()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "mandarine.png");
        if (!System.IO.File.Exists(path)) return null;
        var logo = new BitmapImage();
        logo.BeginInit();
        logo.UriSource = new Uri(path);
        logo.DecodePixelWidth = 160; // enough for the disc at 200% scale
        logo.CacheOption = BitmapCacheOption.OnLoad;
        logo.EndInit();
        logo.Freeze();
        return logo;
    }

    static double Mod(double a, double m) => (a % m + m) % m;

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
}
