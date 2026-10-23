using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mandarine;

/// <summary>Opens the picture editors for dropped files and saves the results beside them.</summary>
public static class Editor
{
    /// <summary>Returns how many pictures were saved; stops at the first cancel.</summary>
    public static async Task<int> Run(Format tool, string[] files)
    {
        var op = tool.Args[0];
        // Crop, adjustments and frame are set once and applied to every picture (the same share of each);
        // annotations and hidden areas belong to one picture, so each gets its own editor.
        var shared = op is "crop" or "adjust" or "frame";
        Func<BitmapSource, BitmapSource>? apply = null;
        var saved = 0;
        for (var i = 0; i < files.Length; i++)
        {
            var picture = await ImageEdit.Load(files[i]);
            if (apply == null || !shared)
            {
                var progress = files.Length == 1 ? "" : shared ? $"Se aplicará a {files.Length} imágenes" : $"{i + 1} de {files.Length}";
                var editor = new EditorWindow(op, tool.Label, picture, System.IO.Path.GetFileName(files[i]), progress);
                if (editor.ShowDialog() != true) return saved;
                apply = editor.Result!;
            }
            ImageEdit.Save(apply(picture), Converter.OutputPath(files[i], op == "frame" ? "png" : ImageEdit.OutputExt(files[i])));
            saved++;
        }
        return saved;
    }
}

public partial class EditorWindow : Window
{
    static readonly (string Name, Color Color)[] Inks =
    {
        ("Naranja", Color.FromRgb(0xFF, 0x52, 0x00)), ("Rojo", Color.FromRgb(0xE5, 0x24, 0x3B)), ("Amarillo", Color.FromRgb(0xFF, 0xD6, 0x0A)),
        ("Azul", Color.FromRgb(0x0A, 0x84, 0xFF)), ("Negro", Color.FromRgb(0x1C, 0x1C, 0x1E)), ("Blanco", Colors.White),
    };

    static readonly (string Name, Brush Fill)[] Backgrounds =
    {
        ("Mandarina", Gradient("#FF9A3D", "#FF3D6E")), ("Océano", Gradient("#4FACFE", "#6A5BFF")), ("Menta", Gradient("#43E97B", "#2BC5D4")),
        ("Arena", Gradient("#FBE3B8", "#F2A97E")), ("Grafito", Gradient("#4A4A50", "#1E1E21")), ("Blanco", Frozen(new SolidColorBrush(Colors.White))),
        ("Transparente", Brushes.Transparent),
    };

    readonly BitmapSource _source;
    readonly int _pw, _ph;
    readonly Grid _surface;
    readonly Image _picture = new() { Stretch = Stretch.Fill };
    readonly Canvas _overlay = new() { Background = Brushes.Transparent, ClipToBounds = true };
    Func<Func<BitmapSource, BitmapSource>> _finish = () => p => p;
    Action? _undo;
    (TextBox Box, Point At, Color Color, Action<Mark> Add)? _typing;

    /// <summary>What to do to each picture, set when the user saves.</summary>
    public Func<BitmapSource, BitmapSource>? Result { get; private set; }

    public EditorWindow(string op, string title, BitmapSource source, string fileName, string progress)
    {
        InitializeComponent();
        _source = source;
        _pw = source.PixelWidth;
        _ph = source.PixelHeight;
        Title = $"{title} · {fileName}";
        Progress.Text = progress;
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, area.Width * 0.72);
        Height = Math.Max(MinHeight, area.Height * 0.82);

        // Inside the stage everything is in picture pixels: pointer positions map straight onto the picture.
        _picture.Width = _overlay.Width = _pw;
        _picture.Height = _overlay.Height = _ph;
        _picture.Source = source;
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        _surface = new Grid { Width = _pw, Height = _ph, Children = { _picture, _overlay } };
        Stage.Child = _surface;

        switch (op)
        {
            case "crop": BuildCrop(); break;
            case "redact": BuildRedact(); break;
            case "annotate": BuildAnnotate(); break;
            case "adjust": BuildAdjust(); break;
            case "frame": BuildFrame(); break;
            default: throw new ArgumentException(op);
        }
        if (Hint.Text.Length == 0) Hint.Visibility = Visibility.Collapsed;

        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => DialogResult = false;
        PreviewKeyDown += OnKey;
        // The drag left another app in the foreground; come to the front once, then behave like any window.
        Topmost = true;
        ContentRendered += (_, _) => { Activate(); Topmost = false; };
    }

    void Save()
    {
        CommitText();
        Result = _finish();
        DialogResult = true;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (_typing is { } t && e.OriginalSource == t.Box)
        {
            if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _overlay.Children.Remove(t.Box); _typing = null; e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter) { Save(); e.Handled = true; }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { _undo?.Invoke(); e.Handled = true; }
    }

    // ---- Crop ----

    void BuildCrop()
    {
        Hint.Text = "Arrastra para elegir el área. Arrastra dentro para moverla o desde una esquina para ajustarla.";
        var crop = new Rect(0, 0, _pw, _ph);
        double ratio = 0;
        var dim = new Path { Fill = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))), IsHitTestVisible = false };
        var border = new Rectangle { Stroke = Brushes.White, IsHitTestVisible = false };
        var thirds = Enumerable.Range(0, 4).Select(_ => new Line { Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255))), IsHitTestVisible = false }).ToArray();
        var handles = Enumerable.Range(0, 4).Select(_ => new Rectangle { Fill = Brushes.White, Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0))), IsHitTestVisible = false }).ToArray();
        _overlay.Children.Add(dim);
        foreach (var l in thirds) _overlay.Children.Add(l);
        _overlay.Children.Add(border);
        foreach (var h in handles) _overlay.Children.Add(h);

        Point[] Corners(Rect r) => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };
        void Draw()
        {
            var k = PxPerDip;
            dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _pw, _ph)), new RectangleGeometry(crop));
            Place(border, crop);
            border.StrokeThickness = 1.5 * k;
            for (var i = 0; i < 2; i++)
            {
                var x = crop.X + crop.Width * (i + 1) / 3;
                var y = crop.Y + crop.Height * (i + 1) / 3;
                (thirds[i].X1, thirds[i].Y1, thirds[i].X2, thirds[i].Y2) = (x, crop.Top, x, crop.Bottom);
                (thirds[i + 2].X1, thirds[i + 2].Y1, thirds[i + 2].X2, thirds[i + 2].Y2) = (crop.Left, y, crop.Right, y);
                thirds[i].StrokeThickness = thirds[i + 2].StrokeThickness = k;
            }
            var s = 14 * k;
            var corners = Corners(crop);
            for (var i = 0; i < 4; i++)
            {
                // Inside the area, so a crop that reaches the picture's edge still shows whole handles.
                Place(handles[i], new Rect(corners[i].X - (i is 1 or 2 ? s : 0), corners[i].Y - (i >= 2 ? s : 0), s, s));
                handles[i].StrokeThickness = k;
            }
        }

        // The largest area of a ratio, centred in the picture.
        Rect Largest(double r) => _pw / (double)_ph > r
            ? new Rect((_pw - _ph * r) / 2, 0, _ph * r, _ph)
            : new Rect(0, (_ph - _pw / r) / 2, _pw, _pw / r);

        Rect Spread(Point anchor, Point p)
        {
            var d = p - anchor;
            if (ratio > 0)
            {
                // Shrinks toward the anchor only, so the area never leaves the picture.
                double w = Math.Abs(d.X), h = Math.Abs(d.Y);
                if (w > h * ratio) w = h * ratio; else h = w / ratio;
                d = new Vector(Math.Sign(d.X) * w, Math.Sign(d.Y) * h);
            }
            return new Rect(anchor, anchor + d);
        }

        Point anchor = default, grab = default;
        var before = crop;
        var moving = false;
        OnDrag(p =>
        {
            before = crop;
            var corners = Corners(crop);
            var near = Array.FindIndex(corners, c => (c - p).Length < 16 * PxPerDip);
            moving = near < 0 && crop.Contains(p);
            if (near >= 0) anchor = corners[(near + 2) % 4];
            else if (moving) grab = p;
            else anchor = p;
        }, p =>
        {
            if (moving)
            {
                var d = p - grab;
                crop = new Rect(Math.Clamp(before.X + d.X, 0, _pw - before.Width), Math.Clamp(before.Y + d.Y, 0, _ph - before.Height), before.Width, before.Height);
            }
            else crop = Spread(anchor, p);
            Draw();
        }, _ =>
        {
            if (crop.Width < 4 * PxPerDip || crop.Height < 4 * PxPerDip) crop = before; // a click, not a drag
            Draw();
        });
        _overlay.Cursor = Cursors.Cross;

        Segmented("Proporción", new (string, Action)[]
        {
            ("Libre", () => ratio = 0), ("1:1", () => Pick(1)), ("4:3", () => Pick(4 / 3.0)), ("3:2", () => Pick(1.5)),
            ("16:9", () => Pick(16 / 9.0)), ("9:16", () => Pick(9 / 16.0)),
        });
        void Pick(double r) { ratio = r; crop = Largest(r); Draw(); }
        AddControl(null, ActionButton("Restablecer", () => { crop = ratio > 0 ? Largest(ratio) : new Rect(0, 0, _pw, _ph); Draw(); }));

        // Handles keep their on-screen size; measured once the stage has laid the picture out.
        Stage.SizeChanged += (_, _) => Dispatcher.BeginInvoke(Draw, System.Windows.Threading.DispatcherPriority.Loaded);
        Draw();
        _finish = () =>
        {
            var share = new Rect(crop.X / _pw, crop.Y / _ph, crop.Width / _pw, crop.Height / _ph);
            return p => ImageEdit.Crop(p, share);
        };
    }

    // ---- Hide (redact) ----

    void BuildRedact()
    {
        Hint.Text = "Arrastra sobre lo que quieras ocultar.";
        var areas = new List<Int32Rect>();
        var pixelate = true;
        var band = new Rectangle { Stroke = (Brush)FindResource("Accent"), StrokeDashArray = { 4, 3 }, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _overlay.Children.Add(band);
        _overlay.Cursor = Cursors.Cross;
        void Refresh() => _picture.Source = ImageEdit.Redact(_source, areas, pixelate);

        Point start = default;
        OnDrag(p =>
        {
            start = p;
            band.StrokeThickness = 1.5 * PxPerDip;
            Place(band, new Rect(p, p));
            band.Visibility = Visibility.Visible;
        }, p => Place(band, new Rect(start, p)), p =>
        {
            band.Visibility = Visibility.Collapsed;
            var r = new Rect(start, p);
            if (r.Width < 3 || r.Height < 3) return;
            areas.Add(new Int32Rect((int)r.X, (int)r.Y, (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)));
            Refresh();
        });

        Segmented("Estilo", new (string, Action)[] { ("Pixelar", () => { pixelate = true; Refresh(); }), ("Tapar", () => { pixelate = false; Refresh(); }) });
        _undo = () => { if (areas.Count == 0) return; areas.RemoveAt(areas.Count - 1); Refresh(); };
        AddControl(null, ActionButton("Deshacer", () => _undo?.Invoke()));
        _finish = () =>
        {
            var chosen = areas.ToList();
            var style = pixelate;
            return p => ImageEdit.Redact(p, chosen, style);
        };
    }

    // ---- Annotate ----

    sealed class MarksView : FrameworkElement
    {
        public readonly List<Mark> Marks = new();
        public Mark? Draft;
        protected override void OnRender(DrawingContext dc) =>
            ImageEdit.DrawMarks(dc, Draft == null ? Marks : Marks.Append(Draft), Width, Height);
    }

    void BuildAnnotate()
    {
        Hint.Text = "Dibuja sobre la imagen. Con Texto, haz clic donde quieras escribir.";
        var view = new MarksView { Width = _pw, Height = _ph, IsHitTestVisible = false };
        _overlay.Children.Add(view);
        var kind = MarkKind.Arrow;
        var color = Inks[0].Color;
        var points = new List<Point>();
        void Preview() { view.Draft = new Mark(kind, color, points.ToArray()); view.InvalidateVisual(); }

        OnDrag(p =>
        {
            CommitText();
            if (kind == MarkKind.Text) { BeginText(p, color, m => { view.Marks.Add(m); view.InvalidateVisual(); }); return; }
            points = new List<Point> { p, p };
            Preview();
        }, p =>
        {
            if (kind == MarkKind.Text) return;
            if (kind == MarkKind.Pen) points.Add(p); else points[1] = p;
            Preview();
        }, _ =>
        {
            var mark = view.Draft;
            view.Draft = null;
            // A click without a drag leaves nothing.
            if (mark != null && Extent(mark.Points) > 3 * PxPerDip) view.Marks.Add(mark);
            view.InvalidateVisual();
        });
        _overlay.Cursor = Cursors.Cross;

        Segmented("Herramienta", new (string, Action)[]
        {
            ("Flecha", () => Tool(MarkKind.Arrow)), ("Rectángulo", () => Tool(MarkKind.Box)), ("Lápiz", () => Tool(MarkKind.Pen)),
            ("Resaltar", () => Tool(MarkKind.Highlight)), ("Texto", () => Tool(MarkKind.Text)),
        });
        void Tool(MarkKind k) { CommitText(); kind = k; _overlay.Cursor = k == MarkKind.Text ? Cursors.IBeam : Cursors.Cross; }
        Swatches("Color", Inks.Select(i => (i.Name, (Brush)Frozen(new SolidColorBrush(i.Color)), (Action)(() => color = i.Color))));
        _undo = () =>
        {
            CommitText();
            if (view.Marks.Count == 0) return;
            view.Marks.RemoveAt(view.Marks.Count - 1);
            view.InvalidateVisual();
        };
        AddControl(null, ActionButton("Deshacer", () => _undo?.Invoke()));
        _finish = () =>
        {
            var marks = view.Marks.ToList();
            return p => ImageEdit.Annotate(p, marks);
        };
    }

    void BeginText(Point at, Color color, Action<Mark> add)
    {
        var brush = Frozen(new SolidColorBrush(color));
        var box = new TextBox
        {
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold, FontSize = ImageEdit.TextSizeFor(_pw, _ph),
            Foreground = brush, CaretBrush = brush, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            MinWidth = ImageEdit.TextSizeFor(_pw, _ph),
        };
        AutomationProperties.SetName(box, "Texto de la anotación");
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
        _overlay.Children.Add(box);
        _typing = (box, at, color, add);
        box.LostKeyboardFocus += (_, _) => CommitText();
        Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    void CommitText()
    {
        if (_typing is not { } t) return;
        _typing = null;
        _overlay.Children.Remove(t.Box);
        if (t.Box.Text.Trim().Length > 0) t.Add(new Mark(MarkKind.Text, t.Color, new[] { t.At }, t.Box.Text.Trim()));
    }

    // ---- Adjustments ----

    void BuildAdjust()
    {
        var preview = ImageEdit.Scaled(_source, 1600); // live preview; the full picture is processed on save
        var values = new Adjustments();
        _overlay.IsHitTestVisible = false;
        void Refresh() => _picture.Source = ImageEdit.Adjust(preview, values);
        var sliders = new List<Slider>
        {
            AddSlider("Brillo", v => values = values with { Brightness = v }),
            AddSlider("Contraste", v => values = values with { Contrast = v }),
            AddSlider("Saturación", v => values = values with { Saturation = v }),
            AddSlider("Calidez", v => values = values with { Warmth = v }),
        };
        AddControl(null, ActionButton("Restablecer", () => sliders.ForEach(s => s.Value = 0)));
        Refresh();

        Slider AddSlider(string caption, Action<double> set)
        {
            var slider = new Slider { Minimum = -100, Maximum = 100, Width = 100, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            AutomationProperties.SetName(slider, caption);
            slider.ValueChanged += (_, e) => { set(e.NewValue / 100); Refresh(); };
            slider.MouseDoubleClick += (_, _) => slider.Value = 0;
            AddControl(caption, slider);
            return slider;
        }
        _finish = () =>
        {
            var chosen = values;
            return p => ImageEdit.Adjust(p, chosen);
        };
    }

    // ---- Background / frame ----

    void BuildFrame()
    {
        var preview = ImageEdit.Scaled(_source, 900);
        var style = new FrameStyle(Backgrounds[0].Fill, 0.08, 0.03, true, 0);
        var shown = new Image();
        Stage.Child = shown;
        void Refresh() => shown.Source = ImageEdit.Frame(preview, style);

        Swatches("Fondo", Backgrounds.Select(b => (b.Name, b.Fill == Brushes.Transparent ? Checker() : b.Fill, (Action)(() => { style = style with { Background = b.Fill }; Refresh(); }))));
        AddControl("Margen", SliderFor(0, 25, 8, v => style = style with { Margin = v / 100 }));
        AddControl("Esquinas", SliderFor(0, 12, 3, v => style = style with { Corners = v / 100 }));
        var shadow = new CheckBox { Content = "Sombra", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        shadow.Click += (_, _) => { style = style with { Shadow = shadow.IsChecked == true }; Refresh(); };
        AddControl(null, shadow);
        Segmented("Proporción", new (string, Action)[]
        {
            ("Auto", () => Ratio(0)), ("1:1", () => Ratio(1)), ("4:3", () => Ratio(4 / 3.0)), ("16:9", () => Ratio(16 / 9.0)), ("9:16", () => Ratio(9 / 16.0)),
        });
        void Ratio(double r) { style = style with { Ratio = r }; Refresh(); }
        Refresh();

        Slider SliderFor(double min, double max, double value, Action<double> set)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 100, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            slider.ValueChanged += (_, e) => { set(e.NewValue); Refresh(); };
            return slider;
        }
        _finish = () =>
        {
            var chosen = style;
            return p => ImageEdit.Frame(p, chosen);
        };
    }

    // ---- Shared pieces ----

    /// <summary>Picture pixels per screen DIP, so handles and outlines keep a constant on-screen size.</summary>
    double PxPerDip
    {
        get
        {
            if (!_surface.IsVisible || Stage.ActualWidth == 0) return 1;
            var onScreen = _surface.TransformToAncestor(Stage).TransformBounds(new Rect(0, 0, _pw, _ph)).Width;
            return onScreen > 0 ? _pw / onScreen : 1;
        }
    }

    void OnDrag(Action<Point> down, Action<Point> move, Action<Point> up)
    {
        Point At(MouseEventArgs e)
        {
            var p = e.GetPosition(_overlay);
            return new Point(Math.Clamp(p.X, 0, _pw), Math.Clamp(p.Y, 0, _ph));
        }
        _overlay.MouseLeftButtonDown += (_, e) => { _overlay.CaptureMouse(); down(At(e)); e.Handled = true; };
        _overlay.MouseMove += (_, e) => { if (_overlay.IsMouseCaptured) move(At(e)); };
        _overlay.MouseLeftButtonUp += (_, e) =>
        {
            if (!_overlay.IsMouseCaptured) return;
            _overlay.ReleaseMouseCapture();
            up(At(e));
        };
    }

    void AddControl(string? caption, UIElement control)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 22, 4) };
        if (caption != null) group.Children.Add(new TextBlock { Text = caption, Style = (Style)FindResource("Caption") });
        group.Children.Add(control);
        Controls.Children.Add(group);
    }

    void Segmented(string caption, IEnumerable<(string Label, Action Pick)> options)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var (label, pick) in options)
        {
            var chip = new RadioButton { Content = label, GroupName = group, Style = (Style)FindResource("Chip"), IsChecked = row.Children.Count == 0 };
            chip.Checked += (_, _) => pick();
            row.Children.Add(chip);
        }
        AddControl(caption, new Border { Style = (Style)FindResource("Segmented"), Child = row });
    }

    void Swatches(string caption, IEnumerable<(string Name, Brush Fill, Action Pick)> options)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var (name, fill, pick) in options)
        {
            var swatch = new RadioButton { Background = fill, ToolTip = name, GroupName = group, Style = (Style)FindResource("Swatch"), IsChecked = row.Children.Count == 0 };
            AutomationProperties.SetName(swatch, name);
            swatch.Checked += (_, _) => pick();
            row.Children.Add(swatch);
        }
        AddControl(caption, row);
    }

    Button ActionButton(string label, Action click)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("Action"), Margin = new Thickness(0), Padding = new Thickness(12, 5, 12, 5) };
        button.Click += (_, _) => click();
        return button;
    }

    static void Place(FrameworkElement e, Rect r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = r.Width;
        e.Height = r.Height;
    }

    static double Extent(Point[] points)
    {
        double x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), y0 = points.Min(p => p.Y), y1 = points.Max(p => p.Y);
        return Math.Max(x1 - x0, y1 - y0);
    }

    static Brush Gradient(string from, string to) =>
        Frozen(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to), new Point(0, 0), new Point(1, 1)));

    /// <summary>The usual sign for "transparent": a grey checkerboard.</summary>
    static Brush Checker()
    {
        var squares = new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 4, 4)), new RectangleGeometry(new Rect(4, 4, 4, 4)) } };
        return Frozen(new DrawingBrush(new GeometryDrawing(Brushes.LightGray, null, squares))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 8, 8), ViewboxUnits = BrushMappingMode.Absolute,
        });
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
