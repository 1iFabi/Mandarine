using System.IO;
using Microsoft.Win32;

namespace Mandarine.Tests;

// Shares the one practice photo with GuideWindowTests, so the two never run at the same time.
[Collection("Practice photo")]
public class PracticeTests
{
    static void OnSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void EnsurePhoto_creates_and_recreates() => OnSta(() =>
    {
        var photo = Practice.EnsurePhoto();
        Assert.True(new FileInfo(photo).Length > 10_000);
        File.Delete(photo);
        Assert.Equal(photo, Practice.EnsurePhoto());
        Assert.True(File.Exists(photo));
    });

    [Fact]
    public void Involves_finds_the_photo_among_others()
    {
        Assert.True(Practice.Involves(new[] { @"C:\x\mio.png", Path.Combine(Practice.Folder, "MANDARINA.JPG") }));
        Assert.False(Practice.Involves(new[] { @"C:\x\mio.png" }));
    }

    [Fact]
    public void StepFor_maps_wheels()
    {
        var photo = new[] { "foto.jpg" };
        Assert.Equal(2, Practice.StepFor(Catalog.ActionsFor(photo).First(f => f.Label == "Copiar")));
        Assert.Equal(1, Practice.StepFor(Catalog.ToolsFor(photo).First(f => f.Label == "Recortar")));
        Assert.Equal(0, Practice.StepFor(Catalog.TargetsFor(photo).First(f => f.Label == "PNG")));
    }

    [Fact]
    public void Seen_is_stored_in_registry()
    {
        var name = $@"Software\Mandarine\Test-{Guid.NewGuid():N}";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(name);
            Assert.False(Practice.IsSeen(key));
            Practice.MarkSeen(key);
            Assert.True(Practice.IsSeen(key));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(name, false); }
    }
}
