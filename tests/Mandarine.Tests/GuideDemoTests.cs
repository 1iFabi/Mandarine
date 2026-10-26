using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandarine.Tests;

public class GuideDemoTests
{
    [Fact]
    public void Script_comes_from_the_catalog()
    {
        var s = DemoScript.For(0);
        Assert.Equal(("Shift", "Convertir · Shift"), (s.Key, $"{s.Name} · {s.Key}"));
        Assert.Equal(Catalog.TargetsFor(new[] { "mandarina.jpg" }).Select(f => f.Label).Take(8), s.Options);
        Assert.Equal(("PNG", "mandarina.png"), (s.Options[s.Pick], s.Output));
        Assert.Equal("Sin fondo", DemoScript.For(1).Options[DemoScript.For(1).Pick]);
        var copy = DemoScript.For(2);
        Assert.Equal(("Copiar", (string?)null, "Copiado: pégalo donde quieras"), (copy.Options[copy.Pick], copy.Output, copy.Notice));
        Assert.All(new[] { 0, 1, 2 }, i => Assert.InRange(DemoScript.For(i).Options.Count, 1, 8));
    }

    [Fact]
    public void Steps_are_built_once() => Assert.Same(DemoScript.For(1), DemoScript.For(1));

    [Fact]
    public void Missing_choice_promises_no_result()
    {
        var s = DemoScript.Make("Ctrl", "Herramientas", "x", new[] { "Recortar", "Rotar" }, "Sin fondo", "mandarina-sin-fondo.png");
        Assert.Equal(("Recortar", (string?)null), (s.Options[s.Pick], s.Output));
        Assert.DoesNotContain("sin-fondo", s.Notice);
    }

    [Fact]
    public void Missing_choice_falls_back_to_the_first() => Assert.Equal(0, DemoScript.IndexOr0(new[] { "A", "B" }, "Sin fondo"));

    [Fact]
    public void Timeline_tells_the_gesture_in_order()
    {
        var s = DemoScript.For(0);
        Assert.Equal(DemoTimeline.Rest, DemoTimeline.At(0.2, s).Cursor);
        Assert.True(DemoTimeline.At(1.3, s).Pressed);
        Assert.True(DemoTimeline.At(2.6, s).KeyLit);
        var pointing = DemoTimeline.At(3.8, s);
        Assert.True(pointing is { Dragging: true, WheelOpen: true, Hot: true, KeyLit: false });
        Assert.Equal(DemoTimeline.PickPoint(s), pointing.Cursor);
        var after = DemoTimeline.At(4.6, s);
        Assert.True(after is { Result: true, WheelOpen: false, Dragging: false, Pressed: false });
        Assert.Equal(DemoTimeline.At(1.0, s), DemoTimeline.At(1.0 + DemoTimeline.Loop, s));
        Assert.True(DemoTimeline.KeyFrame(s) is { WheelOpen: true, Hot: true, KeyLit: true, Dragging: true });
    }

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
    public void Stop_leaves_nothing_moving() => OnSta(() =>
    {
        var demo = new GuideDemo { Width = 860, Height = 480 };
        var photo = new Border();
        var keys = new[] { new Border(), new Border(), new Border() };
        demo.Attach(photo, keys);
        demo.Play(0);
        demo.ShowPose(DemoTimeline.At(1.8, DemoScript.For(0)), DemoScript.For(0));
        Assert.Equal(0.35, photo.Opacity, 2);
        demo.Stop();
        Assert.False(demo.Playing);
        Assert.Equal(1, photo.Opacity);
    });

    // Renders the still the demo shows with Windows animations off, to compare with the mock-up by eye.
    [Fact]
    public void Renders_demo_keyframe() => OnSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "mandarine-guide-demo.png");
        var host = new Grid { Width = 860, Height = 480, Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)) };
        var demo = new GuideDemo();
        host.Children.Add(demo);
        host.Measure(new Size(860, 480));
        host.Arrange(new Rect(0, 0, 860, 480));
        var step = DemoScript.For(1);
        demo.ShowPose(DemoTimeline.KeyFrame(step), step);
        host.UpdateLayout();
        var bmp = new RenderTargetBitmap(860, 480, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path)) png.Save(f);
        Assert.True(new FileInfo(path).Length > 5_000);
    });
}
