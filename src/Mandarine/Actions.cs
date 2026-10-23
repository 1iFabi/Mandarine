using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace Mandarine;

/// <summary>The Alt wheel: things done with files rather than to them. Nothing is written beside the originals.</summary>
public static class Actions
{
    /// <summary>Runs on the UI thread: the clipboard and the folder picker need it.</summary>
    public static void Run(string action, string[] files)
    {
        switch (action)
        {
            case "share":
                Share.Show(files);
                break;
            case "copy":
                var list = new StringCollection();
                list.AddRange(files);
                System.Windows.Clipboard.SetFileDropList(list); // paste in Explorer copies them
                break;
            case "path":
                System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, files));
                break;
            case "move":
                var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Mover a…" };
                if (picker.ShowDialog() != true) return;
                foreach (var f in files)
                {
                    var dest = Path.Combine(picker.FolderName, Path.GetFileName(f));
                    // Windows' own move: progress, cross-drive moves and the "replace or skip" prompt.
                    if (Directory.Exists(f)) FileSystem.MoveDirectory(f, dest, UIOption.AllDialogs, UICancelOption.DoNothing);
                    else FileSystem.MoveFile(f, dest, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                break;
            case "terminal":
                var folder = Directory.Exists(files[0]) ? files[0] : Path.GetDirectoryName(files[0])!;
                // The folder goes in as the working directory, never on wt's command line, where ";" starts a new command.
                try { Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", "." }, WorkingDirectory = folder, UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) // no Windows Terminal: plain PowerShell
                {
                    Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = folder, UseShellExecute = true });
                }
                break;
            case "reveal":
                Process.Start("explorer.exe", $"/select,\"{files[0]}\"");
                break;
            default:
                throw new ArgumentException(action);
        }
    }
}
