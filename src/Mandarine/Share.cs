using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Mandarine;

/// <summary>
/// Windows' share sheet (Nearby Share, Mail, Teams...): the counterpart of Tangerine's AirDrop. A desktop
/// app shows it for one of its windows, so a tiny invisible window hosts it.
/// </summary>
public static class Share
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    static readonly Guid DataTransferManagerId = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    public static void Show(string[] files)
    {
        var host = new Window
        {
            Width = 1, Height = 1, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        host.Show();
        host.Activate();
        var hwnd = new WindowInteropHelper(host).Handle;

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = DataTransferManagerId;
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(hwnd, ref iid));
        manager.DataRequested += async (_, args) =>
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var f in files)
                    items.Add(Directory.Exists(f) ? await StorageFolder.GetFolderFromPathAsync(f) : await StorageFile.GetFileFromPathAsync(f));
                var data = args.Request.Data;
                data.Properties.Title = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} archivos";
                data.SetStorageItems(items);
                data.ShareCompleted += (_, _) => host.Dispatcher.BeginInvoke(host.Close);
            }
            catch (Exception ex)
            {
                args.Request.FailWithDisplayText(ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };
        interop.ShowShareUIForWindow(hwnd);
        // The sheet belongs to the host window; there is no "dismissed" event, so the host goes after a while.
        new DispatcherTimer(TimeSpan.FromMinutes(10), DispatcherPriority.Background, (s, _) => { ((DispatcherTimer)s!).Stop(); host.Close(); }, host.Dispatcher).Start();
    }
}
