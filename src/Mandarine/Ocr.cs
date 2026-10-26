using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract;

namespace Mandarine;

/// <summary>
/// Scanned PDF -> searchable PDF. pdfcraft renders the pages, Tesseract (Apache-2.0, tessdata_best
/// Spanish + English) reads them (pdfcraft's own OCR and Windows' both lose á, ñ...), and the result
/// is written here: each page is its picture with the recognised words laid invisibly on top.
/// </summary>
public static class Ocr
{
    const double Dpi = 300; // what scanners use and what Tesseract is tuned for
    // shortcut: Spanish + English only; map the Windows display language to more tessdata files when someone needs it.
    const string Languages = "spa+eng";

    static string TessData => Path.Combine(AppContext.BaseDirectory, "tools", "tessdata");

    public static bool Available => Languages.Split('+').All(l => File.Exists(Path.Combine(TessData, l + ".traineddata")));

    public static async Task SearchablePdf(string input, string output)
    {
        var tmp = Directory.CreateTempSubdirectory("mandarine-").FullName;
        try
        {
            var text = Path.Combine(tmp, "text.txt");
            await Converter.RunTool(new[] { input }, text, new Format("", "", Catalog.PdfSteps("{\"tool\":\"doc_export_text\",\"args\":{\"doc\":1,\"path\":{out}}}"), Engine.Pdfcraft));
            if (File.ReadAllText(text).Any(char.IsLetter))
                throw new InvalidOperationException("Este PDF ya tiene texto seleccionable.");

            var pagesDir = Directory.CreateDirectory(Path.Combine(tmp, "pages")).FullName;
            await Converter.RunTool(new[] { input }, pagesDir, new Format("", "", Catalog.PdfPages("jpeg", Dpi), Engine.Pdfcraft));
            var pages = await Task.Run(() =>
            {
                using var engine = new TesseractEngine(TessData, Languages, EngineMode.LstmOnly);
                return Directory.GetFiles(pagesDir).OrderBy(f => f.Length).ThenBy(f => f, StringComparer.Ordinal)
                    .Select(jpg => new Page(File.ReadAllBytes(jpg), Read(engine, jpg)))
                    .ToList();
            });
            WritePdf(output, pages);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    /// <summary>Scanned pages (drawn at 300 dpi) into one PDF; with readText, each page's words go invisibly over it.</summary>
    public static async Task ScannedPdf(IReadOnlyList<byte[]> jpegs, string output, bool readText)
    {
        var tmp = Directory.CreateTempSubdirectory("mandarine-").FullName;
        try
        {
            var done = await Task.Run(() =>
            {
                using var engine = readText ? new TesseractEngine(TessData, Languages, EngineMode.LstmOnly) : null;
                return jpegs.Select((jpeg, i) =>
                {
                    if (engine == null) return new Page(jpeg, Array.Empty<Word>());
                    var jpg = Path.Combine(tmp, $"{i}.jpg");
                    File.WriteAllBytes(jpg, jpeg);
                    return new Page(jpeg, Read(engine, jpg));
                }).ToList();
            });
            WritePdf(output, done);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    sealed record Word(string Text, double X, double Y, double Width, double Height); // pixels, top-left origin
    sealed record Page(byte[] Jpeg, IReadOnlyList<Word> Words);

    static IReadOnlyList<Word> Read(TesseractEngine engine, string jpg)
    {
        using var image = Pix.LoadFromFile(jpg);
        using var page = engine.Process(image);
        using var it = page.GetIterator();
        var words = new List<Word>();
        it.Begin();
        do
        {
            var word = it.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrEmpty(word) && it.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                words.Add(new Word(word, box.X1, box.Y1, box.Width, box.Height));
        } while (it.Next(PageIteratorLevel.Word));
        return words;
    }

    /// <summary>A minimal PDF 1.4: per page one JPEG and invisible (render mode 3) Helvetica text.</summary>
    static void WritePdf(string path, IReadOnlyList<Page> pages)
    {
        var winAnsi = CodePagesEncoding(1252);
        var scale = 72 / Dpi; // pixels -> points
        using var o = new FileStream(path, FileMode.CreateNew);
        var offsets = new List<long>();
        void Raw(string s) { var b = Encoding.ASCII.GetBytes(s); o.Write(b); }
        void Obj(string dict, byte[]? stream = null)
        {
            offsets.Add(o.Position);
            Raw($"{offsets.Count} 0 obj\n{dict}");
            if (stream != null) { Raw("\nstream\n"); o.Write(stream); Raw("\nendstream"); }
            Raw("\nendobj\n");
        }
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        Raw("%PDF-1.4\n");
        o.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }); // marks the file as binary
        // Objects: 1 catalog, 2 page tree, 3 font, then page / contents / image per page.
        var kids = string.Join(" ", pages.Select((_, i) => $"{4 + 3 * i} 0 R"));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pages.Count; i++)
        {
            var (jpeg, words) = (pages[i].Jpeg, pages[i].Words);
            var (pw, ph, components) = JpegInfo(jpeg);
            double w = pw * scale, h = ph * scale;
            var content = new StringBuilder($"q {F(w)} 0 0 {F(h)} 0 0 cm /Im0 Do Q\nBT 3 Tr /F1 1 Tf\n");
            foreach (var word in words)
            {
                // Font size 1 scaled by the text matrix: height from the box, width stretched to fill it
                // (Helvetica averages ~0.5 em per glyph), so selections line up with the picture. The trailing
                // space keeps copied text from gluing words together.
                var size = word.Height * scale;
                var stretch = word.Width * scale / (0.5 * Math.Max(1, word.Text.Length));
                var baseline = h - (word.Y + word.Height) * scale + 0.2 * size;
                content.Append($"{F(stretch)} 0 0 {F(size)} {F(word.X * scale)} {F(baseline)} Tm ({Escape(word.Text)} ) Tj\n");
            }
            content.Append("ET");
            var contentBytes = winAnsi.GetBytes(content.ToString());

            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(w)} {F(h)}] /Resources << /XObject << /Im0 {6 + 3 * i} 0 R >> /Font << /F1 3 0 R >> >> /Contents {5 + 3 * i} 0 R >>");
            Obj($"<< /Length {contentBytes.Length} >>", contentBytes);
            var space = components switch { 1 => "/DeviceGray", 4 => "/DeviceCMYK", _ => "/DeviceRGB" };
            Obj($"<< /Type /XObject /Subtype /Image /Width {pw} /Height {ph} /ColorSpace {space} /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>", jpeg);
        }
        var xref = o.Position;
        Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets) Raw($"{off:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    static Encoding CodePagesEncoding(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback); // "?" for what WinAnsi lacks
    }

    /// <summary>Width, height and colour components from the JPEG's frame header (SOFn).</summary>
    static (int Width, int Height, int Components) JpegInfo(byte[] jpeg)
    {
        for (var at = 2; at + 9 < jpeg.Length;)
        {
            if (jpeg[at] != 0xFF) break;
            var marker = jpeg[at + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return (jpeg[at + 7] << 8 | jpeg[at + 8], jpeg[at + 5] << 8 | jpeg[at + 6], jpeg[at + 9]);
            at += 2 + (jpeg[at + 2] << 8 | jpeg[at + 3]);
        }
        throw new InvalidDataException("No se pudo leer la página renderizada.");
    }
}
