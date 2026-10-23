using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Mandarine;

/// <summary>The editors' right-hand pane: who and what at the top, the options, and the tab naming the result.</summary>
public partial class EditorPane : UserControl
{
    public EditorPane()
    {
        InitializeComponent();
        Logo.Source = WheelWindow.LoadLogo();
    }

    public void Show(string tool, string file, string progress)
    {
        ToolName.Text = tool;
        FileName.Text = file;
        Progress.Text = progress;
        Progress.Visibility = progress.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>What the tab says: the new file's name and, under it, its size and where it lands.</summary>
    public void Output(string path, string? detail)
    {
        OutputName.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        var where = $"junto al original, en {Path.GetFileName(Path.GetDirectoryName(path)) ?? path}";
        OutputMeta.Text = detail is { Length: > 0 } ? $"{detail} · {where}" : char.ToUpper(where[0]) + where[1..];
    }

    /// <summary>A named group of controls, stacked under the previous one.</summary>
    public void Add(string? caption, UIElement control)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        if (caption != null) group.Children.Add(new TextBlock { Text = caption, Style = (Style)FindResource("GroupLabel") });
        group.Children.Add(control);
        Body.Children.Add(group);
    }
}
