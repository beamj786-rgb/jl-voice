using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace JLVoice;

// A bounded, in-memory clipboard transaction. Never serialize or log saved content.
// Unsupported handle-based formats fail BEFORE EmptyClipboard, rather than losing data.
internal sealed class ClipboardTransfer : IDisposable
{
    private const int MaxBytes = 16 * 1024 * 1024;
    private readonly List<(uint Format, IntPtr Data)> saved = new();
    private readonly IntPtr owner;
    private uint publishedSequence;
    private bool published;
    private ClipboardTransfer(IntPtr owner) => this.owner = owner;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern int CountClipboardFormats();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr bitmap, int size, out BitmapInfo info);
    [DllImport("user32.dll")] private static extern IntPtr CopyImage(IntPtr image, uint type, int width, int height, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr image);
    private static void Free(uint format, IntPtr data)
    {
        if (format == 2) DeleteObject(data); else GlobalFree(data);
    }

    private static IntPtr Allocate(byte[] bytes)
    {
        var memory = GlobalAlloc(2, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) throw new InvalidOperationException("Cannot prepare clipboard data. Text remains in JL Voice.");
        var pointer = GlobalLock(memory);
        if (pointer == IntPtr.Zero) { GlobalFree(memory); throw new InvalidOperationException("Cannot lock clipboard data."); }
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        finally { GlobalUnlock(memory); }
        return memory;
    }
    private void Snapshot()
    {
        var total = 0;
        for (uint format = 0; (format = EnumClipboardFormats(format)) != 0;)
        {
            // Metafile/owner-display/private/GDI formats aren't plain HGLOBAL data.
            if (format is 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8e || format is >= 0x200 and <= 0x3ff || saved.Count >= 64)
                throw new InvalidOperationException("Clipboard contains a format that cannot safely be restored. Original clipboard unchanged; use Copy in JL Voice and paste manually.");
            var source = GetClipboardData(format);
            if (format == 2)
            {
                if (source == IntPtr.Zero || GetObject(source, Marshal.SizeOf<BitmapInfo>(), out var info) == 0)
                    throw new InvalidOperationException("Cannot preserve clipboard bitmap. Original clipboard unchanged.");
                var bitmapBytes = Math.Abs((long)info.Height) * Math.Abs((long)info.WidthBytes);
                if (bitmapBytes <= 0 || bitmapBytes > MaxBytes - total) throw new InvalidOperationException("Clipboard bitmap exceeds safe backup size. Original clipboard unchanged.");
                var copy = CopyImage(source, 0, 0, 0, 0x2000);
                if (copy == IntPtr.Zero) throw new InvalidOperationException("Cannot preserve clipboard bitmap. Original clipboard unchanged.");
                saved.Add((format, copy)); total += (int)bitmapBytes;
                continue;
            }
            var size = source == IntPtr.Zero ? 0 : GlobalSize(source).ToUInt64();
            if (size == 0 || size > (ulong)(MaxBytes - total))
                throw new InvalidOperationException("Clipboard is unavailable, unsupported or larger than 16 MB. Original clipboard unchanged; paste manually from JL Voice.");
            var pointer = GlobalLock(source);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Cannot preserve the clipboard. Original clipboard unchanged.");
            var bytes = new byte[(int)size];
            try
            {
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                saved.Add((format, Allocate(bytes))); total += bytes.Length;
            }
            finally { Array.Clear(bytes); GlobalUnlock(source); }
        }
        if (saved.Count != CountClipboardFormats()) throw new InvalidOperationException("Cannot preserve all clipboard formats. Original clipboard unchanged.");
    }
    private static async Task OpenAsync(IntPtr owner, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (OpenClipboard(owner)) return;
            if (clock.ElapsedMilliseconds >= 2000) throw new InvalidOperationException("Clipboard is busy. Text remains in JL Voice; check the target before retrying.");
            await Task.Delay(20, token);
        }
    }
    internal static async Task<ClipboardTransfer> PublishAsync(IntPtr owner, string text, CancellationToken token)
    {
        if (owner == IntPtr.Zero) throw new InvalidOperationException("Clipboard requires an application window.");
        var lease = new ClipboardTransfer(owner);
        await OpenAsync(owner, token);
        var blocks = new List<(uint Format, IntPtr Data)>();
        try
        {
            lease.Snapshot();
            foreach (var name in new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            {
                var format = RegisterClipboardFormat(name);
                if (format == 0) throw new InvalidOperationException("Cannot set clipboard privacy flags.");
                blocks.Add((format, Allocate(new byte[4])));
            }
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            try { blocks.Add((13, Allocate(bytes))); } finally { Array.Clear(bytes); }
            token.ThrowIfCancellationRequested();
            if (!EmptyClipboard()) throw new InvalidOperationException("Cannot prepare clipboard paste.");
            lease.published = true;
            for (var i = 0; i < blocks.Count; ++i)
            {
                var block = blocks[i];
                if (SetClipboardData(block.Format, block.Data) == IntPtr.Zero) throw new InvalidOperationException("Cannot publish clipboard paste.");
                blocks[i] = (block.Format, IntPtr.Zero); // Windows owns it now.
            }
        }
        catch
        {
            try { if (lease.published) lease.RestoreWhileOpen(); }
            finally { lease.Dispose(); }
            throw;
        }
        finally
        {
            foreach (var block in blocks) if (block.Data != IntPtr.Zero) GlobalFree(block.Data);
            CloseClipboard();
        }
        // Windows finalizes the sequence when closing the write. Check ownership on
        // both sides of this read so a foreign copy is never adopted as our write.
        if (GetClipboardOwner() != owner) { lease.Dispose(); throw new InvalidOperationException("Clipboard changed before paste. Text remains in JL Voice."); }
        lease.publishedSequence = GetClipboardSequenceNumber();
        if (GetClipboardOwner() != owner) { lease.Dispose(); throw new InvalidOperationException("Clipboard changed before paste. Text remains in JL Voice."); }
        return lease;
    }
    internal void CheckUnchanged()
    {
        if (GetClipboardOwner() != owner || GetClipboardSequenceNumber() != publishedSequence)
            throw new InvalidOperationException("Clipboard changed before paste. Text remains in JL Voice; nothing was sent.");
    }
    private void RestoreWhileOpen()
    {
        if (!EmptyClipboard()) throw new InvalidOperationException("Cannot restore previous clipboard. Check clipboard contents before retrying.");
        for (var i = 0; i < saved.Count; ++i)
        {
            var block = saved[i];
            if (SetClipboardData(block.Format, block.Data) == IntPtr.Zero)
                throw new InvalidOperationException("Previous clipboard could not be fully restored. Check clipboard contents before retrying.");
            saved[i] = (block.Format, IntPtr.Zero);
        }
        published = false;
    }
    internal async Task RestoreAsync()
    {
        if (!published || GetClipboardOwner() != owner || GetClipboardSequenceNumber() != publishedSequence) return;
        // Cleanup must run even when the dictation cancellation token is cancelled.
        await OpenAsync(owner, CancellationToken.None);
        try { if (GetClipboardOwner() == owner && GetClipboardSequenceNumber() == publishedSequence) RestoreWhileOpen(); }
        finally { CloseClipboard(); }
    }
    public void Dispose()
    {
        foreach (var block in saved) if (block.Data != IntPtr.Zero) Free(block.Format, block.Data);
        saved.Clear();
    }
}
