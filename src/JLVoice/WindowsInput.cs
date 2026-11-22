using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace JLVoice;

internal static class WindowsInput
{
    public const int HotkeyId = 1;
    internal static readonly UIntPtr OwnInputTag = new(0x4A4C564F);
    internal static bool IsInjected(IntPtr data) => (Marshal.ReadInt32(data, 8) & 0x10) != 0;
    internal static bool IsOwnInput(IntPtr data) => unchecked((ulong)Marshal.ReadInt64(data, 16)) == OwnInputTag.ToUInt64();
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
    internal static Target TryCaptureTarget()
    {
        var window = GetForegroundWindow();
        var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        return window != IntPtr.Zero && GetGUIThreadInfo(0, ref info) && info.Focus != IntPtr.Zero && window == GetForegroundWindow()
            ? new Target(window, info.Focus) : default;
    }
    public static Target CaptureTarget()
    {
        var target = TryCaptureTarget();
        if (target == default)
            throw new InvalidOperationException("Cannot identify the focused input control. Focus a supported text field and try again.");
        return target;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public Keyboard Key; }

    private static void CheckTarget(Target original)
    {
        if (original.Window == IntPtr.Zero || CaptureTarget() != original)
            throw new InvalidOperationException("Focus changed. Text is retained in the JL Voice window; nothing was inserted.");
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) throw new InvalidOperationException("Release modifier keys. Text is retained in JL Voice.");
    }
    internal static void ToggleCapsLock()
    {
        var inputs = new[]
        {
            new Input { Type = 1, Key = new Keyboard { Key = DictationKeys.CapsLock, Extra = OwnInputTag } },
            new Input { Type = 1, Key = new Keyboard { Key = DictationKeys.CapsLock, Flags = 2, Extra = OwnInputTag } }
        };
        Send(inputs);
    }
    private static void Send(Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Insertion was blocked or partial. Check the target field before retrying; elevated apps may reject input.");
    }
    public static async Task InsertAsync(string text, Target original, CancellationToken token, IntPtr clipboardOwner)
    {
        token.ThrowIfCancellationRequested();
        CheckTarget(original);
        using var transfer = await ClipboardTransfer.PublishAsync(clipboardOwner, text, token);
        var dispatched = false;
        try
        {
            token.ThrowIfCancellationRequested(); CheckTarget(original);
            transfer.CheckUnchanged();
            // A single normal paste avoids modern editors corrupting VK_PACKET streams.
            // No transcript characters are sent as keyboard packets.
            dispatched = true;
            var paste = new[]
            {
                new Input { Type = 1, Key = new Keyboard { Key = 0x11, Extra = OwnInputTag } },
                new Input { Type = 1, Key = new Keyboard { Key = 0x56, Extra = OwnInputTag } },
                new Input { Type = 1, Key = new Keyboard { Key = 0x56, Flags = 2, Extra = OwnInputTag } },
                new Input { Type = 1, Key = new Keyboard { Key = 0x11, Flags = 2, Extra = OwnInputTag } }
            };
            try { Send(paste); }
            catch
            {
                // A partial SendInput must not leave our synthetic Ctrl/V held.
                var release = paste.Skip(2).ToArray();
                SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
                throw;
            }
        }
        finally
        {
            // The destination reads the clipboard asynchronously. This grace period is
            // a compatibility bound, not proof of paste completion in an arbitrary app.
            // Once dispatched, cancellation cannot retract a paste already delivered.
            if (dispatched) await Task.Delay(700);
            await transfer.RestoreAsync();
        }
    }

    public delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
}
