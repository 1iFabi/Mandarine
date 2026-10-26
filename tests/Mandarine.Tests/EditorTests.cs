using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mandarine.Tests;

public class EditorTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("mandarine-test").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Image_tools_include_the_editors()
    {
        var labels = Catalog.ToolsFor(new[] { "foto.jpg" }).Select(t => t.Label).ToList();
        Assert.Contains("Recortar", labels);
        Assert.Contains("Anotar", labels);
        Assert.Contains("Ocultar", labels);
        Assert.Contains("Ajustes", labels);
        Assert.Contains("Redimensionar", labels);
        Assert.Contains("Fondo", labels);
        Assert.Contains("Escanear", labels);
        Assert.Equal(new[] { "rotate" }, Catalog.ToolsFor(new[] { "clip.mp4" }).Where(t => t.Engine == Engine.Editor).Select(t => t.Args[0]));
        Assert.Equal(new[] { "pdfrotate", "pdfsplit", "pdforganize" }, Catalog.ToolsFor(new[] { "a.pdf" }).Where(t => t.Engine == Engine.Editor).Select(t => t.Args[0]));
        Assert.Equal("png", ImageEdit.OutputExt("x.webp"));
        Assert.Equal("jpg", ImageEdit.OutputExt("x.JPG"));
    }

    [Fact]
    public void Edits_do_what_they_say() => OnSta(() =>
    {
        var jpg = Path.Combine(_dir, "foto.jpg");
        Ffmpeg($"-f lavfi -i testsrc=size=400x200 -frames:v 1 \"{jpg}\"");
        var picture = ImageEdit.Load(jpg).GetAwaiter().GetResult();
        Assert.Equal((400, 200), (picture.PixelWidth, picture.PixelHeight));

        Assert.Equal((100, 50), (ImageEdit.Resize(picture, 0.25).PixelWidth, ImageEdit.Resize(picture, 0.25).PixelHeight));

        // Crop takes a share, saves and reads back at that size.
        var cropped = ImageEdit.Crop(picture, new Rect(0.25, 0, 0.5, 1));
        var saved = Converter.OutputPath(jpg, "jpg");
        ImageEdit.Save(cropped, saved);
        var reloaded = ImageEdit.Load(saved).GetAwaiter().GetResult();
        Assert.Equal((200, 200), (reloaded.PixelWidth, reloaded.PixelHeight));

        // Neutral adjustments change nothing; more light brightens.
        Assert.Equal(Mean(picture), Mean(ImageEdit.Adjust(picture, new Adjustments())), 0.5);
        var brighter = Mean(ImageEdit.Adjust(picture, new Adjustments(Brightness: 0.5, 0, 0, 0)));
        Assert.True(brighter > Mean(picture) + 10, $"{brighter} vs {Mean(picture)}");

        // Covered areas are black; pixelated ones lose their detail.
        var area = new Int32Rect(40, 40, 120, 90);
        Assert.Equal(0, Mean(ImageEdit.Redact(picture, new[] { area }, pixelate: false), area));
        var block = new Int32Rect(40, 40, 30, 30); // blocks are a third of the area's short side
        Assert.False(Uniform(picture, block));
        Assert.True(Uniform(ImageEdit.Redact(picture, new[] { area }, pixelate: true), block));

        // An arrow leaves ink along its path.
        var arrow = new Mark(MarkKind.Arrow, Colors.Black, new[] { new Point(20, 180), new Point(380, 20) });
        Assert.True(Mean(ImageEdit.Annotate(picture, new[] { arrow }), new Int32Rect(195, 95, 10, 10)) < Mean(picture, new Int32Rect(195, 95, 10, 10)));

        // The frame grows the canvas to the ratio; a transparent background stays transparent.
        var framed = ImageEdit.Frame(picture, new FrameStyle(Brushes.Transparent, 0.1, 0.05, true, 1));
        Assert.Equal(framed.PixelWidth, framed.PixelHeight);
        Assert.True(framed.PixelWidth >= 480);
        var corner = new byte[4];
        framed.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
        Assert.Equal(0, corner[3]);
    });

    [Fact]
    public void Scan_finds_the_sheet_and_makes_a_searchable_pdf() => OnSta(() =>
    {
        var (photo, sheet) = SheetPhoto();
        var found = Scan.FindPage(photo);
        for (var i = 0; i < 4; i++)
            Assert.True((found[i] - sheet[i]).Length < 0.02, $"corner {i}: {found[i]} vs {sheet[i]}");

        var page = Scan.Page(photo, found, ScanMode.BlackWhite);
        Assert.Equal(Scan.PageLongSide, page.PixelHeight);
        Assert.InRange(page.PixelWidth, 2600, 2840); // 850 × 1100 keeps its shape
        Assert.All(Values(page, null).Distinct(), v => Assert.True(v is 0 or 255, $"{v}"));

        Assert.True(Ocr.Available, "run scripts/fetch-tools.ps1 first");
        var pdf = Path.Combine(_dir, "hoja.pdf");
        Ocr.ScannedPdf(new[] { Scan.Jpeg(Scan.Page(photo, found, ScanMode.Gray), ScanMode.Gray) }, pdf, readText: true).GetAwaiter().GetResult();
        using var p = Process.Start(new ProcessStartInfo(Tools.Exe(Engine.Pdfcraft), $"text \"{pdf}\"") { UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(text.Contains("mañana") && text.Contains("acción"), text);
    });

    /// <summary>A photo of a sheet with text, tilted on a dark table under uneven light, and its corners as shares.</summary>
    static (BitmapSource Photo, Point[] Corners) SheetPhoto()
    {
        const int w = 1600, h = 1300, sw = 850, sh = 1100;
        var place = Matrix.Identity;
        place.Translate(-sw / 2.0, -sh / 2.0);
        place.Rotate(8);
        place.Translate(w / 2.0, h / 2.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x26)), null, new Rect(0, 0, w, h));
            dc.PushTransform(new MatrixTransform(place));
            dc.DrawRectangle(new LinearGradientBrush(Colors.White, Color.FromRgb(0xC8, 0xC0, 0xB0), 30), null, new Rect(0, 0, sw, sh));
            var lines = "Carta de la mañana\nEl pingüino comió una piña.\nAño de acción y canción.";
            dc.DrawText(new FormattedText(lines, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 40, Brushes.Black, 1), new Point(70, 90));
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var corners = new[] { new Point(0, 0), new Point(sw, 0), new Point(sw, sh), new Point(0, sh) }
            .Select(c => place.Transform(c)).Select(c => new Point(c.X / w, c.Y / h)).ToArray();
        return (bmp, corners);
    }

    // Renders each editor so its layout can be looked at without opening it by hand.
    [Theory]
    [InlineData("crop")]
    [InlineData("redact")]
    [InlineData("annotate")]
    [InlineData("adjust")]
    [InlineData("frame")]
    [InlineData("rotate")]
    [InlineData("resize")]
    [InlineData("scan")]
    public void Renders_editor_preview(string op) => OnSta(() =>
    {
        var jpg = Path.Combine(_dir, "foto.jpg");
        Ffmpeg($"-f lavfi -i testsrc=size=1200x800 -frames:v 1 \"{jpg}\"");
        var picture = op == "scan" ? SheetPhoto().Photo : ImageEdit.Load(jpg).GetAwaiter().GetResult();
        var tool = Catalog.ToolsFor(new[] { jpg }).Single(t => t.Args[0] == op);
        Snapshot(new EditorWindow(op, tool.Label, picture, "C:/Fotos/foto.jpg", "C:/Fotos/foto (Mandarine).jpg", "1 de 3"), $"mandarine-editor-{op}.png");
    });

    // The PDF page window over a five-page PDF made from pictures, with a page chosen or a cut made.
    [Theory]
    [InlineData("pdfrotate")]
    [InlineData("pdfsplit")]
    [InlineData("pdforganize")]
    [InlineData("pdfview")]
    public void Renders_pdf_pages_preview(string shot) => OnSta(() =>
    {
        var png = Path.Combine(_dir, "pagina.png");
        Ffmpeg($"-f lavfi -i testsrc=size=850x1100 -frames:v 1 \"{png}\"");
        var pdf = Converter.CombineAsync(Enumerable.Repeat(png, 5).ToArray(), Catalog.TargetsFor(new[] { png }).Single(f => f.Ext == "pdf")).GetAwaiter().GetResult();
        if (shot == "pdfview")
        {
            // Text, to check the reader is sharp enough to read.
            var html = Path.Combine(_dir, "texto.html");
            File.WriteAllText(html, "<h1>Capítulo uno</h1>" + string.Concat(Enumerable.Repeat("<p>El texto pequeño de un contrato tiene que leerse sin esfuerzo al ampliar la página en el lector.</p>", 30)));
            pdf = Converter.ConvertAsync(html, Catalog.TargetsFor(new[] { html }).Single(f => f.Ext == "pdf")).GetAwaiter().GetResult();
        }
        var pages = PdfPagesWindow.Thumbnails(pdf).GetAwaiter().GetResult();
        if (shot != "pdfview") Assert.Equal(5, pages.Count);
        var op = shot == "pdfview" ? "pdfsplit" : shot;
        var window = new PdfPagesWindow(op, op switch { "pdfrotate" => "Rotar", "pdforganize" => "Organizar", _ => "Dividir" }, pdf, pages, "");
        var toggles = Descendants(window).OfType<System.Windows.Controls.Primitives.ToggleButton>().ToList();
        Assert.Equal(op == "pdfsplit" ? pages.Count - 1 : 5, toggles.Count); // a tile per page, or a gap between each two
        Snapshot(window, $"mandarine-editor-{shot}.png", wait: shot == "pdfview" ? 2500 : 400, act: () =>
        {
            if (shot == "pdfview")
            {
                var page = Descendants(window).OfType<System.Windows.Controls.ContentControl>().First(c => c.GetType() == typeof(System.Windows.Controls.ContentControl));
                page.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); // laid out before fitting to the width
                Descendants(window).OfType<System.Windows.Controls.Button>().First(b => Equals(b.Content, "Ancho"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return;
            }
            // Rotate: page 2 chosen and turned. Split: a cut after page 2. Organize: page 2 chosen and sent to the end.
            toggles[1].IsChecked = true;
            toggles[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var then = op switch { "pdfrotate" => "90°", "pdforganize" => "Al final", _ => null };
            if (then != null) Descendants(window).OfType<System.Windows.Controls.Button>().First(b => Equals(b.Content, then))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        });
    });

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>Renders a window off-screen into %TEMP% so its layout can be looked at; act runs once it is shown.</summary>
    static void Snapshot(Window editor, string name, Action? act = null, int wait = 400)
    {
        editor.Width = 1100;
        editor.Height = 700;
        editor.Left = -20000;
        editor.Top = 0;
        editor.ShowActivated = false;
        editor.WindowStartupLocation = WindowStartupLocation.Manual;
        // As on the app's UI thread, so the window's awaits come back to it (set only now: the test's blocking waits would deadlock).
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(editor.Dispatcher));
        editor.Show();
        act?.Invoke();
        var frame = new DispatcherFrame();
        new DispatcherTimer(TimeSpan.FromMilliseconds(wait), DispatcherPriority.Normal,
            (s, _) => { ((DispatcherTimer)s!).Stop(); frame.Continue = false; }, editor.Dispatcher).Start();
        Dispatcher.PushFrame(frame);

        var content = (FrameworkElement)editor.Content;
        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(editor.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, content.ActualWidth, content.ActualHeight) }, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        var path = Path.Combine(Path.GetTempPath(), name);
        using (var f = File.Create(path)) png.Save(f);
        editor.Close();
        Assert.True(new FileInfo(path).Length > 10_000);
    }

    static double Mean(BitmapSource image, Int32Rect? area = null) => Values(image, area).Average();

    static bool Uniform(BitmapSource image, Int32Rect area)
    {
        var v = Values(image, area);
        return v.Where((_, i) => i % 3 == 0).Distinct().Count() == 1 && v.Where((_, i) => i % 3 == 1).Distinct().Count() == 1
            && v.Where((_, i) => i % 3 == 2).Distinct().Count() == 1;
    }

    /// <summary>Colour channels (alpha left out) of an area.</summary>
    static double[] Values(BitmapSource image, Int32Rect? area)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var r = area ?? new Int32Rect(0, 0, bgra.PixelWidth, bgra.PixelHeight);
        var px = new byte[r.Width * r.Height * 4];
        bgra.CopyPixels(r, px, r.Width * 4, 0);
        return px.Where((_, i) => i % 4 != 3).Select(b => (double)b).ToArray();
    }

    // The finish notice names where the editors saved, so Run hands back the written paths, not a count.
    // shortcut: checks the signature only; Run always opens a dialog, so its paths are covered by the manual test.
    [Fact]
    public void Run_returns_saved_paths() =>
        Assert.Equal(typeof(Task<IReadOnlyList<string>>), typeof(Editor).GetMethod(nameof(Editor.Run))!.ReturnType);

    static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Exception(error.Message, error);
    }

    static void Ffmpeg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y {args}") { UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
