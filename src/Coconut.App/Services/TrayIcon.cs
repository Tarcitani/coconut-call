using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Coconut.App.Services;

/// <summary>Win32 notification icon, re-added after Explorer restart. Hooks are removed before window destruction.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private static readonly ConcurrentDictionary<nuint, TrayIcon> s_live = new();
    private static long s_nextKey;
    private const uint CallbackMessage = 0x8000 + 42;
    private readonly nint _window, _icon;
    private readonly nuint _key = (nuint)Interlocked.Increment(ref s_nextKey);
    private readonly Action _open, _mute, _exit;
    private readonly Func<bool?> _muted;
    private readonly uint _taskbarCreated;
    private bool _disposed;

    public TrayIcon(nint window, string iconPath, Action open, Action mute, Action exit, Func<bool?> muted)
    {
        _window = window; _open = open; _mute = mute; _exit = exit; _muted = muted;
        _icon = DesktopNative.LoadImage(0, iconPath, 1, 32, 32, 0x10);
        if (_icon == 0) { throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        _taskbarCreated = DesktopNative.RegisterWindowMessage("TaskbarCreated");
        s_live[_key] = this;
        if (!DesktopNative.SetWindowSubclass(window, &OnMessage, _key, _key)) { s_live.TryRemove(_key, out _); DesktopNative.DestroyIcon(_icon); throw new System.ComponentModel.Win32Exception(); }
        if (!Add()) { Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    }

    public void Dispose()
    {
        if (_disposed) { return; } _disposed = true;
        s_live.TryRemove(_key, out _); var data = Data(); DesktopNative.Notify(2, &data);
        DesktopNative.RemoveWindowSubclass(_window, &OnMessage, _key); DesktopNative.DestroyIcon(_icon);
    }

    private DesktopNative.NotifyData Data()
    {
        var data = new DesktopNative.NotifyData { Size = (uint)sizeof(DesktopNative.NotifyData), Window = _window, Id = 1, Flags = 7, Message = CallbackMessage, Icon = _icon };
        "Coconut · chamadas de voz e tela".AsSpan().CopyTo(new Span<char>(data.Tip, 128)); return data;
    }

    private bool Add() { var data = Data(); var added = DesktopNative.Notify(0, &data); if (added) { data.Version = 4; DesktopNative.Notify(4, &data); } return added; }

    private void Menu()
    {
        var menu = DesktopNative.CreatePopupMenu();
        try
        {
            DesktopNative.AppendMenu(menu, 0, 1, "Abrir Coconut");
            var muted = _muted(); DesktopNative.AppendMenu(menu, muted is null ? 1u : 0u, 2, muted == true ? "Ativar microfone" : "Silenciar microfone");
            DesktopNative.AppendMenu(menu, 0x800, 0, null);
            DesktopNative.AppendMenu(menu, 0, 3, "Encerrar Coconut");
            DesktopNative.GetCursorPos(out var point); DesktopNative.SetForegroundWindow(_window);
            var selected = DesktopNative.TrackPopupMenu(menu, 0x182, point.X, point.Y, 0, _window, 0);
            DesktopNative.PostMessage(_window, 0, 0, 0);
            if (selected == 1) { _open(); } else if (selected == 2 && muted is not null) { _mute(); } else if (selected == 3) { _exit(); }
        }
        finally { DesktopNative.DestroyMenu(menu); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint OnMessage(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint key)
    {
        if (s_live.TryGetValue(key, out var tray))
        {
            try
            {
                if (message == tray._taskbarCreated) { tray.Add(); }
                if (message == CallbackMessage)
                {
                    var code = (uint)((nuint)lparam & 0xffff);
                    if (code is 0x205 or 0x7b) { tray.Menu(); }
                    else if (code is 0x202 or 0x203 or 0x400 or 0x401) { tray._open(); }
                    return 0;
                }
            }
#pragma warning disable CA1031 // Exceptions cannot cross a window procedure.
            catch { }
#pragma warning restore CA1031
        }
        return DesktopNative.DefSubclassProc(window, message, wparam, lparam);
    }
}

internal static unsafe partial class DesktopNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        public fixed char Tip[128]; public uint State, StateMask; public fixed char Info[256]; public uint Version;
        public fixed char InfoTitle[64]; public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Notify(uint command, NotifyData* data);
    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint LoadImage(nint instance, string name, uint type, int x, int y, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string name);
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint key);
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);
    [LibraryImport("comctl32.dll")]
    internal static partial nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")]
    internal static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")]
    internal static partial uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint menu);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);
}
