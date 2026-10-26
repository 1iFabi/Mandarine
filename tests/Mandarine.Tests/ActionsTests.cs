using System.IO;

namespace Mandarine.Tests;

public class ActionsTests
{
    // A cancelled "Mover" (picker closed, or every file skipped at Windows' prompt) must not count as done.
    [Fact]
    public void Moved_only_when_a_source_left()
    {
        var dir = Directory.CreateTempSubdirectory("mandarine-move").FullName;
        try
        {
            var a = Path.Combine(dir, "a.txt");
            var b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "x");
            File.WriteAllText(b, "x");
            Assert.False(Actions.Moved(new[] { a, b }));
            File.Delete(a);
            Assert.True(Actions.Moved(new[] { a, b }));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Run_reports_whether_it_happened() =>
        Assert.Equal(typeof(bool), typeof(Actions).GetMethod(nameof(Actions.Run))!.ReturnType);
}
