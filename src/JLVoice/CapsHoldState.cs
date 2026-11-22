namespace JLVoice;

internal enum DictationPhase { Idle, Pending, Recording, Transcribing }
internal enum CapsAction { None, Start, Stop, Toggle }

// No clock, device, UI, or native calls. The caller supplies monotonic milliseconds.
internal sealed class CapsHoldState
{
    internal DictationPhase Phase { get; private set; }
    internal int Version { get; private set; }
    internal long DownAt { get; private set; }
    private bool held, allowTap;
    internal bool Down(long now, bool available)
    {
        if (held) return false;
        held = true; allowTap = true; DownAt = now; ++Version;
        if (available) Phase = DictationPhase.Pending;
        return available;
    }
    internal CapsAction Due(long now, int version, int threshold)
    {
        if (!held || version != Version || Phase != DictationPhase.Pending || now - DownAt < threshold) return CapsAction.None;
        Phase = DictationPhase.Recording;
        return CapsAction.Start;
    }
    internal CapsAction Up(long now, int threshold)
    {
        if (!held) return CapsAction.None;
        held = false;
        if (Phase == DictationPhase.Recording) { Phase = DictationPhase.Transcribing; return CapsAction.Stop; }
        var pending = Phase == DictationPhase.Pending;
        if (pending) Phase = DictationPhase.Idle;
        // A delayed UI timer must never start a microphone after physical key-up.
        return now - DownAt < threshold && allowTap ? CapsAction.Toggle : CapsAction.None;
    }
    internal void Cancel() { ++Version; allowTap = false; Phase = DictationPhase.Idle; }
    internal void Complete() { Phase = DictationPhase.Idle; }
    internal void Reset() { held = false; Cancel(); }
}
