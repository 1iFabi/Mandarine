using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mandarine.Tests;

public class WheelPreviewTests
{
    // Renders the real wheel over a dark and a light synthetic desktop and saves PNGs, so the glass
    // and its text contrast can be checked without performing a drag.
    [Theory]
    [InlineData("light", 236, false)]
    [InlineData("hot", 236, true)]
    [InlineData("dark", 28, true)]
    [InlineData("tools", 120, true)]
    [InlineData("actions", 120, true)]
    [InlineData("empty", 236, false)]
    public void Renders_wheel_preview(string name, int baseTone, bool pointing)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mandarine-wheel-{name}.png");
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var tools = name == "tools";
                var actions = name == "actions";
                var wheel = new WheelWindow
                {
                    AutoHide = false, Mode = tools ? WheelMode.Tools : actions ? WheelMode.Actions : WheelMode.Convert,
                    Theme = name == "dark" ? WheelTheme.Dark : WheelTheme.Light,
                };
                var dpi = VisualTreeHelper.GetDpi(wheel);
                var side = (int)Math.Round(wheel.Width * dpi.DpiScaleX);
                wheel.ShowAt(FakeDesktop(side, baseTone), 100, 100);
                var files = tools || actions ? new[] { "photo.jpg" } : name == "empty" ? new[] { "notes.xyz" } : new[] { "clip.mp4" };
                wheel.ShowTargets(files);
                // Point at the second segment (clockwise from the top) to render the hot state.
                var n = (tools ? Catalog.ToolsFor(files) : actions ? Catalog.ActionsFor(files) : Catalog.TargetsFor(files)).Count;
                var a = -Math.PI / 2 + 2 * Math.PI / n;
                if (pointing) wheel.PointAt(new Point(105 + 66 * Math.Cos(a), 105 + 66 * Math.Sin(a)));
                // Let the entrance animation finish before snapshotting.
                var frame = new DispatcherFrame();
                new DispatcherTimer(TimeSpan.FromMilliseconds(800), DispatcherPriority.Normal,
                    (s, _) => { ((DispatcherTimer)s!).Stop(); frame.Continue = false; }, wheel.Dispatcher).Start();
                Dispatcher.PushFrame(frame);

                // Composite over the same fake desktop so the picture shows what the user sees.
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(ToBitmap(FakeDesktopBytes(side, baseTone), side, dpi.DpiScaleX), new Rect(0, 0, wheel.Width, wheel.Height));
                    dc.DrawRectangle(new VisualBrush((Visual)wheel.Content), null, new Rect(0, 0, wheel.Width, wheel.Height));
                }
                var bmp = new RenderTargetBitmap(side, side, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(visual);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using (var f = File.Create(path)) png.Save(f);
                wheel.HideWheel();
                wheel.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.True(new FileInfo(path).Length > 2_000);
    }

    // A key with the button held is not always a file drag: the wheel stays unseen until files arrive.
    [Fact]
    public void Wheel_waits_unseen_until_the_drag_brings_files()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var wheel = new WheelWindow { Mode = WheelMode.Convert };
                var side = (int)Math.Round(wheel.Width * VisualTreeHelper.GetDpi(wheel).DpiScaleX);
                wheel.ShowAt(FakeDesktop(side, 236), 100, 100);
                Assert.True(wheel.IsWaitingForFiles);
                wheel.ShowTargets(new[] { "clip.mp4" });
                Assert.False(wheel.IsWaitingForFiles);
                wheel.HideWheel();

                // No files ever came (selecting text with Shift): hiding is immediate, no animation left running.
                wheel.ShowAt(FakeDesktop(side, 236), 100, 100);
                wheel.HideWheel();
                Assert.False(wheel.IsVisible);
                Assert.False(wheel.IsWaitingForFiles);
                wheel.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    // A desktop-like backdrop: base tone with lines of "text" and a coloured window.
    static byte[] FakeDesktopBytes(int side, int tone)
    {
        var px = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            var i = (y * side + x) * 4;
            var text = y % 18 < 3 && (x / 7 + y / 18) % 5 != 0;
            var window = x > side * 0.55 && y > side * 0.15 && y < side * 0.6;
            var (b, g, r) = window ? (200, 120, 40) : text ? (tone < 128 ? 210 : 40, tone < 128 ? 210 : 40, tone < 128 ? 210 : 40) : (tone, tone, tone);
            px[i] = (byte)b; px[i + 1] = (byte)g; px[i + 2] = (byte)r; px[i + 3] = 255;
        }
        return px;
    }

    static Backdrop FakeDesktop(int side, int tone) => Backdrop.FromBgra(side, side, FakeDesktopBytes(side, tone));

    static BitmapSource ToBitmap(byte[] px, int side, double scale) =>
        BitmapSource.Create(side, side, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, px, side * 4);
}
