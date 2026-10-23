using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mandarine;

/// <summary>Locates the bundled converters under tools\.</summary>
public static class Tools
{
    static string Bundled(params string[] parts) => Path.Combine(AppContext.BaseDirectory, "tools", Path.Combine(parts));

    // Bundled copy wins; otherwise rely on PATH.
    public static string Ffmpeg => File.Exists(Bundled("ffmpeg", "ffmpeg.exe")) ? Bundled("ffmpeg", "ffmpeg.exe") : "ffmpeg";

    /// <summary>The executable for an engine; the craft tools each live in tools\&lt;name&gt;\&lt;name&gt;-cli.exe.</summary>
    public static string Exe(Engine e)
    {
        if (e == Engine.Ffmpeg) return Ffmpeg;
        var name = e.ToString().ToLowerInvariant();
        return Bundled(name, $"{name}-cli.exe");
    }

    /// <summary>Whether this machine can run a wheel entry.</summary>
    public static bool Has(Format f) => f.Engine != Engine.Builtin ? Has(f.Engine) : f.Args[0] switch
    {
        "ocr" => Ocr.Available,
        "removebg" => Background.Available,
        "sheettxt" or "sheetmd" => Has(Engine.Gridcraft),
        _ => true,
    };

    /// <summary>ffmpeg may come from PATH and Word from Office; the rest only ship bundled.</summary>
    public static bool Has(Engine e) => e switch
    {
        Engine.Ffmpeg or Engine.Builtin or Engine.Action or Engine.Editor => true,
        Engine.Word => Type.GetTypeFromProgID("Word.Application") != null,
        _ => File.Exists(Exe(e)),
    };
}

public static class Converter
{
    /// <summary>Saves beside the original; never overwrites ("name (1).ext", "name (2).ext", ...). Ext "" names a folder.</summary>
    public static string OutputPath(string input, string ext)
    {
        var dir = Path.GetDirectoryName(input)!;
        var name = Path.GetFileNameWithoutExtension(input);
        var suffix = ext.Length > 0 ? "." + ext : "";
        var path = Path.Combine(dir, name + suffix);
        for (var i = 1; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(dir, $"{name} ({i}){suffix}");
        return path;
    }

    /// <summary>Returns the output path; throws with the tool's error text on failure.</summary>
    public static Task<string> ConvertAsync(string input, Format target) => RunAsync(new[] { input }, target);

    /// <summary>Several files into one output (a Combines format), named after the first.</summary>
    public static Task<string> CombineAsync(string[] inputs, Format target) => RunAsync(inputs, target);

    static async Task<string> RunAsync(string[] inputs, Format target)
    {
        var input = inputs[0];
        if (target.ToFolder) return await ToFolder(input, target);
        var output = OutputPath(input, target.Ext.Length > 0 ? target.Ext : Path.GetExtension(input).TrimStart('.'));
        string? scratch = null;
        try
        {
            switch (target.Engine)
            {
                case Engine.Word: await OnSta(() => WordPdfToDocx(input, output)); break;
                case Engine.Builtin: return await Builtin(target.Args[0], inputs, output);
                case Engine.Pdfcraft when target.Ext == "pdf" && target.Combines && !inputs[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase):
                    scratch = Directory.CreateTempSubdirectory("mandarine-").FullName;
                    await RunTool(await PdfReadableImages(inputs, scratch), output, target);
                    break;
                default: await RunTool(inputs, output, target); break;
            }
        }
        catch
        {
            if (File.Exists(output)) File.Delete(output); // don't leave a half-written file
            throw;
        }
        finally
        {
            if (scratch != null) Directory.Delete(scratch, recursive: true);
        }
        return output;
    }

    static async Task<string> Builtin(string op, string[] inputs, string output)
    {
        switch (op)
        {
            case "strip": Metadata.Strip(inputs[0], output); return output;
            case "zip": await Task.Run(() => Archive.Zip(inputs, output)); return output;
            case "unzip":
                var folder = OutputPath(inputs[0], "");
                try { await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(inputs[0], folder)); } // rejects entries escaping the folder
                catch { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); throw; }
                return folder;
            case "ocr": await Ocr.SearchablePdf(inputs[0], output); return output;
            case "removebg": await Background.Remove(inputs[0], output); return output;
            case "sheettxt": await SheetText.Txt(inputs[0], output); return output;
            case "sheetmd": await SheetText.Markdown(inputs[0], output); return output;
            default: throw new ArgumentException(op);
        }
    }

    /// <summary>
    /// pdfcraft reads PNG, JPEG, TIFF, GIF and BMP but ignores EXIF rotation: other formats and rotated
    /// photos go through ffmpeg first (it applies the rotation).
    /// </summary>
    static async Task<string[]> PdfReadableImages(string[] inputs, string scratch)
    {
        var result = new string[inputs.Length];
        for (var i = 0; i < inputs.Length; i++)
        {
            var ext = Path.GetExtension(inputs[i]).ToLowerInvariant();
            var upright = ext is ".jpg" or ".jpeg" ? Metadata.Orientation(inputs[i]) == 1 : ext is ".png" or ".tif" or ".tiff" or ".gif" or ".bmp";
            if (upright) { result[i] = inputs[i]; continue; }
            result[i] = Path.Combine(scratch, $"{i}{(ext is ".jpg" or ".jpeg" ? ".jpg" : ".png")}");
            await RunTool(new[] { inputs[i] }, result[i], new Format("", "", new[] { "-frames:v", "1", "-q:v", "2" }));
        }
        return result;
    }

    internal static async Task RunTool(string[] inputs, string output, Format target)
    {
        var input = inputs[0];
        var psi = new ProcessStartInfo(Tools.Exe(target.Engine))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        string? script = null;
        IEnumerable<string> args;
        switch (target.Engine)
        {
            case Engine.Ffmpeg:
                args = new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-n", "-i", input }.Concat(target.Args).Append(output);
                break;
            case Engine.Pdfcraft:
                script = Path.GetTempFileName();
                var json = Fill(target.Args[0], target.Combines ? JsonSerializer.Serialize(inputs) : JsonSerializer.Serialize(input), output, s => JsonSerializer.Serialize(s));
                await File.WriteAllTextAsync(script, json);
                args = new[] { "run", "--script", script };
                break;
            default:
                args = target.Args.Select(a => Fill(a, input, output));
                break;
        }
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {psi.FileName}.");
            var stdout = p.StandardOutput.ReadToEndAsync(); // drained so a chatty tool can't block on a full pipe
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var outText = await stdout;
            if (p.ExitCode == 0 && (File.Exists(output) || Directory.Exists(output))) return; // a folder for page images
            // doc_reduce writes nothing when it can't make the file smaller.
            if (p.ExitCode == 0 && outText.Contains("\"written\": false"))
                throw new InvalidOperationException("El PDF ya está lo más comprimido posible.");
            throw new InvalidOperationException(err.Trim().Length > 0 ? err.Trim() : $"{Path.GetFileNameWithoutExtension(psi.FileName)} salió con código {p.ExitCode}.");
        }
        finally
        {
            if (script != null) File.Delete(script);
        }
    }

    /// <summary>
    /// {in} becomes input as given; {out} the output, and {out:name} a file inside the output folder, both through quote.
    /// One pass, so a file name that contains "{out}" is left as it is.
    /// </summary>
    public static string Fill(string template, string input, string output, Func<string, string>? quote = null) =>
        Regex.Replace(template, @"\{in\}|\{out(?::([^{}]*))?\}", m => m.Value == "{in}" ? input
            : (quote ?? (s => s))(m.Groups[1].Success ? Path.Combine(output, m.Groups[1].Value) : output));

    /// <summary>For tools that write several files (pages, parts): one result lands beside the input, several in a "name" folder.</summary>
    static async Task<string> ToFolder(string input, Format target)
    {
        var tmp = Directory.CreateTempSubdirectory("mandarine-").FullName;
        try
        {
            await RunTool(new[] { input }, tmp, target);
            var pages = Directory.GetFiles(tmp).OrderBy(f => f.Length).ThenBy(f => f, StringComparer.Ordinal).ToArray(); // page_9 before page_10
            if (pages.Length == 0) throw new InvalidOperationException("No se generó ningún archivo.");
            if (pages.Length == 1)
            {
                var single = OutputPath(input, Path.GetExtension(pages[0]).TrimStart('.'));
                File.Move(pages[0], single);
                return single;
            }
            var folder = OutputPath(input, "");
            Directory.CreateDirectory(folder);
            foreach (var page in pages) File.Move(page, Path.Combine(folder, Path.GetFileName(page)));
            return folder;
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    // A private hidden Word: Quit only ends this instance, never the user's open documents.
    static void WordPdfToDocx(string input, string output)
    {
        dynamic word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0; // wdAlertsNone
            var doc = word.Documents.Open(input, false, true); // no conversion prompt, read-only
            doc.SaveAs2(output, 16); // wdFormatDocumentDefault
            doc.Close(0);
        }
        finally
        {
            word.Quit(0); // never prompt to save: a hidden prompt would hang Word
            Marshal.FinalReleaseComObject(word);
        }
    }

    static Task OnSta(Action action)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); // Office automation is apartment-threaded
        thread.Start();
        return done.Task;
    }
}
