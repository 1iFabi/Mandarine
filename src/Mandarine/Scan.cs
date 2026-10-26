using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandarine;

public enum ScanMode { Color, Gray, BlackWhite }

/// <summary>A photo of a sheet of paper made to look scanned: find the sheet, square it up, clean the background.</summary>
public static class Scan
{
    /// <summary>A4 at 300 dpi: the long side every page is drawn at, so pages print at their real size and OCR reads them well.</summary>
    public const int PageLongSide = 3508;

    /// <summary>
    /// The sheet's corners as shares of the picture (top-left, top-right, bottom-right, bottom-left).
    /// The sheet is the largest bright, unsaturated region; when there is none, the whole picture.
    /// </summary>
    public static Point[] FindPage(BitmapSource image)
    {
        var small = ImageEdit.Scaled(image, 512);
        var px = ImageEdit.Bgra(small, out var w, out var h);
        var lum = new byte[w * h];
        var hist = new int[256];
        for (var i = 0; i < w * h; i++)
        {
            int b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
            lum[i] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
            hist[lum[i]]++;
        }
        var threshold = Otsu(hist, w * h);
        var paper = new bool[w * h];
        for (var i = 0; i < w * h; i++)
        {
            int b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
            paper[i] = lum[i] > threshold && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 70;
        }

        // The largest connected region of paper pixels.
        var label = new int[w * h];
        var best = (Id: 0, Size: 0);
        var queue = new Queue<int>();
        for (int start = 0, id = 1; start < w * h; start++)
        {
            if (!paper[start] || label[start] != 0) continue;
            var size = 0;
            label[start] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                size++;
                int x = at % w, y = at / w;
                foreach (var next in new[] { x > 0 ? at - 1 : -1, x < w - 1 ? at + 1 : -1, y > 0 ? at - w : -1, y < h - 1 ? at + w : -1 })
                    if (next >= 0 && paper[next] && label[next] == 0) { label[next] = id; queue.Enqueue(next); }
            }
            if (size > best.Size) best = (id, size);
            id++;
        }
        var whole = new[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) };
        var share = (double)best.Size / (w * h);
        if (share < 0.12 || share > 0.97) return whole; // no sheet in sight, or the sheet is the whole photo

        // Corners: the region's points furthest along each diagonal.
        Point tl = default, tr = default, br = default, bl = default;
        double minSum = double.MaxValue, maxSum = double.MinValue, minDiff = double.MaxValue, maxDiff = double.MinValue;
        for (var i = 0; i < w * h; i++)
        {
            if (label[i] != best.Id) continue;
            double x = i % w, y = i / w;
            if (x + y < minSum) { minSum = x + y; tl = new Point(x, y); }
            if (x + y > maxSum) { maxSum = x + y; br = new Point(x, y); }
            if (x - y > maxDiff) { maxDiff = x - y; tr = new Point(x, y); }
            if (x - y < minDiff) { minDiff = x - y; bl = new Point(x, y); }
        }
        return new[] { tl, tr, br, bl }.Select(p => new Point(Math.Clamp((p.X + 0.5) / w, 0, 1), Math.Clamp((p.Y + 0.5) / h, 0, 1))).ToArray();
    }

    /// <summary>The sheet inside the corners (shares, clockwise from top-left), squared up, its long side `longSide` px, cleaned per mode.</summary>
    public static BitmapSource Page(BitmapSource image, IReadOnlyList<Point> corners, ScanMode mode, int longSide = PageLongSide)
    {
        var src = ImageEdit.Bgra(image, out var sw, out var sh);
        var p = corners.Select(c => new Point(c.X * sw, c.Y * sh)).ToArray();
        double Dist(Point a, Point b) => (a - b).Length;
        var width = Math.Max(Dist(p[0], p[1]), Dist(p[3], p[2]));
        var height = Math.Max(Dist(p[0], p[3]), Dist(p[1], p[2]));
        var k = longSide / Math.Max(1, Math.Max(width, height));
        int w = Math.Max(1, (int)Math.Round(width * k)), h = Math.Max(1, (int)Math.Round(height * k));
        var m = Homography(new[] { new Point(0, 0), new Point(w, 0), new Point(w, h), new Point(0, h) }, p);

        var dst = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // Output pixel centre -> source position, sampled bilinearly.
                double u = x + 0.5, v = y + 0.5;
                var d = m[6] * u + m[7] * v + 1;
                var sx = (m[0] * u + m[1] * v + m[2]) / d - 0.5;
                var sy = (m[3] * u + m[4] * v + m[5]) / d - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, sw - 1), y0 = Math.Clamp((int)Math.Floor(sy), 0, sh - 1);
                int x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                double fx = Math.Clamp(sx - x0, 0, 1), fy = Math.Clamp(sy - y0, 0, 1);
                var o = (y * w + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    var top = src[(y0 * sw + x0) * 4 + c] * (1 - fx) + src[(y0 * sw + x1) * 4 + c] * fx;
                    var bottom = src[(y1 * sw + x0) * 4 + c] * (1 - fx) + src[(y1 * sw + x1) * 4 + c] * fx;
                    dst[o + c] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
                dst[o + 3] = 255;
            }
        });
        Clean(dst, w, h, mode);
        return ImageEdit.FromBgra(dst, w, h);
    }

    /// <summary>A page as the JPEG the PDF holds: one gray channel unless the mode keeps colour.</summary>
    public static byte[] Jpeg(BitmapSource page, ScanMode mode)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = mode == ScanMode.BlackWhite ? 90 : 85 };
        encoder.Frames.Add(BitmapFrame.Create(mode == ScanMode.Color ? page : new FormatConvertedBitmap(page, PixelFormats.Gray8, null, 0)));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Evens out the lighting (each pixel against the brightness around it, so shadows and a yellowish
    /// light turn white) and, for black and white, keeps what is clearly darker than its surroundings.
    /// </summary>
    static void Clean(byte[] px, int w, int h, ScanMode mode)
    {
        var lum = new double[w * h];
        for (var i = 0; i < w * h; i++) lum[i] = (px[i * 4 + 2] * 299 + px[i * 4 + 1] * 587 + px[i * 4] * 114) / 1000.0;
        // Local mean over a window a fifteenth of the page wide (about 2 cm): wider than letters, narrower than shadows.
        var mean = BoxMean(lum, w, h, Math.Max(4, Math.Max(w, h) / 30));
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var bg = Math.Max(mean[i], 1);
                switch (mode)
                {
                    case ScanMode.BlackWhite:
                        var ink = lum[i] < bg * 0.82 ? (byte)0 : (byte)255;
                        px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = ink;
                        break;
                    case ScanMode.Gray:
                        var g = Stretch(lum[i] / bg * 255);
                        px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = g;
                        break;
                    default:
                        for (var c = 0; c < 3; c++) px[i * 4 + c] = Stretch(px[i * 4 + c] / bg * 255);
                        break;
                }
            }
        });

        // Paper (at or near the background) goes to white; ink keeps its depth.
        static byte Stretch(double v) => (byte)Math.Clamp((v - 40) / (235 - 40) * 255, 0, 255);
    }

    static double[] BoxMean(double[] v, int w, int h, int r)
    {
        var sum = new double[(w + 1) * (h + 1)]; // integral image
        for (var y = 0; y < h; y++)
        {
            double row = 0;
            for (var x = 0; x < w; x++)
            {
                row += v[y * w + x];
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + row;
            }
        }
        var mean = new double[w * h];
        Parallel.For(0, h, y =>
        {
            int y0 = Math.Max(0, y - r), y1 = Math.Min(h, y + r + 1);
            for (var x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w, x + r + 1);
                var s = sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
                mean[y * w + x] = s / ((x1 - x0) * (y1 - y0));
            }
        });
        return mean;
    }

    static int Otsu(int[] hist, int total)
    {
        double sum = 0;
        for (var i = 0; i < 256; i++) sum += i * hist[i];
        double sumB = 0, best = -1;
        int wB = 0, threshold = 128;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            var wF = total - wB;
            if (wF == 0) break;
            sumB += t * hist[t];
            var between = (double)wB * wF * Math.Pow(sumB / wB - (sum - sumB) / wF, 2);
            if (between > best) { best = between; threshold = t; }
        }
        return threshold;
    }

    /// <summary>The projective map taking each `from` point onto its `to` point: 8 coefficients (h33 = 1).</summary>
    public static double[] Homography(IReadOnlyList<Point> from, IReadOnlyList<Point> to)
    {
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            double x = from[i].X, y = from[i].Y, u = to[i].X, v = to[i].Y;
            double[] r1 = { x, y, 1, 0, 0, 0, -x * u, -y * u, u };
            double[] r2 = { 0, 0, 0, x, y, 1, -x * v, -y * v, v };
            for (var j = 0; j < 9; j++) { a[2 * i, j] = r1[j]; a[2 * i + 1, j] = r2[j]; }
        }
        // Gaussian elimination with partial pivoting.
        for (var c = 0; c < 8; c++)
        {
            var pivot = Enumerable.Range(c, 8 - c).MaxBy(r => Math.Abs(a[r, c]));
            if (Math.Abs(a[pivot, c]) < 1e-12) throw new InvalidOperationException("Las esquinas no forman una hoja: sepáralas.");
            for (var j = 0; j < 9; j++) (a[c, j], a[pivot, j]) = (a[pivot, j], a[c, j]);
            for (var r = 0; r < 8; r++)
            {
                if (r == c) continue;
                var f = a[r, c] / a[c, c];
                for (var j = c; j < 9; j++) a[r, j] -= f * a[c, j];
            }
        }
        return Enumerable.Range(0, 8).Select(r => a[r, 8] / a[r, r]).ToArray();
    }
}
