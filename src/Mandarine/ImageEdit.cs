using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Mandarine;

public enum MarkKind { Arrow, Box, Pen, Highlight, Text }

/// <summary>One annotation in image pixels: two corners (arrow tail/head, box), a stroke's points, or a text's top-left.</summary>
public sealed record Mark(MarkKind Kind, Color Color, Point[] Points, string Text = "");

/// <summary>Each value from -1 to 1; 0 leaves the picture as it is.</summary>
public readonly record struct Adjustments(double Brightness, double Contrast, double Saturation, double Warmth);

/// <summary>Margin and corners are shares of the picture's size, so one style fits pictures of any size. Ratio 0 = follow the picture.</summary>
public sealed record FrameStyle(Brush Background, double Margin, double Corners, bool Shadow, double Ratio);

/// <summary>
/// The picture editors' operations, on 96-dpi Bgra32 bitmaps where one DIP is one pixel. The editor
/// window previews with the same calls it saves with, so what is seen is what is written.
/// </summary>
public static class ImageEdit
{
    static readonly string[] Encodable = { "jpg", "jpeg", "png", "bmp", "tif", "tiff" };

    /// <summary>Same format as the input when WPF can write it; PNG otherwise (WebP, HEIC, AVIF).</summary>
    public static string OutputExt(string input)
    {
        var ext = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        return Encodable.Contains(ext) ? ext : "png";
    }

    /// <summary>Any still ffmpeg reads, upright (ffmpeg applies the EXIF rotation, WPF would not).</summary>
    public static async Task<BitmapSource> Load(string path)
    {
        var tmp = Directory.CreateTempSubdirectory("mandarine-").FullName;
        try
        {
            var png = Path.Combine(tmp, "in.png");
            await Converter.RunTool(new[] { path }, png, new Format("", "", new[] { "-frames:v", "1", "-pix_fmt", "rgba" }));
            var frame = BitmapDecoder.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            // Re-created at 96 dpi: the file's own dpi would make DIPs differ from pixels.
            return FromPixels(Pixels(frame, out var w, out var h), w, h);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    public static void Save(BitmapSource image, string path)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        // JPEG and BMP have no alpha; the edits keep those pictures opaque.
        if (encoder is JpegBitmapEncoder or BmpBitmapEncoder) image = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var f = new FileStream(path, FileMode.CreateNew);
        encoder.Save(f);
    }

    /// <summary>A smaller copy for live previews; the edits are relative, so they look the same on it.</summary>
    public static BitmapSource Scaled(BitmapSource image, int maxSide)
    {
        var k = (double)maxSide / Math.Max(image.PixelWidth, image.PixelHeight);
        if (k >= 1) return image;
        var scaled = new TransformedBitmap(image, new ScaleTransform(k, k));
        return FromPixels(Pixels(scaled, out var w, out var h), w, h);
    }

    /// <summary>Crops to a share of the picture (0-1 on each axis), so one crop fits pictures of any size.</summary>
    public static BitmapSource Crop(BitmapSource image, Rect share)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var x = Math.Clamp((int)Math.Round(share.X * w), 0, w - 1);
        var y = Math.Clamp((int)Math.Round(share.Y * h), 0, h - 1);
        var cw = Math.Clamp((int)Math.Round(share.Width * w), 1, w - x);
        var ch = Math.Clamp((int)Math.Round(share.Height * h), 1, h - y);
        var cropped = new CroppedBitmap(image, new Int32Rect(x, y, cw, ch));
        cropped.Freeze();
        return cropped;
    }

    public static BitmapSource Adjust(BitmapSource image, Adjustments a)
    {
        var px = Pixels(image, out var w, out var h);
        double contrast = 1 + a.Contrast, saturation = 1 + a.Saturation, light = a.Brightness * 0.35, warm = a.Warmth * 0.12;
        Parallel.For(0, h, y =>
        {
            for (var i = y * w * 4; i < (y + 1) * w * 4; i += 4)
            {
                double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
                r = (r - 0.5) * contrast + 0.5 + light + warm;
                g = (g - 0.5) * contrast + 0.5 + light;
                b = (b - 0.5) * contrast + 0.5 + light - warm;
                var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                px[i] = Byte(luma + (b - luma) * saturation);
                px[i + 1] = Byte(luma + (g - luma) * saturation);
                px[i + 2] = Byte(luma + (r - luma) * saturation);
            }
        });
        return FromPixels(px, w, h);
    }

    /// <summary>Hides areas under solid black, or under blocks so coarse (a third of the area's short side) that text and faces can't be read.</summary>
    public static BitmapSource Redact(BitmapSource image, IEnumerable<Int32Rect> areas, bool pixelate)
    {
        var px = Pixels(image, out var w, out var h);
        foreach (var area in areas)
        {
            int x0 = Math.Clamp(area.X, 0, w), y0 = Math.Clamp(area.Y, 0, h);
            int x1 = Math.Clamp(area.X + area.Width, 0, w), y1 = Math.Clamp(area.Y + area.Height, 0, h);
            var block = pixelate ? Math.Max(8, Math.Min(x1 - x0, y1 - y0) / 3) : Math.Max(x1 - x0, y1 - y0);
            for (var by = y0; by < y1; by += block)
            for (var bx = x0; bx < x1; bx += block)
            {
                int ex = Math.Min(bx + block, x1), ey = Math.Min(by + block, y1);
                long sb = 0, sg = 0, sr = 0, n = 0;
                if (pixelate)
                    for (var y = by; y < ey; y++)
                    for (var x = bx; x < ex; x++, n++)
                    {
                        var i = (y * w + x) * 4;
                        sb += px[i]; sg += px[i + 1]; sr += px[i + 2];
                    }
                n = Math.Max(n, 1);
                for (var y = by; y < ey; y++)
                for (var x = bx; x < ex; x++)
                {
                    var i = (y * w + x) * 4;
                    px[i] = (byte)(sb / n); px[i + 1] = (byte)(sg / n); px[i + 2] = (byte)(sr / n); px[i + 3] = 255;
                }
            }
        }
        return FromPixels(px, w, h);
    }

    public static BitmapSource Annotate(BitmapSource image, IEnumerable<Mark> marks)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, w, h));
            DrawMarks(dc, marks, w, h);
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Stroke width for a picture: thick enough to read once the picture is shrunk to a screen.</summary>
    public static double StrokeFor(double w, double h) => Math.Max(2, Math.Min(w, h) / 160);

    public static double TextSizeFor(double w, double h) => Math.Max(12, Math.Min(w, h) / 20);

    /// <summary>Draws annotations in picture pixels; the editor calls it too, for the live preview.</summary>
    public static void DrawMarks(DrawingContext dc, IEnumerable<Mark> marks, double w, double h)
    {
        var unit = StrokeFor(w, h);
        foreach (var m in marks)
        {
            var brush = Frozen(new SolidColorBrush(m.Color));
            var pen = Frozen(new Pen(brush, unit) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
            var p = m.Points;
            switch (m.Kind)
            {
                case MarkKind.Arrow:
                    var along = p[1] - p[0];
                    if (along.Length < 1) break;
                    along.Normalize();
                    var head = unit * 5;
                    var across = new Vector(-along.Y, along.X) * head * 0.6;
                    var neck = p[1] - along * head;
                    dc.DrawLine(pen, p[0], neck + along * unit);
                    dc.DrawGeometry(brush, Frozen(new Pen(brush, unit) { LineJoin = PenLineJoin.Round }), Polygon(p[1], neck + across, neck - across));
                    break;
                case MarkKind.Box:
                    dc.DrawRoundedRectangle(null, pen, new Rect(p[0], p[1]), unit, unit);
                    break;
                case MarkKind.Highlight:
                    dc.DrawRectangle(Frozen(new SolidColorBrush(Color.FromArgb(0x66, m.Color.R, m.Color.G, m.Color.B))), null, new Rect(p[0], p[1]));
                    break;
                case MarkKind.Pen:
                    if (p.Length < 2) break;
                    var line = new StreamGeometry();
                    using (var g = line.Open())
                    {
                        g.BeginFigure(p[0], false, false);
                        g.PolyLineTo(p.Skip(1).ToList(), true, true);
                    }
                    line.Freeze();
                    dc.DrawGeometry(null, pen, line);
                    break;
                case MarkKind.Text:
                    var text = new FormattedText(m.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), TextSizeFor(w, h), brush, 1);
                    var glyphs = text.BuildGeometry(p[0]);
                    // A halo in the opposite tone keeps the text legible on any part of the picture.
                    var halo = m.Color.R + m.Color.G + m.Color.B > 600 ? Colors.Black : Colors.White;
                    dc.DrawGeometry(null, Frozen(new Pen(Frozen(new SolidColorBrush(halo)), TextSizeFor(w, h) * 0.16) { LineJoin = PenLineJoin.Round }), glyphs);
                    dc.DrawGeometry(brush, null, glyphs);
                    break;
            }
        }
    }

    /// <summary>Sets the picture on a background with a margin, rounded corners and a shadow, at a chosen canvas ratio.</summary>
    public static BitmapSource Frame(BitmapSource image, FrameStyle style)
    {
        double w = image.PixelWidth, h = image.PixelHeight, big = Math.Max(w, h);
        double cw = w + 2 * style.Margin * big, ch = h + 2 * style.Margin * big;
        if (style.Ratio > 0)
        {
            if (cw / ch < style.Ratio) cw = ch * style.Ratio;
            else ch = cw / style.Ratio;
        }
        var picture = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(style.Corners * Math.Min(w, h)),
            Background = new ImageBrush(image), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = style.Shadow ? new DropShadowEffect { BlurRadius = big * 0.04, ShadowDepth = big * 0.012, Direction = 270, Opacity = 0.35 } : null,
        };
        var canvas = new Grid { Width = cw, Height = ch, Background = style.Background, Children = { picture } };
        canvas.Measure(new Size(cw, ch));
        canvas.Arrange(new Rect(0, 0, cw, ch));
        var bitmap = new RenderTargetBitmap((int)Math.Round(cw), (int)Math.Round(ch), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        bitmap.Freeze();
        return bitmap;
    }

    static Geometry Polygon(params Point[] points)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(points[0], true, true);
            c.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    static byte[] Pixels(BitmapSource image, out int w, out int h)
    {
        var bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth;
        h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        return px;
    }

    static BitmapSource FromPixels(byte[] px, int w, int h)
    {
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bitmap.Freeze();
        return bitmap;
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
