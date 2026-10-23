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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _wheel = new WheelWindow();
        _wheel.Converted += (files, format) =>
        {
            // After the drop has returned: the editor's window must not hold up the app the drag came from.
            if (format.Engine == Engine.Editor) { Dispatcher.BeginInvoke(() => _ = EditAll(files, format)); return; }
            if (format.Engine != Engine.Action) { _ = ConvertAll(files, format); return; }
            try { Actions.Run(format.Args[0], files); }
            catch (Exception ex) { Notify(format.Label, ex.Message, Forms.ToolTipIcon.Error); }
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
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Mandarine"))
            _wheel.Theme = key.GetValue("DarkWheel") is 1 ? WheelTheme.Dark : WheelTheme.Light;
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
        Notify("Mandarine está listo", "Arrastra un archivo y, sin soltarlo, toca Shift.");
    }

    async Task ConvertAll(string[] files, Format format)
    {
        // Sequential: same-named files (a.png + a.jpg -> a.webp) would race for the output name.
        var errors = new System.Collections.Generic.List<string>();
        if (format.Combines)
        {
            try { await Converter.CombineAsync(files, format); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        else
            foreach (var f in files)
            {
                try { await Converter.ConvertAsync(f, format); }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
            }
        if (errors.Count == 0)
            Notify("Listo", format.Icon != null
                ? $"{format.Label}: {files.Length} archivo(s)."
                : $"{files.Length} archivo(s) convertido(s) a {format.Label}.");
        else
            Notify($"Fallaron {errors.Count} de {files.Length}", string.Join("\n", errors), Forms.ToolTipIcon.Error);
    }

    async Task EditAll(string[] files, Format format)
    {
        try
        {
            var saved = await Editor.Run(format, files);
            if (saved > 0) Notify("Listo", $"{format.Label}: {saved} archivo(s).");
        }
        catch (Exception ex) { Notify(format.Label, ex.Message, Forms.ToolTipIcon.Error); }
    }

    void Notify(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info) =>
        Dispatcher.Invoke(() => _tray?.ShowBalloonTip(4000, title, text.Length > 250 ? text[..250] + "…" : text, icon));

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        if (_tray != null) _tray.Visible = false;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
