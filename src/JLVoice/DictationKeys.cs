namespace JLVoice;

internal sealed record DictationKey(int Code, string Name);

internal static class DictationKeys
{
    internal const int CapsLock = 0x14;
    internal static IReadOnlyList<DictationKey> Options { get; } =
        [new(CapsLock, "Caps Lock"), new(0x78, "F9"), new(0x79, "F10"), new(0x7A, "F11"), new(0x7B, "F12")];
    internal static bool IsSupported(int code) => code == CapsLock || code is >= 0x78 and <= 0x7B;
    internal static string Name(int code) => Options.First(k => k.Code == code).Name;
}

internal enum HoldAction { None, Start, Stop }

// Consume the chosen physical key, but emit only one start/stop pair per hold.
// Cancellation disarms the current hold without allowing auto-repeat to restart it.
internal sealed class HoldKeyState
{
    private bool held, armed;
    internal HoldAction Change(bool down, bool canStart)
    {
        if (down)
        {
            if (held) return HoldAction.None;
            held = true; armed = canStart;
            return armed ? HoldAction.Start : HoldAction.None;
        }
        var stop = held && armed; held = armed = false;
        return stop ? HoldAction.Stop : HoldAction.None;
    }
    internal void Disarm() => armed = false;
    internal void Reset() => held = armed = false;
}
