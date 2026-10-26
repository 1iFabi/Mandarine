using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Mandarine;

/// <summary>The "Cómo se usa" practice: a sample photo in a temp folder, which wheel step an entry teaches, and whether the guide was seen.</summary>
public static class Practice
{
    public static string Folder => Path.Combine(Path.GetTempPath(), "Mandarine", "Práctica");

    static string Photo => Path.Combine(Folder, "mandarina.jpg");

    /// <summary>The sample photo, drawn again whenever practice moved or deleted it. Needs an STA thread.</summary>
    public static string EnsurePhoto()
    {
        if (File.Exists(Photo)) return Photo;
        Directory.CreateDirectory(Folder);
        const int w = 1200, h = 800;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xFF, 0xB0, 0x67), Color.FromRgb(0xFF, 0x52, 0x00), 45), null, new Rect(0, 0, w, h));
            if (WheelWindow.LoadLogo() is { } logo) dc.DrawImage(logo, new Rect(w / 2 - 220, h / 2 - 220, 440, 440));
            else
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A)), null, new Point(w / 2, h / 2 + 20), 220, 210);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x3E, 0x8E, 0x3A)), null, new Point(w / 2 + 60, h / 2 - 200), 70, 30);
            }
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        ImageEdit.Save(bmp, Photo);
        return Photo;
    }

    public static bool Involves(IEnumerable<string> files) => files.Any(f => string.Equals(Path.GetFullPath(f), Photo, StringComparison.OrdinalIgnoreCase));

    /// <summary>0 = Convertir (Shift), 1 = Herramientas (Ctrl), 2 = Acciones (Alt).</summary>
    public static int StepFor(Format f) => f.Engine == Engine.Action ? 2 : f.Icon != null ? 1 : 0;

    public static bool IsSeen(RegistryKey key) => key.GetValue("GuideShown") is 1;

    public static void MarkSeen(RegistryKey key) => key.SetValue("GuideShown", 1, RegistryValueKind.DWord);
}
