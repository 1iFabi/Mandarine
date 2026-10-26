using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Mandarine;

/// <summary>What the finish line says: the result and where it went, or what failed. Reveal is what "Abrir carpeta" selects.</summary>
public sealed record Notice(bool IsError, string Title, string Detail, string? Reveal)
{
    /// <summary>One output is named; several are counted. The folder is the first output's (mixed folders are rare).</summary>
    public static Notice Done(IReadOnlyList<string> outputs)
    {
        var first = outputs[0].TrimEnd('\\', '/');
        var title = outputs.Count == 1 ? Path.GetFileName(first) : $"{outputs.Count} archivos";
        return new Notice(false, title, $"en {Path.GetFileName(Path.GetDirectoryName(first))}", outputs[0]);
    }

    /// <summary>What "No se pudo …" says for an entry; a trailing preposition joins the file name, and is dropped without one.</summary>
    public static string VerbFor(Format f) => f.Engine == Engine.Action
        ? f.Args[0] switch
        {
            "share" => "compartir", "copy" => "copiar", "move" => "mover",
            "path" => "copiar la ruta de", "terminal" => "abrir la terminal en", _ => "mostrar",
        }
        : f.Combines ? "unir los archivos"
        : f.Icon != null ? $"aplicar {f.Label} a"
        : "convertir";

    public static Notice Failed(int failed, int total, string detail, string? file, string verb = "convertir")
    {
        var title = total == 1
            ? file != null ? $"No se pudo {verb} {file}" : $"No se pudo {Regex.Replace(verb, " (a|de|en)$", "")}"
            : $"Fallaron {failed} de {total}";
        var line = detail.Split('\n')[0].Trim();
        return new Notice(true, title, line.Length > 120 ? line[..119] + "…" : line, null);
    }
}
