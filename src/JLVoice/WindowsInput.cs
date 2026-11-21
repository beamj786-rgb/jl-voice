using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JLVoice;

internal static class WindowsInput
{
    public const int HotkeyId = 1;
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct GuiInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    public readonly record struct Target(IntPtr Window, IntPtr Control);
    public static Target CaptureTarget()
    {
        var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.Focus == IntPtr.Zero)
            throw new InvalidOperationException("Cannot identify the focused input control. Focus a supported text field and try again.");
        return new Target(GetForegroundWindow(), info.Focus);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public Keyboard Key; }

    public static void Insert(string text, Target original)
    {
        if (original.Window == IntPtr.Zero || CaptureTarget() != original)
            throw new InvalidOperationException("Focus changed. Text is retained in the JL Voice window; nothing was inserted.");
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) throw new InvalidOperationException("Release modifier keys. Text is retained in JL Voice.");
        var inputs = text.SelectMany(ch => new[]
        {
            new Input { Type = 1, Key = new Keyboard { Scan = ch, Flags = 4 } },
            new Input { Type = 1, Key = new Keyboard { Scan = ch, Flags = 4 | 2 } }
        }).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Insertion was blocked or partial. Check the target field before retrying; elevated apps may reject input.");
    }

    public delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
}
