using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mandarine.Tests;

public class NoticeWindowTests
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
    public void Present_shows_title_and_hides_after_click() => OnSta(() =>
    {
        var w = new NoticeWindow();
        w.Present(Notice.Done(new[] { @"C:\x\V\a.mkv" }));
        Assert.True(w.IsVisible);
        Assert.Equal("a.mkv", w.ShownTitle);
        ((UIElement)w.Content).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Assert.False(w.IsVisible);
        w.Close();
    });

    [Fact]
    public void Second_notice_replaces_and_restarts() => OnSta(() =>
    {
        var w = new NoticeWindow();
        w.Present(Notice.Done(new[] { @"C:\x\V\a.mkv" }));
        w.Present(Notice.Failed(2, 5, "x", null));
        Assert.True(w.IsVisible);
        Assert.Equal("Fallaron 2 de 5", w.ShownTitle);
        Assert.Equal(2, w.TimerResets);
        w.Close();
    });

    // Long names in a long folder: the row stays narrow and "Abrir carpeta" stays whole inside the dark row.
    [Fact]
    public void Long_name_keeps_the_link_visible() => OnSta(() =>
    {
        var w = new NoticeWindow();
        w.Present(Notice.Done(new[] { @"C:\x\" + new string('d', 60) + @"\" + new string('n', 90) + ".pdf" }));
        w.UpdateLayout();
        var row = (FrameworkElement)w.Content;
        var link = Find(row, "Abrir carpeta");
        var right = link.TranslatePoint(new Point(link.ActualWidth, 0), row).X;
        Assert.True(w.ActualWidth <= 480, $"{w.ActualWidth}");
        Assert.True(right <= row.ActualWidth, $"link ends at {right}, row is {row.ActualWidth}");
        w.Close();
    });

    // Shown before it knows its size: it must not flash at Windows' default spot first.
    [Fact]
    public void Starts_off_screen_until_placed() => OnSta(() =>
    {
        var w = new NoticeWindow();
        Assert.True(w.Left + 480 < SystemParameters.VirtualScreenLeft, $"{w.Left}");
        w.Close();
    });

    static TextBlock Find(DependencyObject root, string text)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock t && t.Text == text) return t;
            try { return Find(child, text); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"no '{text}'");
    }
}
