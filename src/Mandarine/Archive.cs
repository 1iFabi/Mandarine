using System.IO;
using System.IO.Compression;

namespace Mandarine;

public static class Archive
{
    /// <summary>Files and folders into one ZIP; a folder keeps its name as the top level inside.</summary>
    public static void Zip(string[] paths, string output)
    {
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            if (!Directory.Exists(path))
            {
                zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
                continue;
            }
            var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
        }
    }
}
