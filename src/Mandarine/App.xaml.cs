using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Mandarine;

public partial class App : System.Windows.Application
{
    Forms.NotifyIcon? _tray;
    KeyboardHook? _hook;
    WheelWindow? _wheel;
    NoticeWindow? _notice;
    GuideWindow? _guide;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _wheel = new WheelWindow();
        _notice = new NoticeWindow();
        _wheel.Converted += (files, format) =>
        {
            GuideWindow.Landed(_guide, files);
            // After the drop has returned: the editor's window must not hold up the app the drag came from.
            if (format.Engine == Engine.Editor) { Dispatcher.BeginInvoke(() => _ = EditAll(files, format)); return; }
            if (format.Engine != Engine.Action) { _ = ConvertAll(files, format); return; }
            // After the drop has returned too: a folder picker must not open inside a drag our own guide started.
            Dispatcher.BeginInvoke(() =>
            {
                try { if (Actions.Run(format.Args[0], files)) GuideWindow.Apply(_guide, files, format); }
                catch (Exception ex) { Present(Notice.Failed(1, 1, ex.Message, files.Length == 1 ? Path.GetFileName(files[0]) : null, Notice.VerbFor(format))); }
            });
        };

        _hook = new KeyboardHook();
        // Shift tapped while the left button is held = the user is mid-drag (Tangerine's gesture).
        _hook.ShiftTapped += () => Dispatcher.BeginInvoke(() => { if (KeyboardHook.LeftButtonDown) _wheel.ShowAtCursor(WheelMode.Convert); });
        // Ctrl opens the tools (Tangerine uses Command; Ctrl is the Windows counterpart).
        _hook.CtrlTapped += () => Dispatcher.BeginInvoke(() => { if (KeyboardHook.LeftButtonDown) _wheel.ShowAtCursor(WheelMode.Tools); });
        // Alt opens the actions (Tangerine uses Control, which here already opens the tools).
        _hook.AltTapped += () => Dispatcher.BeginInvoke(() => { if (KeyboardHook.LeftButtonDown) _wheel.ShowAtCursor(WheelMode.Actions); });
        _hook.Escape += () => Dispatcher.BeginInvoke(() => _wheel.HideWheel());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Cómo se usa", null, (_, _) => OpenGuide());
        bool seen;
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Mandarine"))
        {
            _wheel.Theme = key.GetValue("DarkWheel") is 1 ? WheelTheme.Dark : WheelTheme.Light;
            seen = Practice.IsSeen(key);
        }
        var dark = new Forms.ToolStripMenuItem("Rueda oscura") { Checked = _wheel.Theme == WheelTheme.Dark, CheckOnClick = true };
        dark.CheckedChanged += (_, _) =>
        {
            _wheel.Theme = dark.Checked ? WheelTheme.Dark : WheelTheme.Light;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Mandarine");
            key.SetValue("DarkWheel", dark.Checked ? 1 : 0, RegistryValueKind.DWord);
        };
        menu.Items.Add(dark);

        // Per-user Run entry: no admin needed. Rewritten on every start so a moved folder keeps working.
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var command = $"\"{Environment.ProcessPath}\"";
        bool startup;
        using (var key = Registry.CurrentUser.CreateSubKey(run))
        {
            startup = key.GetValue("Mandarine") != null;
            if (startup) key.SetValue("Mandarine", command);
        }
        var autostart = new Forms.ToolStripMenuItem("Iniciar con Windows") { Checked = startup, CheckOnClick = true };
        autostart.CheckedChanged += (_, _) =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(run);
            if (autostart.Checked) key.SetValue("Mandarine", command);
            else key.DeleteValue("Mandarine", false);
        };
        menu.Items.Add(autostart);
        menu.Items.Add("Salir", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon
        {
            // Pick the frame matching the tray size so it stays crisp at any display scale.
            Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "mandarine.ico"), Forms.SystemInformation.SmallIconSize),
            Text = "Mandarine: arrastra y toca Shift, Ctrl o Alt",
            ContextMenuStrip = menu,
            Visible = true,
        };
        if (!seen) OpenGuide();
    }

    /// <summary>One guide at a time: asking again brings the open one forward.</summary>
    void OpenGuide()
    {
        if (_guide != null) { _guide.Activate(); return; }
        _guide = new GuideWindow();
        _guide.Finished += () =>
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Mandarine")) Practice.MarkSeen(key);
            _guide?.Close();
        };
        _guide.Closed += (_, _) => _guide = null;
        _guide.Show();
    }

    async Task ConvertAll(string[] files, Format format)
    {
        // Sequential: same-named files (a.png + a.jpg -> a.webp) would race for the output name.
        var errors = new System.Collections.Generic.List<string>();
        var outputs = new System.Collections.Generic.List<string>();
        if (format.Combines)
        {
            try { outputs.Add(await Converter.CombineAsync(files, format)); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        else
            foreach (var f in files)
            {
                try { outputs.Add(await Converter.ConvertAsync(f, format)); }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
            }
        // A combine is one job however many files went in.
        var total = format.Combines ? 1 : files.Length;
        if (errors.Count == 0) GuideWindow.Apply(_guide, files, format);
        Present(errors.Count == 0
            ? Notice.Done(outputs)
            : Notice.Failed(errors.Count, total, string.Join("\n", errors), files.Length == 1 ? Path.GetFileName(files[0]) : null, Notice.VerbFor(format)));
    }

    async Task EditAll(string[] files, Format format)
    {
        try
        {
            var saved = await Editor.Run(format, files);
            if (saved.Count > 0) { Present(Notice.Done(saved)); GuideWindow.Apply(_guide, files, format); }
        }
        catch (Exception ex) { Present(Notice.Failed(1, 1, ex.Message, files.Length == 1 ? Path.GetFileName(files[0]) : null, Notice.VerbFor(format))); }
    }

    void Present(Notice n) => Dispatcher.Invoke(() => _notice?.Present(n));


    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        if (_tray != null) _tray.Visible = false;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
