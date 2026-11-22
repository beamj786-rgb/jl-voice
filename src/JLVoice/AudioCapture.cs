using NAudio.Wave;

namespace JLVoice;

// No microphone handle exists outside an explicit recording session.
internal sealed class AudioCapture : IDisposable
{
    private readonly WaveInEvent input;
    private readonly MemoryStream pcm = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private bool disposed;
    private float level;
    public float Level => Volatile.Read(ref level);
    private long firstBufferTick;
    // Environment.TickCount64 (~10-16 ms resolution) at: constructor entry (device open begins),
    // StartRecording() return, and delivery of the first non-empty PCM buffer callback (0 = none yet).
    // The first sample of that buffer is roughly BufferMilliseconds earlier; its exact time is not measured.
    public long OpenStartedTick { get; } = Environment.TickCount64;
    public long StartReturnedTick { get; private set; }
    public long FirstBufferTick => Interlocked.Read(ref firstBufferTick);
    public event Action? FirstBuffer;
    public const int BufferMilliseconds = 50;
    public event Action<Exception>? Faulted;
    public const int MaxSeconds = 90;
    private const int MaxBytes = 16000 * 2 * MaxSeconds;
    public AudioCapture(int device)
    {
        input = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = BufferMilliseconds };
        input.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded > 0 && Interlocked.CompareExchange(ref firstBufferTick, Environment.TickCount64, 0) == 0) FirstBuffer?.Invoke();
            Volatile.Write(ref level, PcmLevel.Rms(e.Buffer.AsSpan(0, e.BytesRecorded)));
            lock (gate) pcm.Write(e.Buffer, 0, Math.Min(e.BytesRecorded, MaxBytes - (int)pcm.Length));
        };
        input.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) { stopped.TrySetException(e.Exception); Faulted?.Invoke(e.Exception); }
            else stopped.TrySetResult();
        };
    }
    public void Start() { input.StartRecording(); StartReturnedTick = Environment.TickCount64; }
    public async Task<byte[]> StopAsync(bool discard = false)
    {
        input.StopRecording();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (discard) return [];
        byte[] bytes;
        lock (gate) bytes = pcm.ToArray();
        if (bytes.Length < 16000) throw new InvalidDataException("Recording is too short (minimum 0.5 seconds).");
        try { return EncodePcm(bytes); }
        finally { Array.Clear(bytes); }
    }
    internal static byte[] EncodePcm(byte[] bytes)
    {
        if (bytes.Length < 16000 || bytes.Length > MaxBytes || bytes.Length % 2 != 0)
            throw new InvalidDataException("Expected 0.5 to 90 seconds of mono 16 kHz 16-bit PCM.");
        using var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(wav), new WaveFormat(16000, 16, 1)))
            writer.Write(bytes, 0, bytes.Length);
        return wav.ToArray();
    }
    public void Dispose()
    {
        if (disposed) return;
        input.Dispose();
        Volatile.Write(ref level, 0);
        lock (gate)
        {
            if (pcm.TryGetBuffer(out var buffer)) Array.Clear(buffer.Array!, buffer.Offset, buffer.Count);
            pcm.Dispose(); disposed = true;
        }
    }
}
