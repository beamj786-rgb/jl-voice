using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JLVoice;

internal static class NativeDesktop
{
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    internal static Rect WorkingArea(IntPtr window)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) throw new InvalidOperationException("Cannot identify the monitor working area.");
        return info.Work;
    }
    internal static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();
}

// Direct shell integration; no Windows Forms dependency or hidden message-loop thread.
internal sealed class TrayIcon : IDisposable
{
    internal const int Message = 0x8031;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Data
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint operation, ref Data data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string file, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    private Data data;
    private bool disposed;
    internal TrayIcon(IntPtr window)
    {
        data = new Data { Size = (uint)Marshal.SizeOf<Data>(), Window = window, Id = 1, Callback = Message, Flags = 1 | 2 | 4,
            Icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "assets", "brand", "JLVoice.ico"), 1, 0, 0, 0x10 | 0x40), Tip = "JL Voice", Info = "", Title = "" };
        Add();
    }
    internal void Add() { if (!Shell_NotifyIcon(0, ref data)) throw new InvalidOperationException("Could not create the JL Voice tray icon."); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; Shell_NotifyIcon(2, ref data); if (data.Icon != IntPtr.Zero) DestroyIcon(data.Icon);
    }
}
