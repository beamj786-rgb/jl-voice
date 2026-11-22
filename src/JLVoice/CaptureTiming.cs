namespace JLVoice;

// Milliseconds after the physical hold press (low-level hook time, Environment.TickCount64,
// ~10-16 ms resolution). FirstBufferMs is when the first PCM buffer was DELIVERED; that buffer's
// first sample is roughly one buffer length earlier and is not measured exactly. Missing = null.
internal sealed record CaptureTiming(double? OpenStartedMs, double? StartReturnedMs, double? FirstBufferMs)
{
    internal static CaptureTiming From(long pressTick, AudioCapture capture) => new(
        Since(pressTick, capture.OpenStartedTick), Since(pressTick, capture.StartReturnedTick), Since(pressTick, capture.FirstBufferTick));
    internal static double? Since(long pressTick, long tick) => tick > 0 ? tick - pressTick : null;
    internal string Describe(bool czech)
    {
        static string F(double? v) => v is { } ms ? $"{ms:F0} ms" : "n/a";
        return czech
            ? $"Mikrofon po stisku: otevírání +{F(OpenStartedMs)}, StartRecording +{F(StartReturnedMs)}, první PCM buffer doručen +{F(FirstBufferMs)} (jeho začátek přibližně o {AudioCapture.BufferMilliseconds} ms dřív; řeč před tím se nenahrála)"
            : $"Microphone after press: opening +{F(OpenStartedMs)}, StartRecording +{F(StartReturnedMs)}, first PCM buffer delivered +{F(FirstBufferMs)} (its start ~{AudioCapture.BufferMilliseconds} ms earlier; earlier speech was not captured)";
    }
}
