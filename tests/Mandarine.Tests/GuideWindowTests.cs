using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandarine.Tests;

[Collection("Practice photo")]
public class GuideWindowTests
{
    static void OnSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void Opens_watching_the_first_undone_step() => OnSta(() =>
    {
        var g = new GuideWindow();
        Assert.Equal((0, false, "Mira cómo se hace, o toma la foto para probar"), (g.Shown, g.Practising, g.FooterText));
        g.Close();
    });

    [Fact]
    public void Pressing_the_photo_stops_the_demo() => OnSta(() =>
    {
        var g = new GuideWindow();
        g.BeginPractice();
        Assert.False(g.Demo.Playing);
        Assert.Equal("Ahora tú: arrastra la foto y, sin soltar, toca Shift", g.FooterText);
        g.Close();
    });

    [Fact]
    public void Done_step_celebrates_then_moves_on() => OnSta(() =>
    {
        var g = new GuideWindow();
        g.MarkDone(1);
        Assert.Equal((0, false), (g.Shown, g.Celebrating));
        g.MarkDone(0);
        Assert.True(g.Celebrating);
        g.Advance();
        Assert.Equal((2, false, "2 de 3 listo"), (g.Shown, g.Practising, g.TabText));
        g.MarkDone(2);
        g.Advance();
        Assert.Equal((2, "¡Listo! Ya sabes usar Mandarine", "Listo"), (g.Shown, g.FooterText, (string)g.NextButton.Content));
        g.Close();
    });

    [Fact]
    public void A_drop_on_the_wheel_waits_for_the_result() => OnSta(() =>
    {
        var g = new GuideWindow();
        g.BeginPractice();
        GuideWindow.Landed(g, new[] { Practice.EnsurePhoto() });
        g.DragEnded();
        Assert.False(g.Retrying);
        g.Close();

        var missed = new GuideWindow();
        missed.BeginPractice();
        missed.DragEnded();
        Assert.True(missed.Retrying);
        missed.Close();
    });

    [Fact]
    public void Next_during_the_tick_moves_on_cleanly() => OnSta(() =>
    {
        var g = new GuideWindow();
        g.MarkDone(0);
        g.Next();
        Assert.Equal((1, false), (g.Shown, g.Celebrating));
        g.Close();
    });

    [Fact]
    public void Next_walks_and_last_finishes_once() => OnSta(() =>
    {
        var g = new GuideWindow();
        var finished = 0;
        g.Finished += () => finished++;
        g.Next();
        g.Next();
        Assert.Equal((2, "Listo", 0), (g.Shown, (string)g.NextButton.Content, finished));
        g.Next();
        Assert.Equal(1, finished);
        g.Close();
    });

    [Fact]
    public void Closing_stops_the_demo() => OnSta(() =>
    {
        var g = new GuideWindow();
        g.Show();
        g.Close();
        Assert.False(g.Demo.Playing);
    });

    [Fact]
    public void Skip_and_done_finish_but_close_does_not() => OnSta(() =>
    {
        var closed = new GuideWindow();
        var finished = 0;
        closed.Finished += () => finished++;
        closed.Show();
        closed.Close();
        Assert.Equal(0, finished);

        var skipped = new GuideWindow();
        skipped.Finished += () => finished++;
        skipped.SkipButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(1, finished);
        skipped.Close();
    });

    [Fact]
    public void Apply_marks_only_practice() => OnSta(() =>
    {
        var g = new GuideWindow();
        var mine = new[] { @"C:\x\mio.png" };
        Assert.False(GuideWindow.Apply(g, mine, Catalog.TargetsFor(mine).First()));
        Assert.Equal(0, g.DoneCount);

        var photo = Practice.EnsurePhoto();
        File.Delete(photo); // "Mover" took it away
        var copy = Catalog.ActionsFor(new[] { photo }).First(f => f.Label == "Copiar");
        Assert.True(GuideWindow.Apply(g, new[] { photo }, copy));
        Assert.Equal(1, g.DoneCount);
        Assert.True(g.IsDone(2)); // an action teaches the Alt step
        Assert.True(File.Exists(photo));
        g.Close();
    });

    [Fact]
    public void Apply_ignores_closed_guide()
    {
        var photo = Path.Combine(Practice.Folder, "mandarina.jpg");
        Assert.False(GuideWindow.Apply(null, new[] { photo }, Catalog.TargetsFor(new[] { photo }).First()));
    }

    // Renders the guide with the first step done, to compare against the approved mock-up by eye.
    [Fact]
    public void Renders_guide_preview() => OnSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "mandarine-guide.png");
        var g = new GuideWindow();
        g.MarkDone(0);
        g.Advance();
        g.Show();
        g.UpdateLayout();
        g.Demo.Stop();
        g.Demo.ShowPose(DemoTimeline.KeyFrame(DemoScript.For(1)), DemoScript.For(1));
        g.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(g);
        var content = (FrameworkElement)g.Content;
        var bmp = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bmp.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path)) png.Save(f);
        g.Close();
        Assert.True(new FileInfo(path).Length > 5_000);
    });
}
