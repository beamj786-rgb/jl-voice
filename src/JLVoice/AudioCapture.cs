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
    public const int MaxSeconds = 90;
    private const int MaxBytes = 16000 * 2 * MaxSeconds;
    public AudioCapture(int device)
    {
        input = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        input.DataAvailable += (_, e) =>
        {
            lock (gate) pcm.Write(e.Buffer, 0, Math.Min(e.BytesRecorded, MaxBytes - (int)pcm.Length));
        };
        input.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) stopped.TrySetException(e.Exception);
            else stopped.TrySetResult();
        };
    }
    public void Start() => input.StartRecording();
    public async Task<byte[]> StopAsync()
    {
        input.StopRecording();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        byte[] bytes;
        lock (gate) bytes = pcm.ToArray();
        if (bytes.Length < 16000) throw new InvalidDataException("Recording is too short (minimum 0.5 seconds).");
        using var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(wav), input.WaveFormat)) writer.Write(bytes, 0, bytes.Length);
        Array.Clear(bytes);
        return wav.ToArray();
    }
    public void Dispose()
    {
        if (disposed) return;
        input.Dispose();
        lock (gate)
        {
            if (pcm.TryGetBuffer(out var buffer)) Array.Clear(buffer.Array!, buffer.Offset, buffer.Count);
            pcm.Dispose(); disposed = true;
        }
    }
}
