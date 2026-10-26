using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mandarine;

/// <summary>
/// "Cómo se usa": one wheel at a time. A demo plays the gesture until the person takes the photo; a real gesture
/// on the photo ticks the step, and the guide moves on to the next one.
/// </summary>
public partial class GuideWindow : Window
{
    const int Steps = 3;

    readonly bool[] _done = new bool[Steps];
    readonly Border[] _keys = new Border[Steps];
    readonly Border[] _pips = new Border[Steps];
    readonly SpringClock _clock = new() { ReduceMotion = !SystemParameters.ClientAreaAnimation };
    readonly Spring _title, _check;
    // After the ✓ has been seen; and after a try that did not land, before the demo plays again.
    readonly DispatcherTimer _celebrate = new() { Interval = TimeSpan.FromSeconds(0.8) };
    readonly DispatcherTimer _retry = new() { Interval = TimeSpan.FromSeconds(1.5) };

    /// <summary>Listo or Saltar: the guide was seen. Closing with the X does not count.</summary>
    public event Action? Finished;

    public int DoneCount => _done.Count(d => d);
    public int Current => Array.IndexOf(_done, false) is var i and >= 0 ? i : Steps;
    public string TabText => $"{DoneCount} de {Steps} listo";
    public int Shown { get; private set; }
    public bool Practising { get; private set; }
    public bool Celebrating { get; private set; }
    public string FooterText => Footer.Text;
    public bool Retrying => _retry.IsEnabled;
    bool _landed; // the drop reached the wheel: wait for the result instead of replaying the demo

    public GuideWindow()
    {
        InitializeComponent();
        Logo.Source = WheelWindow.Logo;
        for (var i = 0; i < Steps; i++)
        {
            _pips[i] = new Border { Width = 28, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(6, 0, 0, 0) };
            Pips.Children.Add(_pips[i]);
            _keys[i] = new Border
            {
                MinWidth = 64, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(7),
                Background = Brushes.White, BorderBrush = (Brush)FindResource("Control"), BorderThickness = new Thickness(1, 1, 1, 4),
                Child = new TextBlock { Text = DemoScript.For(i).Key, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
            };
            Keys.Children.Add(_keys[i]);
        }
        _title = _clock.Add(new Spring(1) { Response = 0.3 }, v => { StepTitle.Opacity = Math.Clamp(v, 0, 1); TitleMove.Y = 12 * (1 - v); });
        _check = _clock.Add(new Spring(0) { DampingRatio = 0.6, Response = 0.35 }, v => { Check.Opacity = Math.Clamp(v, 0, 1); CheckScale.ScaleX = CheckScale.ScaleY = 0.4 + 0.6 * v; });

        SkipButton.Click += (_, _) => Finished?.Invoke();
        NextButton.Click += (_, _) => Next();
        _celebrate.Tick += (_, _) => Advance();
        _retry.Tick += (_, _) => { _retry.Stop(); Practising = false; ShowStep(Shown); };
        Closed += (_, _) => { Demo.Stop(); _celebrate.Stop(); _retry.Stop(); };

        var photo = Practice.EnsurePhoto();
        // Loaded into memory: a thumbnail holding the file open would make "Mover" fail on it.
        var thumb = new BitmapImage();
        thumb.BeginInit();
        thumb.UriSource = new Uri(photo);
        thumb.DecodePixelWidth = 192;
        thumb.CacheOption = BitmapCacheOption.OnLoad;
        thumb.EndInit();
        Thumb.Source = thumb;
        Demo.Attach(Sample, _keys, thumb);

        Sample.PreviewMouseLeftButtonDown += (_, _) => BeginPractice();
        Sample.MouseLeftButtonUp += (_, _) => DragEnded(); // a click without a drag
        // A real file drag: the wheel and the notice treat the sample like any file.
        Sample.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !Practising) return;
            DragDrop.DoDragDrop(Sample, new DataObject(DataFormats.FileDrop, new[] { Practice.EnsurePhoto() }), DragDropEffects.Copy | DragDropEffects.Move);
            DragEnded();
        };
        ShowStep(Current < Steps ? Current : Steps - 1);
    }

    public bool IsDone(int step) => _done[step];

    /// <summary>A job on the practice photo finished well: tick its wheel's step and put the photo back if it moved.</summary>
    public static bool Apply(GuideWindow? guide, string[] files, Format format)
    {
        if (guide == null || !Practice.Involves(files)) return false;
        guide.MarkDone(Practice.StepFor(format));
        Practice.EnsurePhoto();
        return true;
    }

    public void MarkDone(int step)
    {
        if (_done[step]) return;
        _done[step] = true;
        Paint();
        if (step != Shown) return;
        Celebrating = true;
        _retry.Stop();
        Demo.Stop();
        _check.Target = 1;
        _clock.Kick();
        _celebrate.Start();
    }

    /// <summary>The person has the photo: the demo gets out of the way.</summary>
    public void BeginPractice()
    {
        if (Celebrating) return;
        _retry.Stop();
        _landed = false;
        Demo.Stop();
        Practising = true;
        Footer.Text = $"Ahora tú: arrastra la foto y, sin soltar, toca {DemoScript.For(Shown).Key}";
    }

    /// <summary>After the ✓: on to the first step not yet done, or the end.</summary>
    public void Advance()
    {
        _celebrate.Stop();
        Celebrating = false;
        _check.Target = 0;
        _clock.Kick();
        ShowStep(Current < Steps ? Current : Shown);
    }

    public void Next()
    {
        // Leaving during the ✓: the pending advance must not replay this step on top of the next.
        _celebrate.Stop();
        Celebrating = false;
        _check.Target = 0;
        _check.Snap();
        _clock.Kick();
        if (Shown == Steps - 1) { Finished?.Invoke(); return; }
        ShowStep(Shown + 1);
    }

    /// <summary>The wheel took the practice photo: a slow job (Sin fondo, a folder picker) must not look like a miss.</summary>
    public static void Landed(GuideWindow? guide, string[] files)
    {
        if (guide == null || !Practice.Involves(files)) return;
        guide._landed = true;
        guide._retry.Stop();
    }

    /// <summary>The drag is over: if nothing landed, show the demo again in a moment.</summary>
    public void DragEnded()
    {
        if (Practising && !Celebrating && !_landed) _retry.Start();
    }

    void ShowStep(int step)
    {
        var moved = step != Shown;
        Shown = step;
        Practising = false;
        var s = DemoScript.For(step);
        StepName.Text = $"{s.Name} · {s.Key}";
        StepWhat.Text = s.What;
        Paint();
        if (moved)
        {
            _title.Value = 0;
            _title.Target = 1;
            _clock.Kick();
        }
        if (DoneCount == Steps)
        {
            Demo.Stop();
            Footer.Text = "¡Listo! Ya sabes usar Mandarine";
            return;
        }
        Footer.Text = "Mira cómo se hace, o toma la foto para probar";
        Demo.Play(step);
    }

    void Paint()
    {
        var accent = (Brush)FindResource("Accent");
        for (var i = 0; i < Steps; i++)
        {
            _pips[i].Background = i <= Shown || _done[i] ? accent : (Brush)FindResource("Control");
            ((TextBlock)_keys[i].Child).Foreground = (Brush)FindResource(i == Shown ? "Ink" : "Muted");
        }
        NextButton.Content = Shown == Steps - 1 ? "Listo" : "Siguiente";
    }
}
