using NAudio.Wave;

namespace JLVoice;

// Explicit, session-only microphone check. No audio file or transcript is saved.
internal static class AudioPlayback
{
    public static async Task PlayAsync(byte[] wav, CancellationToken token)
    {
        using var stream = new MemoryStream(wav, writable: false);
        using var reader = new WaveFileReader(stream);
        using var output = new WaveOutEvent();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } error) completed.TrySetException(error);
            else completed.TrySetResult();
        };
        token.ThrowIfCancellationRequested();
        output.Init(reader);
        output.Play();
        // Cancellation disposes the owned output immediately, including on application exit.
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(AudioCapture.MaxSeconds + 5), token);
    }
}
