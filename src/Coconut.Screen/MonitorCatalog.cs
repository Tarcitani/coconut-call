using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Coconut.Screen;

public static unsafe partial class MonitorCatalog
{
    public static IReadOnlyList<ScreenMonitor> List()
    {
        var monitors = new List<ScreenMonitor>();
        var key = GCHandle.Alloc(monitors);
        try
        {
            if (!EnumDisplayMonitors(0, 0, &OnMonitor, GCHandle.ToIntPtr(key))) { throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        }
        finally { key.Free(); }
        return monitors;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnMonitor(nint monitor, nint hdc, nint rect, nint data)
    {
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        if (!GetMonitorInfo(monitor, &info)) { return 1; }
        var list = (List<ScreenMonitor>)GCHandle.FromIntPtr(data).Target!;
        var width = info.Right - info.Left;
        var height = info.Bottom - info.Top;
        var primary = (info.Flags & 1) != 0;
        list.Add(new ScreenMonitor(monitor, $"Monitor {list.Count + 1} · {width} × {height}{(primary ? " (principal)" : string.Empty)}", width, height, primary));
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public int Left, Top, Right, Bottom;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, MonitorInfo* info);
}
