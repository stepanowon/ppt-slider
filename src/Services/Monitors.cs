using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PptSlider;

public record MonitorInfo(int Index, Int32Rect Bounds, bool Primary)
{
    public override string ToString() => $"{L.F("monitor", Index + 1)}{(Primary ? L.T("primaryMonitor") : "")} — {Bounds.Width}×{Bounds.Height}";
}

/// <summary>Win32 monitor enumeration; WPF has no API for it.</summary>
public static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(h, ref mi))
            {
                var r = mi.rcMonitor;
                list.Add(new MonitorInfo(list.Count, new Int32Rect(r.L, r.T, r.R - r.L, r.B - r.T), (mi.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Makes <paramref name="w"/> a borderless full-screen window on monitor <paramref name="m"/>.</summary>
    public static void PlaceFullScreen(Window w, MonitorInfo m)
    {
        w.WindowStyle = WindowStyle.None;
        w.ResizeMode = ResizeMode.NoResize;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        // physical pixels; moving there first makes Maximize pick that monitor (and its DPI)
        w.SourceInitialized += (_, _) =>
            SetWindowPos(new WindowInteropHelper(w).Handle, IntPtr.Zero, m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height, 0x0004 | 0x0010);
        w.Loaded += (_, _) => w.WindowState = WindowState.Maximized;
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
