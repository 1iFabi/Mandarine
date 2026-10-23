using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mandarine;

/// <summary>Which tool performs a conversion.</summary>
public enum Engine { Ffmpeg, Wordcraft, Gridcraft, Deckcraft, Pdfcraft, Word, Builtin, Action, Editor }

/// <summary>
/// One wheel entry. Args are ffmpeg's output options; for the craft CLIs the whole command line, with
/// {in}/{out} placeholders; for pdfcraft a JSON step script with the same placeholders.
/// Ext "" keeps the input's extension (tools). Icon marks a tool; Combines turns many files into one
/// (MinFiles sets how many it needs); ToFolder tools write several files (pages, parts). Builtin's Args[0]
/// names the in-process operation.
/// </summary>
public record Format(string Ext, string Label, string[] Args, Engine Engine = Engine.Ffmpeg, string? Icon = null,
    bool Combines = false, int MinFiles = 1, bool ToFolder = false);

public static class Catalog
{
    const string Even = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    const string H264 = "-c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p -vf " + Even + " -c:a aac -b:a 192k";
    static readonly string[] CraftConvert = { "convert", "{in}", "{out}" };

    static readonly Format[] Images =
    {
        new("jpg", "JPG", Split("-frames:v 1 -q:v 2")),
        new("png", "PNG", Split("-frames:v 1")),
        new("webp", "WebP", Split("-frames:v 1 -c:v libwebp -quality 85")),
        new("tiff", "TIFF", Split("-frames:v 1")),
        new("avif", "AVIF", Split("-frames:v 1 -c:v libaom-av1 -still-picture 1 -crf 30")),
        new("bmp", "BMP", Split("-frames:v 1")),
        // All dropped images become the pages of one PDF, in order.
        // shortcut: pages take each image's own resolution (72 dpi when unset), so big photos make big pages; fit to A4 if that bothers anyone.
        new("pdf", "PDF", new[] { "[{\"tool\":\"doc_create\",\"args\":{\"from\":\"images\",\"paths\":{in}}},{\"tool\":\"doc_save\",\"args\":{\"doc\":1,\"path\":{out}}}]" }, Engine.Pdfcraft, Combines: true),
    };

    static readonly Format[] Audio =
    {
        new("mp3", "MP3", Split("-vn -c:a libmp3lame -q:a 2")),
        new("m4a", "M4A", Split("-vn -c:a aac -b:a 192k")),
        new("wav", "WAV", Split("-vn -c:a pcm_s16le")),
        new("flac", "FLAC", Split("-vn -c:a flac")),
        new("ogg", "OGG", Split("-vn -c:a libvorbis -q:a 5")),
        new("opus", "Opus", Split("-vn -c:a libopus -b:a 128k")),
        new("aiff", "AIFF", Split("-vn -c:a pcm_s16be")),
        new("wma", "WMA", Split("-vn -c:a wmav2 -b:a 192k")),
    };

    static readonly Format[] Video =
    {
        new("mp4", "MP4", Split(H264 + " -movflags +faststart")),
        new("mov", "MOV", Split(H264)),
        new("mkv", "MKV", Split(H264)),
        new("webm", "WebM", Split("-c:v libvpx-vp9 -crf 32 -b:v 0 -c:a libopus -b:a 128k")),
        new("avi", "AVI", Split("-c:v mpeg4 -q:v 3 -vf " + Even + " -c:a libmp3lame -q:a 2")),
        new("wmv", "WMV", Split("-c:v wmv2 -q:v 3 -vf " + Even + " -c:a wmav2 -b:a 192k")),
        new("gif", "GIF", new[] { "-an", "-loop", "0", "-vf",
            "fps=15,scale='min(640,iw)':-2:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse" }),
        Audio[0], // video -> MP3, as in the original
    };

    // Word-processing formats, all handled by wordcraft-cli. PDF is output only: wordcraft cannot read it.
    static readonly Format[] Documents =
    {
        new("pdf", "PDF", CraftConvert, Engine.Wordcraft),
        new("docx", "DOCX", CraftConvert, Engine.Wordcraft),
        new("odt", "ODT", CraftConvert, Engine.Wordcraft),
        new("rtf", "RTF", CraftConvert, Engine.Wordcraft),
        new("md", "MD", CraftConvert, Engine.Wordcraft),
        new("html", "HTML", CraftConvert, Engine.Wordcraft),
        new("txt", "TXT", CraftConvert, Engine.Wordcraft),
    };

    // Spreadsheets through gridcraft-cli. It reads ODS and XLS but cannot write them.
    static readonly Format[] Sheets =
    {
        // PDF of the active sheet. Columns are auto-fitted first: gridcraft substitutes fonts (Aptos ->
        // a wider face), and at the saved widths numbers would print as ####.
        new("pdf", "PDF", new[] { "run", "--in", "{in}", "--cmd", "home.autofitColumnWidth={\"cols\":\"A:XFD\"}", "--out", "{out}", "--quiet" }, Engine.Gridcraft),
        new("xlsx", "XLSX", CraftConvert, Engine.Gridcraft),
        new("csv", "CSV", CraftConvert, Engine.Gridcraft),
        new("tsv", "TSV", CraftConvert, Engine.Gridcraft),
        new("html", "HTML", CraftConvert, Engine.Gridcraft),
    };

    // Presentations through deckcraft-cli. Images show the first slide; GIF plays one frame per slide.
    static readonly Format[] Slides =
    {
        new("pdf", "PDF", CraftConvert, Engine.Deckcraft),
        new("png", "PNG", CraftConvert, Engine.Deckcraft),
        new("jpg", "JPG", CraftConvert, Engine.Deckcraft),
        new("gif", "GIF", CraftConvert, Engine.Deckcraft),
        new("md", "MD", CraftConvert, Engine.Deckcraft),
        new("txt", "TXT", CraftConvert, Engine.Deckcraft),
    };

    // pdfcraft step scripts; doc 1 is the file doc_open loaded.
    const string Open = "{\"tool\":\"doc_open\",\"args\":{\"path\":{in}}}";
    internal static string[] PdfSteps(string steps) => new[] { "[" + Open + "," + steps + "]" };
    internal static string[] PdfPages(string format, double dpi = 150) =>
        PdfSteps("{\"tool\":\"doc_export_images\",\"args\":{\"doc\":1,\"folder\":{out},\"format\":\"" + format + "\",\"dpi\":" + dpi + "}}");
    static readonly string[] PdfOffice = PdfSteps("{\"tool\":\"doc_export_office\",\"args\":{\"doc\":1,\"path\":{out}}}");

    static readonly Format[] Pdf =
    {
        // Word's PDF Reflow keeps tables, runs and coloured cells; pdfcraft's export loses some, so it is the fallback.
        new("docx", "DOCX", Array.Empty<string>(), Engine.Word),
        new("docx", "DOCX", PdfOffice, Engine.Pdfcraft),
        new("png", "PNG", PdfPages("png"), Engine.Pdfcraft, ToFolder: true),
        new("jpg", "JPG", PdfPages("jpeg"), Engine.Pdfcraft, ToFolder: true),
        new("txt", "TXT", PdfSteps("{\"tool\":\"doc_export_text\",\"args\":{\"doc\":1,\"path\":{out}}}"), Engine.Pdfcraft),
        new("html", "HTML", PdfOffice, Engine.Pdfcraft),
        new("rtf", "RTF", PdfOffice, Engine.Pdfcraft),
    };

    // ---- Tools (the Ctrl wheel). Same file type out, except where noted. ----

    const string IconCompress = "\uE73F", IconMetadata = "\uE8EC", IconVolume = "\uE767", IconCombine = "\uE81E",
        IconRotate = "\uE7AD", IconCutout = "\uE8B3", IconText = "\uE8D2", IconSplit = "\uE8C6", IconZip = "\uE7B8", IconUnzip = "\uE8B7";
    const string Fit2048 = "scale='min(2048,iw)':'min(2048,ih)':force_original_aspect_ratio=decrease";

    // Re-encoding drops metadata and bakes in the EXIF rotation (ffmpeg 9 applies it). Each encoder
    // ignores the other encoders' quality options.
    static readonly Format CompressImage = new("", "Comprimir", Split($"-frames:v 1 -vf {Fit2048} -q:v 5 -quality 75 -compression_level 9"), Icon: IconCompress);
    static readonly Format StripImage = new("", "Metadatos", new[] { "strip" }, Engine.Builtin, IconMetadata);
    static readonly Format StripMedia = new("", "Metadatos", Split("-map 0 -map_metadata -1 -map_chapters -1 -c copy"), Icon: IconMetadata);
    static readonly Format RotateImage = new("", "Rotar", Split("-frames:v 1 -vf transpose=clock -q:v 2 -quality 90 -compression_level 9"), Icon: IconRotate);
    static readonly Format RemoveBackground = new("png", "Sin fondo", new[] { "removebg" }, Engine.Builtin, IconCutout);

    // Picture editors (EditorWindow); Args[0] names the editor.
    static readonly Format[] Editors =
    {
        new("", "Recortar", new[] { "crop" }, Engine.Editor, ""),
        new("", "Anotar", new[] { "annotate" }, Engine.Editor, ""),
        new("", "Ocultar", new[] { "redact" }, Engine.Editor, ""),
        new("", "Ajustes", new[] { "adjust" }, Engine.Editor, ""),
        new("", "Fondo", new[] { "frame" }, Engine.Editor, ""),
    };

    static readonly Format[] JpegPngTools = Editors.Concat(new[] { RemoveBackground, RotateImage, CompressImage, StripImage }).ToArray();
    static readonly Format[] WebpTools = Editors.Concat(new[] { RemoveBackground, RotateImage, CompressImage }).ToArray();
    static readonly Format[] BitmapTools = Editors.Concat(new[] { RemoveBackground, RotateImage }).ToArray();
    static readonly Format[] HeifTools = Editors.Append(RemoveBackground).ToArray(); // ffmpeg reads HEIC/AVIF stills but cannot write HEIC
    static readonly Format[] AudioTools =
    {
        // loudnorm resamples to 192 kHz internally; bring it back to 48 kHz.
        new("", "Normalizar", Split("-vn -af loudnorm=I=-16:TP=-1.5:LRA=11 -ar 48000 -q:a 2"), Icon: IconVolume),
        StripMedia,
    };
    static readonly Format[] VideoTools =
    {
        new("mp4", "Comprimir", Split("-c:v libx264 -crf 28 -preset medium -pix_fmt yuv420p -vf scale='min(1920,iw)':'min(1920,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2 -c:a aac -b:a 128k -movflags +faststart"), Icon: IconCompress),
        StripMedia,
        new("mp4", "Rotar", Split("-vf transpose=clock," + Even + " -c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart"), Icon: IconRotate),
    };
    static readonly Format[] PdfTools =
    {
        new("", "Rotar", PdfSteps("{\"tool\":\"page_rotate\",\"args\":{\"doc\":1,\"degrees\":90}},{\"tool\":\"doc_save\",\"args\":{\"doc\":1,\"path\":{out}}}"), Engine.Pdfcraft, IconRotate),
        new("", "Dividir", PdfSteps("{\"tool\":\"doc_split\",\"args\":{\"doc\":1,\"every\":1,\"out_dir\":{out}}}"), Engine.Pdfcraft, IconSplit, ToFolder: true),
        new("", "Texto (OCR)", new[] { "ocr" }, Engine.Builtin, IconText),
        new("", "Comprimir", PdfSteps("{\"tool\":\"doc_reduce\",\"args\":{\"doc\":1,\"path\":{out}}}"), Engine.Pdfcraft, IconCompress),
        new("", "Metadatos", PdfSteps("{\"tool\":\"doc_remove_hidden\",\"args\":{\"doc\":1,\"categories\":[\"metadata\",\"private-data\"]}},{\"tool\":\"doc_save\",\"args\":{\"doc\":1,\"path\":{out}}}"), Engine.Pdfcraft, IconMetadata),
        new("pdf", "Unir", new[] { "[{\"tool\":\"doc_combine\",\"args\":{\"paths\":{in},\"out\":{out}}}]" }, Engine.Pdfcraft, IconCombine, Combines: true, MinFiles: 2),
    };

    // Any file or folder.
    static readonly Format[] AnyTools = { new("zip", "ZIP", new[] { "zip" }, Engine.Builtin, IconZip, Combines: true) };
    static readonly Format[] ZipTools = { new("", "Extraer", new[] { "unzip" }, Engine.Builtin, IconUnzip) };

    // ---- Actions (the Alt wheel). Args[0] names the action for Actions.Run; they apply to any file. ----

    static readonly Format[] FileActions =
    {
        new("", "Compartir", new[] { "share" }, Engine.Action, "\uE72D"),
        new("", "Copiar", new[] { "copy" }, Engine.Action, "\uE8C8"),
        new("", "Mover", new[] { "move" }, Engine.Action, "\uE8DE"),
        new("", "Ruta", new[] { "path" }, Engine.Action, "\uE71B"),
        new("", "Terminal", new[] { "terminal" }, Engine.Action, "\uE756"),
        new("", "Mostrar", new[] { "reveal" }, Engine.Action, "\uE838"),
    };

    public static IReadOnlyList<Format> ActionsFor(IReadOnlyCollection<string> paths) => paths.Count == 0 ? Array.Empty<Format>() : FileActions;

    static readonly Dictionary<string, Format[]> ByInput = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Format[]> ToolsByInput = new(StringComparer.OrdinalIgnoreCase);

    static Catalog()
    {
        foreach (var e in "jpg jpeg png webp heic heif tif tiff avif bmp".Split(' ')) ByInput[e] = Images;
        foreach (var e in "mp3 m4a aac wav flac ogg opus aif aiff wma".Split(' ')) ByInput[e] = Audio;
        foreach (var e in "mp4 m4v mov mkv webm avi wmv gif".Split(' ')) ByInput[e] = Video;
        foreach (var e in "docx doc odt rtf htm html md markdown tex txt".Split(' ')) ByInput[e] = Documents;
        foreach (var e in "xlsx xls ods csv tsv".Split(' ')) ByInput[e] = Sheets;
        foreach (var e in "pptx ppsx potx".Split(' ')) ByInput[e] = Slides;
        ByInput["pdf"] = Pdf;

        foreach (var e in "jpg jpeg png".Split(' ')) ToolsByInput[e] = JpegPngTools;
        ToolsByInput["webp"] = WebpTools;
        foreach (var e in "bmp tif tiff".Split(' ')) ToolsByInput[e] = BitmapTools;
        foreach (var e in "heic heif avif".Split(' ')) ToolsByInput[e] = HeifTools;
        ToolsByInput["zip"] = ZipTools;
        foreach (var e in "mp3 m4a aac wav flac ogg opus aif aiff wma".Split(' ')) ToolsByInput[e] = AudioTools;
        foreach (var e in "mp4 m4v mov mkv webm avi wmv".Split(' ')) ToolsByInput[e] = VideoTools;
        ToolsByInput["pdf"] = PdfTools;
    }

    static string[] Split(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    static string ExtOf(string path) => Path.GetExtension(path).TrimStart('.');

    /// <summary>Targets valid for every file, excluding each file's own format.</summary>
    public static IReadOnlyList<Format> TargetsFor(IReadOnlyCollection<string> paths)
    {
        var own = paths.Select(ExtOf).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // First available engine wins when two produce the same format (Word before pdfcraft).
        return Common(paths, ByInput).Where(f => !own.Contains(f.Ext) && Tools.Has(f) && paths.Count >= f.MinFiles).DistinctBy(f => f.Ext).ToList();
    }

    /// <summary>Tools valid for every file (folders included), then the ones for any file.</summary>
    public static IReadOnlyList<Format> ToolsFor(IReadOnlyCollection<string> paths) =>
        paths.Count == 0 ? Array.Empty<Format>()
            : Common(paths, ToolsByInput).Concat(AnyTools).Where(f => Tools.Has(f) && paths.Count >= f.MinFiles).ToList();

    static IEnumerable<Format> Common(IReadOnlyCollection<string> paths, Dictionary<string, Format[]> table)
    {
        if (paths.Count == 0) return Array.Empty<Format>();
        IEnumerable<Format>? common = null;
        foreach (var p in paths)
        {
            if (!table.TryGetValue(ExtOf(p), out var entries)) return Array.Empty<Format>();
            common = common == null ? entries : common.Intersect(entries);
        }
        return common!;
    }

    static string Normalize(string ext) => ext.ToLowerInvariant() switch
    {
        "jpeg" => "jpg", "tif" => "tiff", "aif" => "aiff", "htm" => "html", "markdown" => "md", _ => ext,
    };
}
