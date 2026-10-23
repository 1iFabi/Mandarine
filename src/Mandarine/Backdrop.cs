using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandarine;

/// <summary>
/// What is behind the wheel. WPF cannot read what lies under its own window, so this is a screen
/// capture taken just before the wheel appears; blurred, it becomes the frosted glass.
/// </summary>
public sealed class Backdrop
{
    public readonly int Width, Height;
    readonly byte[] _bgra;

    Backdrop(int w, int h, byte[] bgra) { Width = w; Height = h; _bgra = bgra; }

    public static Backdrop FromBgra(int w, int h, byte[] bgra) => new(w, h, bgra);

    /// <summary>Captures a screen rectangle in physical pixels. Falls back to neutral grey.</summary>
    public static Backdrop Capture(int x, int y, int w, int h)
    {
        try
        {
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes = new byte[w * h * 4];
            for (var row = 0; row < h; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * w * 4, w * 4);
            bmp.UnlockBits(data);
            return new Backdrop(w, h, bytes);
        }
        catch (Exception)
        {
            var bytes = new byte[w * h * 4];
            Array.Fill(bytes, (byte)90);
            return new Backdrop(w, h, bytes);
        }
    }

    /// <summary>Three box-blur passes approximate a gaussian (same trick browsers use).</summary>
    public Backdrop Blurred(int radius)
    {
        if (radius < 1) return this;
        var a = (byte[])_bgra.Clone();
        var b = new byte[a.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlur(a, b, Width, Height, radius, horizontal: true);
            BoxBlur(b, a, Width, Height, radius, horizontal: false);
        }
        return new Backdrop(Width, Height, a);
    }

    static void BoxBlur(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        int step = horizontal ? 4 : w * 4;
        var window = 2 * r + 1;
        for (var line = 0; line < lines; line++)
        {
            var start = horizontal ? line * w * 4 : line * 4;
            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var i = -r; i <= r; i++) sum += src[start + Math.Clamp(i, 0, len - 1) * step + c];
                for (var i = 0; i < len; i++)
                {
                    dst[start + i * step + c] = (byte)(sum / window);
                    sum += src[start + Math.Min(i + r + 1, len - 1) * step + c]
                         - src[start + Math.Max(i - r, 0) * step + c];
                }
            }
        }
    }

    /// <summary>The pixels as a WPF bitmap at the screen's scale.</summary>
    public BitmapSource ToBitmap(double dpiScale)
    {
        var bmp = BitmapSource.Create(Width, Height, 96 * dpiScale, 96 * dpiScale, PixelFormats.Bgr32, null, _bgra, Width * 4);
        bmp.Freeze();
        return bmp;
    }
}
