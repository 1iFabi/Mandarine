using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mandarine;

/// <summary>Spreadsheets as text: an aligned plain-text table or a Markdown table per visible sheet, read through gridcraft.</summary>
public static class SheetText
{
    public static async Task Txt(string input, string output)
    {
        var parts = new List<string>();
        var sheets = await Sheets(input);
        foreach (var name in sheets)
        {
            var table = (await Gridcraft("cat", input, "--sheet", name)).TrimEnd();
            parts.Add(sheets.Count > 1 ? $"{name}\n{new string('=', name.Length)}\n\n{table}" : table);
        }
        await File.WriteAllTextAsync(output, string.Join("\n\n\n", parts).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
    }

    public static async Task Markdown(string input, string output)
    {
        var sheets = await Sheets(input);
        var tables = new List<(string, string)>();
        foreach (var name in sheets) tables.Add((name, await Gridcraft("cat", input, "--sheet", name, "--csv")));
        await File.WriteAllTextAsync(output, ToMarkdown(tables));
    }

    /// <summary>The first row is the header; columns of numbers align right. A heading per sheet when there are several.</summary>
    public static string ToMarkdown(IReadOnlyList<(string Name, string Csv)> sheets)
    {
        var md = new StringBuilder();
        foreach (var (name, csv) in sheets)
        {
            var rows = ParseCsv(csv);
            if (rows.Count == 0) continue;
            var width = rows.Max(r => r.Count);
            foreach (var r in rows) while (r.Count < width) r.Add("");
            if (sheets.Count > 1) md.Append("## ").Append(Cell(name)).Append("\n\n");
            Row(rows[0]);
            md.Append('|').Append(string.Concat(Enumerable.Range(0, width).Select(c =>
                rows.Skip(1).Any(r => r[c].Length > 0) && rows.Skip(1).All(r => r[c].Length == 0 || double.TryParse(r[c], out _)) ? " ---: |" : " --- |"))).Append('\n');
            foreach (var r in rows.Skip(1)) Row(r);
            md.Append('\n');
        }
        return md.ToString().Replace("\n", "\r\n");

        void Row(List<string> r) => md.Append('|').Append(string.Concat(r.Select(v => " " + Cell(v) + " |"))).Append('\n');
        static string Cell(string v) => v.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");
    }

    /// <summary>RFC 4180: quoted fields may hold commas, doubled quotes and line breaks.</summary>
    public static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    /// <summary>Visible sheets with something in them, in workbook order; cat only prints the active one by default.</summary>
    static async Task<List<string>> Sheets(string input)
    {
        using var info = JsonDocument.Parse(await Gridcraft("info", input, "--json"));
        var names = info.RootElement.GetProperty("sheets").EnumerateArray()
            .Where(s => s.GetProperty("visibility").GetString() == "Visible" && s.GetProperty("cells").GetInt32() > 0)
            .Select(s => s.GetProperty("name").GetString()!).ToList();
        if (names.Count == 0) throw new InvalidOperationException("La planilla está vacía.");
        return names;
    }

    static async Task<string> Gridcraft(params string[] args)
    {
        var psi = new ProcessStartInfo(Tools.Exe(Engine.Gridcraft))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {psi.FileName}.");
        var err = p.StandardError.ReadToEndAsync(); // drained alongside stdout so neither pipe can fill up
        var text = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException((await err).Trim() is { Length: > 0 } e ? e : $"gridcraft salió con código {p.ExitCode}.");
        return text;
    }
}
