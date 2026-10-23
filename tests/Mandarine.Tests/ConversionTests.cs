using System.Text.Json;
using System.Diagnostics;
using System.IO;

namespace Mandarine.Tests;

public class ConversionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("mandarine-test").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Targets_exclude_own_format_and_intersect_across_files()
    {
        var png = Catalog.TargetsFor(new[] { "a.PNG" }).Select(f => f.Ext).ToList();
        Assert.Contains("jpg", png);
        Assert.DoesNotContain("png", png);

        Assert.DoesNotContain("jpg", Catalog.TargetsFor(new[] { "a.jpeg" }).Select(f => f.Ext));
        Assert.Contains("mp3", Catalog.TargetsFor(new[] { "a.mp4" }).Select(f => f.Ext));
        Assert.Empty(Catalog.TargetsFor(new[] { "a.png", "b.mp3" }));
        Assert.Empty(Catalog.TargetsFor(new[] { "a.xyz" }));
    }

    [Fact]
    public void OutputPath_never_overwrites()
    {
        var input = Path.Combine(_dir, "song.wav");
        Assert.Equal(Path.Combine(_dir, "song.mp3"), Converter.OutputPath(input, "mp3"));
        File.WriteAllText(Path.Combine(_dir, "song.mp3"), "");
        Assert.Equal(Path.Combine(_dir, "song (1).mp3"), Converter.OutputPath(input, "mp3"));
    }

    // Runs every catalog recipe through the real ffmpeg (needs ffmpeg on PATH).
    [Theory]
    [InlineData("-f lavfi -i testsrc=size=320x240:rate=1 -frames:v 1", "png")]
    [InlineData("-f lavfi -i sine=frequency=440:duration=1", "wav")]
    // Odd size: several encoders need the even-scale filter. Audio track: video -> MP3.
    [InlineData("-f lavfi -i testsrc=size=321x241:rate=10:duration=1 -f lavfi -i sine=duration=1 -shortest", "mp4")]
    public async Task Every_target_converts(string inputArgs, string ext)
    {
        var src = Path.Combine(_dir, $"src.{ext}");
        Ffmpeg($"{inputArgs} \"{src}\"");

        foreach (var target in Catalog.TargetsFor(new[] { src }))
        {
            var output = await Converter.ConvertAsync(src, target);
            Assert.True(new FileInfo(output).Length > 0, $"{ext} -> {target.Ext} produced an empty file");
        }
    }

    // md -> docx through wordcraft, then that docx to every document target.
    [Fact]
    public async Task Every_document_target_converts()
    {
        Assert.True(Tools.Has(Engine.Wordcraft), "run scripts/fetch-tools.ps1 first");
        var md = Path.Combine(_dir, "nota.md");
        File.WriteAllText(md, "# Título\n\nPárrafo con **negrita** y una lista:\n\n- uno\n- dos\n");

        var docx = await Converter.ConvertAsync(md, Catalog.TargetsFor(new[] { md }).Single(f => f.Ext == "docx"));
        var targets = Catalog.TargetsFor(new[] { docx });
        Assert.Equal(new[] { "pdf", "odt", "rtf", "md", "html", "txt" }, targets.Select(f => f.Ext));
        foreach (var target in targets)
        {
            var output = await Converter.ConvertAsync(docx, target);
            Assert.True(new FileInfo(output).Length > 0, $"docx -> {target.Ext} produced an empty file");
        }
        Assert.Contains("negrita", File.ReadAllText(Path.Combine(_dir, "nota.txt")));
    }

    [Fact]
    public async Task Every_sheet_target_converts()
    {
        var csv = Path.Combine(_dir, "ventas.csv");
        File.WriteAllText(csv, "Región,Q2,Q3\nNorte,1200,1450\n\"Sur, con coma\",980,1010\n");
        var targets = Catalog.TargetsFor(new[] { csv });
        Assert.Equal(new[] { "pdf", "xlsx", "tsv", "html" }, targets.Select(f => f.Ext));
        foreach (var target in targets)
            Assert.True(new FileInfo(await Converter.ConvertAsync(csv, target)).Length > 0, $"csv -> {target.Ext}");
        Assert.Contains("Sur, con coma\t980", File.ReadAllText(Path.Combine(_dir, "ventas.tsv")));
    }

    [Fact]
    public async Task Every_slide_target_converts()
    {
        var pptx = Path.Combine(_dir, "deck.pptx");
        using (var p = Process.Start(new ProcessStartInfo(Tools.Exe(Engine.Deckcraft))
               { ArgumentList = { "run", "--cmd", "slide.new={\"title\":\"Resultados\"}", "--save", pptx }, UseShellExecute = false, RedirectStandardOutput = true })!)
            p.WaitForExit();
        var targets = Catalog.TargetsFor(new[] { pptx });
        Assert.Equal(new[] { "pdf", "png", "jpg", "gif", "md", "txt" }, targets.Select(f => f.Ext));
        foreach (var target in targets)
            Assert.True(new FileInfo(await Converter.ConvertAsync(pptx, target)).Length > 0, $"pptx -> {target.Ext}");
        Assert.Contains("Resultados", File.ReadAllText(Path.Combine(_dir, "deck.txt")));
    }

    // Word when installed (PDF Reflow), pdfcraft otherwise; pages come back as one image or a folder.
    [Fact]
    public async Task Every_pdf_target_converts()
    {
        var pdf = await TwoPagePdf("informe");
        var targets = Catalog.TargetsFor(new[] { pdf });
        Assert.Equal(new[] { "docx", "png", "jpg", "txt", "html", "rtf" }, targets.Select(f => f.Ext));
        foreach (var target in targets)
        {
            var output = await Converter.ConvertAsync(pdf, target);
            Assert.True(File.Exists(output) || Directory.GetFiles(output).Length == 2, $"pdf -> {target.Ext} via {target.Engine}");
        }
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, "informe")).Length); // the PNG pages
        Assert.Contains("Segunda", File.ReadAllText(Path.Combine(_dir, "informe.txt")));
    }

    [Fact]
    public async Task Every_tool_runs()
    {
        var png = Path.Combine(_dir, "foto.png");
        var wav = Path.Combine(_dir, "voz.wav");
        var mp4 = Path.Combine(_dir, "clip.mp4");
        Ffmpeg($"-f lavfi -i testsrc=size=2500x1200 -frames:v 1 \"{png}\"");
        Ffmpeg($"-f lavfi -i sine=duration=1 -metadata title=secreto \"{wav}\"");
        Ffmpeg($"-f lavfi -i testsrc=size=321x241:rate=10:duration=1 -f lavfi -i sine=duration=1 -shortest -metadata title=secreto \"{mp4}\"");
        var a = await TwoPagePdf("a");
        var b = await TwoPagePdf("b");

        var webp = Path.Combine(_dir, "web.webp");
        Ffmpeg($"-f lavfi -i testsrc=size=300x200 -frames:v 1 \"{webp}\"");

        // OCR wants a scan and has its own test; "Sin fondo" too (it is slow); the editors have theirs.
        foreach (var file in new[] { png, webp, wav, mp4, a })
            foreach (var tool in Catalog.ToolsFor(new[] { file }).Where(t => t.Args is not ["ocr"] and not ["removebg"] && t.Engine != Engine.Editor))
                Assert.True(Produced(await Converter.ConvertAsync(file, tool)), $"{tool.Label} on {file}");
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, "a")).Length); // Dividir: one PDF per page

        var combine = Catalog.ToolsFor(new[] { a, b }).Single(t => t.Label == "Unir");
        Assert.DoesNotContain(combine, Catalog.ToolsFor(new[] { a }));
        var merged = await Converter.CombineAsync(new[] { a, b }, combine);
        Assert.Contains("\"pages\": 4", Run(Tools.Exe(Engine.Pdfcraft), "info", merged));

        // ZIP of a file and a folder, then back.
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "carpeta")).FullName;
        File.WriteAllText(Path.Combine(folder, "nota.txt"), "hola");
        var zip = await Converter.CombineAsync(new[] { wav, folder }, Catalog.ToolsFor(new[] { wav, folder }).Single(t => t.Ext == "zip"));
        var extracted = await Converter.ConvertAsync(zip, Catalog.ToolsFor(new[] { zip }).Single(t => t.Label == "Extraer"));
        Assert.Equal("hola", File.ReadAllText(Path.Combine(extracted, "carpeta", "nota.txt")));
        Assert.True(File.Exists(Path.Combine(extracted, "voz.wav")));
    }

    [Fact]
    public async Task Images_become_one_pdf_upright()
    {
        var png = Path.Combine(_dir, "a.png");
        var webp = Path.Combine(_dir, "b.webp");
        Ffmpeg($"-f lavfi -i testsrc=size=400x200 -frames:v 1 \"{png}\"");
        Ffmpeg($"-f lavfi -i testsrc=size=400x200 -frames:v 1 \"{webp}\"");
        var pdf = await Converter.CombineAsync(new[] { png, webp }, Catalog.TargetsFor(new[] { png, webp }).Single(f => f.Ext == "pdf"));
        Assert.Contains("\"pages\": 2", Run(Tools.Exe(Engine.Pdfcraft), "info", pdf));
    }

    [Fact]
    public async Task Ocr_makes_a_scan_searchable_in_spanish()
    {
        Assert.True(Ocr.Available, "Windows has no OCR language");
        var html = Path.Combine(_dir, "carta.html");
        File.WriteAllText(html, "<h1>Año de acción</h1><p>El pingüino comió una piña en la mañana.</p>");
        var text = await Converter.ConvertAsync(html, Catalog.TargetsFor(new[] { html }).Single(f => f.Ext == "pdf"));
        // A "scan": the page as a picture inside a PDF, without any text.
        var page = Path.Combine(_dir, "scan.png");
        Run(Tools.Exe(Engine.Pdfcraft), "render", text, "--page", "1", "--dpi", "300", "--out", page); // a 300 dpi scanner
        var scan = Path.Combine(_dir, "scan.pdf");
        var script = Path.Combine(_dir, "scan.json");
        File.WriteAllText(script, "[{\"tool\":\"doc_create\",\"args\":{\"from\":\"images\",\"paths\":[" + JsonSerializer.Serialize(page) + "],\"dpi\":300}},{\"tool\":\"doc_save\",\"args\":{\"doc\":1,\"path\":" + JsonSerializer.Serialize(scan) + "}}]");
        Run(Tools.Exe(Engine.Pdfcraft), "run", "--script", script);
        Assert.DoesNotContain("acción", Run(Tools.Exe(Engine.Pdfcraft), "text", scan));

        var ocr = Catalog.ToolsFor(new[] { scan }).Single(t => t.Args is ["ocr"]);
        var searchable = await Converter.ConvertAsync(scan, ocr);
        var read = Run(Tools.Exe(Engine.Pdfcraft), "text", searchable);
        Assert.Contains("acción", read);
        Assert.Contains("piña en la mañana", read); // ñ, and words stay apart
        await Assert.ThrowsAsync<InvalidOperationException>(() => Converter.ConvertAsync(searchable, ocr)); // already has text
    }

    [Fact]
    public async Task Background_removal_keeps_subject_and_clears_backdrop()
    {
        Assert.True(Background.Available, "run scripts/fetch-tools.ps1 first");
        var photo = Path.Combine(_dir, "pelota.jpg");
        // An orange ball on a plain light backdrop.
        Ffmpeg($"-f lavfi -i color=c=0xEEEEEE:s=600x400 -vf \"geq=r='if(lt(hypot(X-300,Y-200),120),255,238)':g='if(lt(hypot(X-300,Y-200),120),110,238)':b='if(lt(hypot(X-300,Y-200),120),20,238)'\" -frames:v 1 \"{photo}\"");
        var cut = await Converter.ConvertAsync(photo, Catalog.ToolsFor(new[] { photo }).Single(t => t.Args is ["removebg"]));
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(cut), default, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var px = new byte[600 * 400 * 4];
        new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(px, 600 * 4, 0);
        byte Alpha(int x, int y) => px[(y * 600 + x) * 4 + 3];
        Assert.True(Alpha(300, 200) > 200, $"centre alpha {Alpha(300, 200)}");
        Assert.True(Alpha(10, 10) < 40, $"corner alpha {Alpha(10, 10)}");
    }

    static bool Produced(string path) => File.Exists(path) ? new FileInfo(path).Length > 0 : Directory.GetFiles(path).Length > 0;

    [Fact]
    public void Actions_apply_to_any_file()
    {
        Assert.Equal(new[] { "share", "copy", "move", "path", "terminal", "reveal" }, Catalog.ActionsFor(new[] { "a.xyz", "carpeta" }).Select(f => f.Args[0]));
        Assert.All(Catalog.ActionsFor(new[] { "a.xyz" }), f => Assert.NotNull(f.Icon));
        Assert.Empty(Catalog.ActionsFor(Array.Empty<string>()));
    }

    [Fact]
    public void Jpeg_metadata_strip_keeps_orientation_and_pixels()
    {
        var src = Path.Combine(_dir, "src.jpg");
        Ffmpeg($"-f lavfi -i testsrc=size=400x200 -frames:v 1 \"{src}\"");
        var jpeg = File.ReadAllBytes(src);
        // EXIF with Orientation 6 and Make "Cam", plus a comment, right after SOI.
        byte[] exif =
        {
            0xFF, 0xE1, 0x00, 0x2E, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0, (byte)'M', (byte)'M', 0, 0x2A, 0, 0, 0, 8,
            0, 2, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0x01, 0x0F, 0, 2, 0, 0, 0, 4, (byte)'C', (byte)'a', (byte)'m', 0, 0, 0, 0, 0,
        };
        var comment = new byte[] { 0xFF, 0xFE, 0, 15 }.Concat("SecretComment"u8.ToArray()).ToArray();
        var tagged = Path.Combine(_dir, "tagged.jpg");
        File.WriteAllBytes(tagged, jpeg[..2].Concat(exif).Concat(comment).Concat(jpeg[2..]).ToArray());

        var clean = Path.Combine(_dir, "clean.jpg");
        Metadata.Strip(tagged, clean);
        var bytes = File.ReadAllBytes(clean);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("SecretComment"u8));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("Cam\0"u8));
        Assert.True(bytes.AsSpan().EndsWith(jpeg.AsSpan()[^200..]), "image data must be copied untouched");
        // The minimal EXIF still rotates the photo; the camera tags are gone.
        var frame = Run("ffprobe", "-v", "error", "-show_frames", "-show_entries", "frame_side_data:frame_tags", "-of", "compact", clean);
        Assert.Contains("rotation=-90", frame);
        Assert.DoesNotContain("Make", frame);
    }

    async Task<string> TwoPagePdf(string name)
    {
        var html = Path.Combine(_dir, name + ".html");
        File.WriteAllText(html, "<h1>Primera</h1><p>Hola.</p><h1 style=\"page-break-before:always\">Segunda</h1><p>Chau.</p>");
        var pdf = await Converter.ConvertAsync(html, Catalog.TargetsFor(new[] { html }).Single(f => f.Ext == "pdf"));
        if (!Run(Tools.Exe(Engine.Pdfcraft), "info", pdf).Contains("\"pages\": 2")) Assert.Fail("fixture should have two pages");
        return pdf;
    }

    static string Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    static void Ffmpeg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y {args}") { UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
