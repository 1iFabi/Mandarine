using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Mandarine;

/// <summary>The finish line: one dark row at the bottom right with an orange (or red) strip, what was made and where. Never takes focus.</summary>
public sealed class NoticeWindow : Window
{
    static readonly FontFamily Face = new("Segoe UI Variable Text, Segoe UI");
    static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7)));
    static readonly Brush Muted = Frozen(new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xA6)));
    static readonly Brush Link = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x24)));
    static readonly Brush Ok = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x00)));
    static readonly Brush Bad = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25)));

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly Border _strip = new() { Width = 4, CornerRadius = new CornerRadius(8, 0, 0, 8) };
    readonly TextBlock _glyph = new() { Foreground = Ink, Margin = new Thickness(0, 0, 8, 0) };
    // Title and folder share one trimmed line, so a long name gives way before the link does.
    readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _open = new() { Text = "Abrir carpeta", Foreground = Link, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 0), Cursor = Cursors.Hand };
    string? _reveal;

    public string ShownTitle { get; private set; } = "";
    public int TimerResets { get; private set; }

    public NoticeWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 480;
        // Off screen until Place knows its size, so the first Show never flashes at Windows' default spot.
        Left = SystemParameters.VirtualScreenLeft - 10000;
        Top = SystemParameters.VirtualScreenTop - 10000;
        FontFamily = Face;
        FontSize = 13;

        var row = new Grid { Margin = new Thickness(14, 10, 14, 10), VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_text, 1);
        Grid.SetColumn(_open, 2);
        row.Children.Add(_glyph);
        row.Children.Add(_text);
        row.Children.Add(_open);
        var body = new DockPanel();
        DockPanel.SetDock(_strip, Dock.Left);
        body.Children.Add(_strip);
        body.Children.Add(row);
        // Margin leaves room for the shadow inside the transparent window.
        Content = new Border
        {
            Background = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E))), CornerRadius = new CornerRadius(8), Margin = new Thickness(12),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            Child = body,
        };

        _timer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();
        ((UIElement)Content).MouseLeftButtonUp += (_, _) => Dismiss();
        _open.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_reveal != null)
                Process.Start("explorer.exe", Directory.Exists(_reveal) ? $"\"{_reveal}\"" : $"/select,\"{_reveal}\"");
            Dismiss();
        };
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Shows a notice, replacing any one on screen and starting its 5 s over.</summary>
    public void Present(Notice n)
    {
        _strip.Background = n.IsError ? Bad : Ok;
        _glyph.Text = n.IsError ? "✗" : "✓";
        ShownTitle = n.Title;
        _text.Inlines.Clear();
        _text.Inlines.Add(new System.Windows.Documents.Run(n.Title) { Foreground = Ink, FontWeight = FontWeights.SemiBold });
        _text.Inlines.Add(new System.Windows.Documents.Run("  " + n.Detail) { Foreground = Muted });
        _reveal = n.Reveal;
        _open.Visibility = n.Reveal != null ? Visibility.Visible : Visibility.Collapsed;
        Show();
        Place();
        _timer.Stop();
        _timer.Start();
        TimerResets++;
    }

    void Dismiss()
    {
        _timer.Stop();
        Hide();
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4; // the shadow margin already spaces it from the edge
        Top = area.Bottom - ActualHeight - 4;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never steal focus from the app the user is working in.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
}
