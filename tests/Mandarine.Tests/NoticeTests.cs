namespace Mandarine.Tests;

public class NoticeTests
{
    [Fact]
    public void Done_one_file_names_it_and_its_folder()
    {
        var n = Notice.Done(new[] { @"C:\Users\a\Videos\clip.mkv" });
        Assert.Equal("clip.mkv", n.Title);
        Assert.Equal("en Videos", n.Detail);
        Assert.Equal(@"C:\Users\a\Videos\clip.mkv", n.Reveal);
        Assert.False(n.IsError);
    }

    [Fact]
    public void Done_many_counts_them()
    {
        var n = Notice.Done(new[] { @"C:\x\Fotos\a.jpg", @"C:\x\Fotos\b.jpg", @"C:\x\Fotos\c.jpg" });
        Assert.Equal("3 archivos", n.Title);
        Assert.Equal("en Fotos", n.Detail);
    }

    [Fact]
    public void Done_mixed_folders_uses_the_first()
    {
        var n = Notice.Done(new[] { @"C:\a\Uno\a.png", @"C:\b\Dos\b.png" });
        Assert.Equal("en Uno", n.Detail);
        Assert.Equal(@"C:\a\Uno\a.png", n.Reveal);
    }

    [Fact]
    public void Done_folder_output_names_the_folder()
    {
        var n = Notice.Done(new[] { @"C:\x\Docs\informe partes" });
        Assert.Equal("informe partes", n.Title);
        Assert.Equal("en Docs", n.Detail);
    }

    // Error titles read as Spanish for every kind of entry, not "No se pudo ruta".
    [Fact]
    public void Verbs_fit_each_entry()
    {
        string Verb(string label, IReadOnlyCollection<string> files, Func<Format, bool> pick) =>
            Notice.VerbFor(Catalog.ActionsFor(files).Concat(Catalog.ToolsFor(files)).Concat(Catalog.TargetsFor(files)).First(f => f.Label == label && pick(f)));
        var photo = new[] { "foto.jpg" };
        Assert.Equal("copiar la ruta de", Verb("Ruta", photo, f => f.Engine == Engine.Action));
        Assert.Equal("abrir la terminal en", Verb("Terminal", photo, f => f.Engine == Engine.Action));
        Assert.Equal("aplicar Sin fondo a", Verb("Sin fondo", photo, _ => true));
        Assert.Equal("convertir", Verb("PNG", photo, _ => true));
        Assert.Equal("unir los archivos", Verb("ZIP", photo, _ => true));
        Assert.Equal("No se pudo unir los archivos", Notice.Failed(1, 1, "x", null, "unir los archivos").Title);
        Assert.Equal("No se pudo copiar la ruta", Notice.Failed(1, 1, "x", null, "copiar la ruta de").Title);
        Assert.Equal("No se pudo aplicar Sin fondo a foto.jpg", Notice.Failed(1, 1, "x", "foto.jpg", "aplicar Sin fondo a").Title);
    }

    [Fact]
    public void Failed_single_and_many()
    {
        var one = Notice.Failed(1, 1, "ffmpeg: códec\nmás", "reunion.mov");
        Assert.Equal("No se pudo convertir reunion.mov", one.Title);
        Assert.Equal("ffmpeg: códec", one.Detail);
        Assert.True(one.IsError);
        Assert.Null(one.Reveal);
        Assert.Equal("Fallaron 2 de 5", Notice.Failed(2, 5, "x", null).Title);
        Assert.Equal("No se pudo copiar", Notice.Failed(1, 1, "x", null, "copiar").Title);
        Assert.EndsWith("…", Notice.Failed(1, 1, new string('a', 300), null).Detail);
    }
}
