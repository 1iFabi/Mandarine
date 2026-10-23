using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;

namespace Mandarine;

/// <summary>
/// Cuts the subject out of a photo with BiRefNet-lite (MIT; ONNX export from rembg), run by ONNX Runtime
/// on the CPU. The result is a PNG whose alpha is the model's mask; colours are left untouched.
/// </summary>
public static class Background
{
    const int Side = 1024; // the model's fixed input
    static readonly float[] Mean = { 0.485f, 0.456f, 0.406f }, Std = { 0.229f, 0.224f, 0.225f };

    static string Model => Path.Combine(AppContext.BaseDirectory, "tools", "models", "birefnet-lite.onnx");

    public static bool Available => File.Exists(Model);

    public static async Task Remove(string input, string output)
    {
        var tmp = Directory.CreateTempSubdirectory("mandarine-").FullName;
        try
        {
            // ffmpeg reads any still (HEIC, WebP...) and applies the EXIF rotation: one pass gives the
            // full-size RGBA picture and the 1024x1024 RGB copy the model looks at.
            var full = Path.Combine(tmp, "full.png");
            var small = Path.Combine(tmp, "small.rgb");
            await Converter.RunTool(new[] { input }, small, new Format("", "", new[]
            {
                "-frames:v", "1", "-pix_fmt", "rgba", full,
                "-frames:v", "1", "-vf", $"scale={Side}:{Side}:flags=bilinear", "-pix_fmt", "rgb24", "-f", "rawvideo",
            }));
            var mask = await Task.Run(() => Predict(File.ReadAllBytes(small)));
            await Task.Run(() => Apply(full, mask, output));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    /// <summary>Foreground probability per pixel of the 1024x1024 input.</summary>
    static float[] Predict(byte[] rgb)
    {
        // Normalised like rembg: scale by the brightest value, then ImageNet mean/std, planar (NCHW).
        float max = 1;
        foreach (var b in rgb) max = Math.Max(max, b);
        var input = new float[3 * Side * Side];
        for (var i = 0; i < Side * Side; i++)
            for (var c = 0; c < 3; c++)
                input[c * Side * Side + i] = (rgb[i * 3 + c] / max - Mean[c]) / Std[c];

        // shortcut: a new session per image (~2 s load) keeps 300 MB out of memory between uses; cache it if batches feel slow.
        using var session = new InferenceSession(Model);
        using var tensor = OrtValue.CreateTensorValueFromMemory(input, new long[] { 1, 3, Side, Side });
        using var runOptions = new RunOptions();
        var inputs = new Dictionary<string, OrtValue> { [session.InputNames[0]] = tensor };
        using var outputs = session.Run(runOptions, inputs, new[] { session.OutputNames[^1] }); // the finest of its side outputs
        var logits = outputs[0].GetTensorDataAsSpan<float>();
        var mask = new float[Side * Side];
        for (var i = 0; i < mask.Length; i++) mask[i] = 1 / (1 + MathF.Exp(-logits[i]));
        return mask;
    }

    static void Apply(string fullPng, float[] mask, string output)
    {
        var frame = BitmapDecoder.Create(new Uri(fullPng), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        for (var y = 0; y < h; y++)
        {
            // Bilinear sample of the mask at this pixel's position.
            var my = Math.Clamp((y + 0.5f) * Side / h - 0.5f, 0, Side - 1);
            int y0 = (int)my, y1 = Math.Min(y0 + 1, Side - 1);
            var fy = my - y0;
            for (var x = 0; x < w; x++)
            {
                var mx = Math.Clamp((x + 0.5f) * Side / w - 0.5f, 0, Side - 1);
                int x0 = (int)mx, x1 = Math.Min(x0 + 1, Side - 1);
                var fx = mx - x0;
                var top = mask[y0 * Side + x0] * (1 - fx) + mask[y0 * Side + x1] * fx;
                var bottom = mask[y1 * Side + x0] * (1 - fx) + mask[y1 * Side + x1] * fx;
                var alpha = top * (1 - fy) + bottom * fy;
                var a = (y * w + x) * 4 + 3;
                px[a] = (byte)Math.Round(px[a] * alpha);
            }
        }
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var f = new FileStream(output, FileMode.CreateNew);
        png.Save(f);
    }
}
