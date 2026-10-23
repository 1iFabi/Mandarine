using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Mandarine;

/// <summary>A PDF's pages as thumbnails: choose pages and an angle (Rotate) or the page ranges to save (Split).</summary>
public partial class PdfPagesWindow : Window
{
    readonly string _pdf;
    readonly IReadOnlyList<BitmapSource> _pages;
    readonly List<(Grid Stage, Image Page)> _views = new();
    double _box = 200; // each page fits a square, so turned pages keep their size
    Func<int, double> _angleOf = _ => 0;

    // The reader. Zoom 1 = the page at 96 dpi; Fit re-fits it whenever the window changes.
    enum Fit { None, Page, Width }
    int _shown = -1;
    double _zoom = 1;
    Fit _fit = Fit.Page;
    int _renderVersion;
    readonly List<(int Page, double Dpi, BitmapSource Image)> _sharp = new(); // newest last, a few kept

    /// <summary>The pdfcraft job for the choices, set when the user saves.</summary>
    public Format? Result { get; private set; }

    Button SaveButton => Pane.SaveButton;
    Button CancelButton => Pane.CancelButton;

    public PdfPagesWindow(string op, string title, string pdf, IReadOnlyList<BitmapSource> pages, string progress)
    {
        InitializeComponent();
        _pdf = pdf;
        _pages = pages;
        Title = $"{title} · {Path.GetFileName(pdf)}";
        Icon = WheelWindow.LoadLogo();
        Pane.Show(title, Path.GetFileName(pdf), progress);
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, area.Width * 0.72);
        Height = Math.Max(MinHeight, area.Height * 0.82);

        switch (op)
        {
            case "pdfrotate": BuildRotate(); break;
            case "pdfsplit": BuildSplit(); break;
            default: throw new ArgumentException(op);
        }

        var size = new Slider { Minimum = 120, Maximum = 360, Value = _box };
        AutomationProperties.SetName(size, "Tamaño de las miniaturas");
        size.ValueChanged += (_, e) => Zoom(e.NewValue);
        AddControl("Tamaño de las miniaturas (Ctrl + rueda)", size);
        Scroller.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            size.Value += e.Delta > 0 ? 30 : -30;
            e.Handled = true;
        };

        BuildReader();
        CancelButton.Click += (_, _) => DialogResult = false;
        PreviewKeyDown += OnKey;
        // The drag left another app in the foreground; come to the front once, then behave like any window.
        Topmost = true;
        ContentRendered += (_, _) => { Activate(); Topmost = false; };
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Viewer.IsVisible)
        {
            if (e.Key == Key.Escape) HideViewer();
            else if (e.Key is Key.Left or Key.PageUp) Show(_shown - 1);
            else if (e.Key is Key.Right or Key.PageDown) Show(_shown + 1);
            else if (e.Key is Key.Add or Key.OemPlus) ZoomBy(1.25);
            else if (e.Key is Key.Subtract or Key.OemMinus) ZoomBy(1 / 1.25);
            else if (e.Key is Key.D0 or Key.NumPad0) SetFit(Fit.Page);
            else if (e.Key == Key.W) SetFit(Fit.Width);
            else return; // arrows up/down scroll the page
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter && e.OriginalSource is TextBox) { Keyboard.ClearFocus(); e.Handled = true; } // done typing a page
        else if (e.Key == Key.Enter && SaveButton.IsEnabled) { SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; }
    }

    /// <summary>Every page rendered small, in order.</summary>
    public static async Task<IReadOnlyList<BitmapSource>> Thumbnails(string pdf)
    {
        // shortcut: renders every page up front (a few seconds for hundreds of pages); render while scrolling if that bothers anyone.
        return await Render(pdf, null, 48);
    }

    /// <summary>The given pages (1-based; null = all) as images, in order.</summary>
    static async Task<IReadOnlyList<BitmapSource>> Render(string pdf, int? page, double dpi)
    {
        var tmp = Directory.CreateTempSubdirectory("mandarine-").FullName;
        try
        {
            var pages = page is { } p ? ",\"pages\":[" + p + "]" : "";
            var steps = Catalog.PdfSteps("{\"tool\":\"doc_export_images\",\"args\":{\"doc\":1,\"folder\":{out},\"format\":\"png\",\"dpi\":" + dpi + pages + "}}");
            await Converter.RunTool(new[] { pdf }, tmp, new Format("", "", steps, Engine.Pdfcraft));
            return Directory.GetFiles(tmp).OrderBy(f => f.Length).ThenBy(f => f, StringComparer.Ordinal).Select(Load).ToList(); // page_9 before page_10
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }

        static BitmapSource Load(string path)
        {
            using var s = File.OpenRead(path);
            var frame = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
    }

    // ---- Rotate ----

    void BuildRotate()
    {
        var degrees = new int[_pages.Count];
        _angleOf = i => degrees[i];
        var tiles = new List<(ToggleButton Tile, RotateTransform Turn)>();
        for (var i = 0; i < _pages.Count; i++)
        {
            var turn = new RotateTransform();
            var tile = new ToggleButton { Style = (Style)FindResource("PageTile"), Content = PageView(i, turn, out _) };
            AutomationProperties.SetName(tile, $"Página {i + 1}");
            tile.Click += (_, _) => Refresh();
            var page = i;
            tile.MouseDoubleClick += (_, _) => Show(page); // the second click undid the first: the choice is unchanged
            tiles.Add((tile, turn));
            Pages.Children.Add(tile);
        }

        // An angle from the original page, so 90 twice stays 90; with nothing chosen it applies to every page.
        void Set(int angle)
        {
            var chosen = tiles.Any(t => t.Tile.IsChecked == true);
            for (var i = 0; i < tiles.Count; i++)
                if (!chosen || tiles[i].Tile.IsChecked == true) degrees[i] = angle;
            Refresh();
        }
        void Refresh()
        {
            for (var i = 0; i < tiles.Count; i++) tiles[i].Turn.Angle = degrees[i];
            var chosen = tiles.Count(t => t.Tile.IsChecked == true);
            Hint.Text = chosen == 0
                ? "Haz clic en las páginas para girar solo esas; si no eliges ninguna, se giran todas. Doble clic para verla en grande."
                : $"{PageCount(chosen)} elegida{(chosen == 1 ? "" : "s")}. Ctrl+A elige todas.";
            var turned = degrees.Count(d => d != 0);
            SaveButton.IsEnabled = turned > 0;
            Pane.Output(Converter.OutputPath(_pdf, "pdf"), turned == 0 ? "Elige un ángulo" : $"{turned} de {PageCount(degrees.Length)} girada{(turned == 1 ? "" : "s")}");
        }

        AddControl("Girar en el sentido del reloj", Row(Button("90°", () => Set(90), "90° a la derecha"), Button("180°", () => Set(180), "Media vuelta"),
            Button("270°", () => Set(270), "90° a la izquierda")));
        AddControl("Páginas", Row(Button("Todas", () => { tiles.ForEach(t => t.Tile.IsChecked = true); Refresh(); }, "Ctrl+A"),
            Button("Ninguna", () => { tiles.ForEach(t => t.Tile.IsChecked = false); Refresh(); }), Button("Sin girar", () => Set(0))));
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.A || Keyboard.Modifiers != ModifierKeys.Control || Viewer.IsVisible) return;
            tiles.ForEach(t => t.Tile.IsChecked = true);
            Refresh();
            e.Handled = true;
        };
        SaveButton.Click += (_, _) => { Result = Catalog.PdfRotation(degrees); DialogResult = true; };
        Refresh();
    }

    // ---- Split ----

    sealed class Part { public int From, To; }

    void BuildSplit()
    {
        var n = _pages.Count;
        var parts = new List<Part> { new() { From = 1, To = n } };
        var sideHint = new TextBlock { Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var partList = new StackPanel();
        var shortcuts = new StackPanel();
        Pane.Body.Children.Add(sideHint);
        AddControl(null, partList);
        AddControl(null, shortcuts);
        var cuts = new List<ToggleButton>(); // cuts[i] sits between pages i + 1 and i + 2
        var captions = new List<TextBlock>();
        var rows = new List<(Border Row, TextBlock Count)>();

        for (var i = 0; i < n; i++)
        {
            var unit = new StackPanel { Orientation = Orientation.Horizontal };
            var page = i;
            var view = new ContentControl { Focusable = false, Margin = new Thickness(4, 6, 4, 6), Padding = new Thickness(8, 8, 8, 6), Content = PageView(i, null, out var caption) };
            view.MouseDoubleClick += (_, _) => Show(page);
            captions.Add(caption);
            unit.Children.Add(view);
            if (i < n - 1)
            {
                var cut = new ToggleButton { Style = (Style)FindResource("Cut"), ToolTip = $"Cortar entre la página {i + 1} y la {i + 2}" };
                AutomationProperties.SetName(cut, $"Cortar después de la página {i + 1}");
                cut.Click += (_, _) => { Toggle(page + 1); Rebuild(); };
                cuts.Add(cut);
                unit.Children.Add(cut);
            }
            Pages.Children.Add(unit);
        }

        bool Joined(int p) => parts.Any(r => r.From <= p && p + 1 <= r.To);
        bool IsCut(int p) => !Joined(p) && parts.Any(r => r.To == p) && parts.Any(r => r.From == p + 1);
        bool Valid(Part r) => 1 <= r.From && r.From <= r.To && r.To <= n;

        // Cutting splits the part holding both pages; cutting again joins the two parts back.
        void Toggle(int p)
        {
            var both = parts.FindIndex(r => r.From <= p && p + 1 <= r.To);
            if (both >= 0)
            {
                parts.Insert(both + 1, new Part { From = p + 1, To = parts[both].To });
                parts[both].To = p;
                return;
            }
            var a = parts.FindIndex(r => r.To == p);
            var b = parts.FindIndex(r => r.From == p + 1);
            if (a < 0 || b < 0) return;
            parts[Math.Min(a, b)] = new Part { From = parts[a].From, To = parts[b].To };
            parts.RemoveAt(Math.Max(a, b));
        }

        void Rebuild()
        {
            partList.Children.Clear();
            rows.Clear();
            for (var k = 0; k < parts.Count; k++) partList.Children.Add(PartRow(k));
            Update();
        }

        // Everything that follows the ranges, without touching the boxes being typed in.
        void Update()
        {
            for (var p = 1; p < n; p++) cuts[p - 1].IsChecked = IsCut(p);
            for (var i = 0; i < n; i++)
            {
                var holders = parts.Select((r, k) => (r, k)).Where(x => Valid(x.r) && x.r.From <= i + 1 && i + 1 <= x.r.To).Select(x => x.k + 1).ToList();
                captions[i].Text = parts.Count == 1 && holders.Count == 1 ? $"{i + 1}"
                    : holders.Count == 0 ? $"{i + 1} · fuera" : $"{i + 1} · parte {string.Join(", ", holders)}";
                ((FrameworkElement)captions[i].Parent).Opacity = holders.Count == 0 ? 0.4 : 1;
            }
            for (var k = 0; k < rows.Count; k++)
            {
                var ok = Valid(parts[k]);
                rows[k].Row.BorderBrush = ok ? Brushes.Transparent : Brushes.Firebrick;
                rows[k].Count.Text = ok ? PageCount(parts[k].To - parts[k].From + 1) : "";
            }
            var bad = parts.FindIndex(r => !Valid(r));
            var whole = parts.Count == 1 && parts[0].From == 1 && parts[0].To == n;
            SaveButton.IsEnabled = bad < 0 && !whole;
            if (parts.Count == 1) Pane.Output(Converter.OutputPath(_pdf, "pdf"), Valid(parts[0]) ? $"págs. {parts[0].From}–{parts[0].To}" : null);
            else Pane.Output(Converter.OutputPath(_pdf, ""), $"{parts.Count} PDF en una carpeta");
            sideHint.Text = bad >= 0 ? $"Revisa la parte {bad + 1}: las páginas van de 1 a {n} y «desde» no puede pasar de «hasta»."
                : whole ? "Cambia el rango o corta entre dos páginas. Cada parte se guarda como un PDF."
                : parts.Count == 1 ? $"Se guardará un PDF con {PageCount(parts[0].To - parts[0].From + 1)}."
                : $"Se guardarán {parts.Count} PDF en una carpeta junto al original.";
        }

        Border PartRow(int k)
        {
            var part = parts[k];
            var count = new TextBlock { Style = (Style)FindResource("Caption"), Margin = new Thickness(0) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (parts.Count > 1)
            {
                var remove = new Button
                {
                    Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10 },
                    Style = (Style)FindResource("Action"), Padding = new Thickness(6), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Quitar esta parte",
                };
                AutomationProperties.SetName(remove, $"Quitar la parte {k + 1}");
                remove.Click += (_, _) => { parts.RemoveAt(k); Rebuild(); };
                DockPanel.SetDock(remove, Dock.Right);
                head.Children.Add(remove);
            }
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(new TextBlock { Text = $"Parte {k + 1}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

            var range = new StackPanel { Orientation = Orientation.Horizontal };
            range.Children.Add(new TextBlock { Text = "Desde", Style = (Style)FindResource("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            range.Children.Add(PageBox(part.From, v => part.From = v, () => part.From, $"Parte {k + 1}, desde la página"));
            range.Children.Add(new TextBlock { Text = "hasta", Style = (Style)FindResource("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) });
            range.Children.Add(PageBox(part.To, v => part.To = v, () => part.To, $"Parte {k + 1}, hasta la página"));

            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF6)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 12),
                Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1.5), Child = new StackPanel { Children = { head, range } },
            };
            rows.Add((row, count));
            return row;
        }

        // A page number: applied as it is typed; on leaving, shows the number that was kept.
        TextBox PageBox(int value, Action<int> set, Func<int> get, string name)
        {
            var box = new TextBox { Text = value.ToString(), Width = 52, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(box, name);
            box.TextChanged += (_, _) => { if (int.TryParse(box.Text, out var v)) { set(v); Update(); } };
            box.LostKeyboardFocus += (_, _) => box.Text = get().ToString();
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
            return box;
        }

        void Preset(IEnumerable<(int From, int To)> ranges)
        {
            parts.Clear();
            parts.AddRange(ranges.Select(r => new Part { From = r.From, To = r.To }));
            Rebuild();
        }

        Hint.Text = n < 2 ? "Este PDF tiene una sola página." : "Haz clic entre dos páginas para cortar ahí, o escribe los rangos a la derecha. Doble clic en una página para verla en grande.";
        var add = Button("Agregar parte", () =>
        {
            var last = parts[^1].To;
            parts.Add(new Part { From = Math.Min(last + 1, n), To = n });
            Rebuild();
        });
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Margin = new Thickness(0, 0, 0, 8);
        shortcuts.Children.Add(add);
        shortcuts.Children.Add(Row(Button("Cada página", () => Preset(Enumerable.Range(1, n).Select(p => (p, p)))),
            Button("Por la mitad", () => Preset(n < 2 ? new[] { (1, n) } : new[] { (1, (n + 1) / 2), ((n + 1) / 2 + 1, n) })),
            Button("Todo junto", () => Preset(new[] { (1, n) }))));

        SaveButton.Click += (_, _) => { Result = Catalog.PdfParts(parts.Select(r => (r.From, r.To)).ToList()); DialogResult = true; };
        Rebuild();
    }

    // ---- Zoom and the large view ----

    void Zoom(double box)
    {
        _box = box;
        foreach (var (stage, page) in _views)
        {
            stage.Width = stage.Height = box;
            page.MaxWidth = page.MaxHeight = box;
        }
    }

    void BuildReader()
    {
        CloseViewer.Click += (_, _) => HideViewer();
        PrevPage.Click += (_, _) => Show(_shown - 1);
        NextPage.Click += (_, _) => Show(_shown + 1);
        ZoomIn.Click += (_, _) => ZoomBy(1.25);
        ZoomOut.Click += (_, _) => ZoomBy(1 / 1.25);
        FitWidth.Click += (_, _) => SetFit(Fit.Width);
        FitPage.Click += (_, _) => SetFit(Fit.Page);
        LargeScroller.SizeChanged += (_, _) => { if (Viewer.IsVisible && _fit != Fit.None) Layout(); };
        LargeScroller.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
            e.Handled = true;
        };

        // Drag the page to move around it once it is larger than the window.
        Point grab = default, from = default;
        Large.Cursor = Cursors.Hand;
        Large.MouseLeftButtonDown += (_, e) =>
        {
            grab = e.GetPosition(LargeScroller);
            from = new Point(LargeScroller.HorizontalOffset, LargeScroller.VerticalOffset);
            Large.CaptureMouse();
            e.Handled = true;
        };
        Large.MouseMove += (_, e) =>
        {
            if (!Large.IsMouseCaptured) return;
            var d = e.GetPosition(LargeScroller) - grab;
            LargeScroller.ScrollToHorizontalOffset(from.X - d.X);
            LargeScroller.ScrollToVerticalOffset(from.Y - d.Y);
        };
        Large.MouseLeftButtonUp += (_, _) => Large.ReleaseMouseCapture();
    }

    /// <summary>Opens the reader on a page; it keeps the zoom from page to page.</summary>
    void Show(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        var opening = !Viewer.IsVisible;
        _shown = index;
        if (opening) _fit = Fit.Page;
        Viewer.Visibility = Visibility.Visible;
        PageLabel.Text = $"Página {index + 1} de {_pages.Count}";
        PrevPage.IsEnabled = index > 0;
        NextPage.IsEnabled = index < _pages.Count - 1;
        Large.LayoutTransform = new RotateTransform(_angleOf(index));
        Large.Source = Best(index)?.Image ?? _pages[index];
        // Fit needs the reader's size, known only once it is laid out.
        if (opening) Dispatcher.BeginInvoke(Layout, System.Windows.Threading.DispatcherPriority.Loaded); else Layout();
        LargeScroller.ScrollToTop();
        LargeScroller.ScrollToLeftEnd();
    }

    void HideViewer()
    {
        Viewer.Visibility = Visibility.Collapsed;
        _shown = -1;
    }

    void SetFit(Fit fit) { _fit = fit; Layout(); }

    void ZoomBy(double factor)
    {
        _fit = Fit.None;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 6);
        Layout();
    }

    /// <summary>The page's size at 96 dpi, as shown (turned pages swap sides). Thumbnails are 48 dpi.</summary>
    Size Natural(int index)
    {
        var (w, h) = (_pages[index].PixelWidth * 2.0, _pages[index].PixelHeight * 2.0);
        return _angleOf(index) % 180 == 0 ? new Size(w, h) : new Size(h, w);
    }

    void Layout()
    {
        if (_shown < 0) return;
        var page = Natural(_shown);
        double roomW = LargeScroller.ActualWidth - 48, roomH = LargeScroller.ActualHeight - 48; // the image's margin
        if (_fit == Fit.Page && roomW > 0 && roomH > 0) _zoom = Math.Min(roomW / page.Width, roomH / page.Height);
        // Leave room for the vertical scrollbar a width fit brings.
        if (_fit == Fit.Width && roomW > 0) _zoom = (roomW - SystemParameters.VerticalScrollBarWidth) / page.Width;
        var upright = _angleOf(_shown) % 180 == 0;
        Large.Width = (upright ? page.Width : page.Height) * _zoom; // before the turn
        Large.Height = (upright ? page.Height : page.Width) * _zoom;
        ZoomLabel.Text = $"{Math.Round(_zoom * 100)} %";
        _ = Sharpen(_shown);
    }

    (int Page, double Dpi, BitmapSource Image)? Best(int index) =>
        _sharp.Where(s => s.Page == index).OrderByDescending(s => s.Dpi).Select(s => ((int, double, BitmapSource)?)s).FirstOrDefault();

    /// <summary>Renders the page as sharp as the zoom and the screen need, once the zoom has settled.</summary>
    async Task Sharpen(int index)
    {
        var version = ++_renderVersion;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dpi = Math.Clamp(Math.Ceiling(96 * _zoom * pixelsPerDip / 24) * 24, 48, 600); // in steps, so small zooms reuse a render
        var have = Best(index);
        if (have is { } h && h.Dpi >= dpi) { Large.Source = h.Image; return; }
        await Task.Delay(120);
        if (version != _renderVersion) return; // still zooming or turning pages
        try
        {
            var image = (await Render(_pdf, index + 1, dpi))[0];
            _sharp.Add((index, dpi, image));
            if (_sharp.Count > 6) _sharp.RemoveAt(0); // a 300 dpi page is tens of MB
            if (_shown == index) Large.Source = image;
        }
        catch (Exception) { } // the thumbnail stays; nothing is lost
    }

    // ---- Shared pieces ----

    /// <summary>A page on white paper with its number under it; turn rotates the paper (Rotate).</summary>
    FrameworkElement PageView(int index, RotateTransform? turn, out TextBlock number)
    {
        var page = new Image { Source = _pages[index], MaxWidth = _box, MaxHeight = _box, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(page, BitmapScalingMode.HighQuality);
        var paper = new Border
        {
            Background = Brushes.White,
            Child = page,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Direction = 270, Opacity = 0.18 },
            LayoutTransform = turn ?? Transform.Identity,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var stage = new Grid { Width = _box, Height = _box, Children = { paper } };
        _views.Add((stage, page));
        number = new TextBlock { Text = (index + 1).ToString(), Style = (Style)FindResource("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        return new StackPanel { Children = { stage, number } };
    }

    static string PageCount(int n) => n == 1 ? "1 página" : $"{n} páginas";

    void AddControl(string? caption, UIElement control) => Pane.Add(caption, control);

    /// <summary>Buttons sharing a row's width equally.</summary>
    static UniformGrid Row(params Button[] items)
    {
        var row = new UniformGrid { Rows = 1 };
        for (var i = 0; i < items.Length; i++)
        {
            items[i].Margin = new Thickness(i == 0 ? 0 : 3, 0, i == items.Length - 1 ? 0 : 3, 0);
            row.Children.Add(items[i]);
        }
        return row;
    }

    Button Button(string label, Action click, string? tip = null)
    {
        var button = new Button { Content = label, ToolTip = tip, Style = (Style)FindResource("Action"), Padding = new Thickness(8, 6, 8, 6) };
        button.Click += (_, _) => click();
        return button;
    }
}
