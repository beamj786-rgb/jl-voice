using System.Runtime.InteropServices;
using System.Text;

namespace JLVoice;

internal static class WindowsCredentialStore
{
    internal const string Target = "JLVoice.NVIDIA.APIKey";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias;
        public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Delete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);

    internal static void Save(string key, string target = Target)
    {
        var bytes = Encoding.Unicode.GetBytes(key);
        if (bytes.Length > 2560) { Array.Clear(bytes); throw new InvalidDataException("API key is too long for Windows credential storage."); }
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var credential = new Credential { Type = 1, TargetName = target, BlobSize = (uint)bytes.Length, Blob = memory, Persist = 2, UserName = "NVIDIA API" };
            if (!Write(ref credential, 0)) throw new IOException("Windows credential storage is unavailable.");
        }
        finally { Array.Clear(bytes); for (var i = 0; i < bytes.Length; ++i) Marshal.WriteByte(memory, i, 0); Marshal.FreeHGlobal(memory); }
    }
    internal static string? Load(string target = Target)
    {
        if (!Read(target, 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new IOException("Windows credential storage is unavailable.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.BlobSize > 2560 || credential.BlobSize % 2 != 0) throw new IOException("Stored credential is invalid.");
            return Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2);
        }
        finally { CredFree(pointer); }
    }
    internal static void Forget(string target = Target)
    {
        if (!Delete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new IOException("Windows could not remove the stored credential.");
    }
}
