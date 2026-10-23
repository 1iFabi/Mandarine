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
        Assert.Contains("Fondo", labels);
        Assert.DoesNotContain(Catalog.ToolsFor(new[] { "clip.mp4" }), t => t.Engine == Engine.Editor);
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

    // Renders each editor so its layout can be looked at without opening it by hand.
    [Theory]
    [InlineData("crop")]
    [InlineData("redact")]
    [InlineData("annotate")]
    [InlineData("adjust")]
    [InlineData("frame")]
    public void Renders_editor_preview(string op) => OnSta(() =>
    {
        var jpg = Path.Combine(_dir, "foto.jpg");
        Ffmpeg($"-f lavfi -i testsrc=size=1200x800 -frames:v 1 \"{jpg}\"");
        var picture = ImageEdit.Load(jpg).GetAwaiter().GetResult();
        var tool = Catalog.ToolsFor(new[] { jpg }).Single(t => t.Args[0] == op);
        var editor = new EditorWindow(op, tool.Label, picture, "foto.jpg", "1 de 3")
        {
            Width = 1100, Height = 700, Left = -20000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        editor.Show();
        var frame = new DispatcherFrame();
        new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal,
            (s, _) => { ((DispatcherTimer)s!).Stop(); frame.Continue = false; }, editor.Dispatcher).Start();
        Dispatcher.PushFrame(frame);

        var content = (FrameworkElement)editor.Content;
        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(editor.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        var path = Path.Combine(Path.GetTempPath(), $"mandarine-editor-{op}.png");
        using (var f = File.Create(path)) png.Save(f);
        editor.Close();
        Assert.True(new FileInfo(path).Length > 10_000);
    });

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
