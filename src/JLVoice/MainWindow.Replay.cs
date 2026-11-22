using System.Windows;

namespace JLVoice;

public partial class MainWindow
{
    private byte[]? replayWav;
    private void ForgetReplay()
    {
        if (replayWav is not null) Array.Clear(replayWav);
        replayWav = null;
        if (ReplayButton is not null) ReplayButton.IsEnabled = false;
    }
    private void ForgetReplayClick(object sender, RoutedEventArgs e) => ForgetReplay();
    private void ShowRecognition(AsrEngine.Result result, byte[] wav, CaptureTiming? timing = null)
    {
        var stats = AudioStats.Analyze(wav);
        Diagnostics.Mark("build", new { commit = BuildInfo.Commit });
        Diagnostics.Mark("audio_stats", new { seconds = stats?.Seconds, peak_dbfs = stats?.PeakDbfs, rms_dbfs = stats?.RmsDbfs, clipped = stats?.ClippedFraction, active = stats?.ActiveFraction, low_level = stats?.LowLevel });
        ResultText.Text = result.Text;
        RecognitionMetrics.Text = $"{result.Model} · {result.Language}\n" + T("Nahrávka", "Recording") + $": {result.AudioSeconds:F2} s · " +
            (result.InferenceMs is { } inference ? T("Inference", "Inference") + $": {inference / 1000:F2} s · " : "") +
            (result.InferenceMs is null ? T("Cloudový požadavek", "Cloud request") : T("Celý STT proces", "STT process total")) + $": {result.WorkerWallMs / 1000:F2} s" +
            (stats is null ? "" : "\n" + stats.Describe(locale == "cs")) +
            (timing is null ? "" : "\n" + timing.Describe(locale == "cs")) +
            "\nBuild: " + (BuildInfo.Commit ?? "n/a");
    }
    private void ReplayClick(object sender, RoutedEventArgs e)
    {
        if (!reviewMode && !busy && capture is null && replayWav is not null && !closing && !suspended) activeTask = ReplayAsync();
    }
    private async Task ReplayAsync()
    {
        busy = true; pendingHold.Stop(); capsHold.Cancel(); holdKey.Disarm(); EnterActive();
        using var cts = new CancellationTokenSource(); cancellation = cts;
        try
        {
            SetStatus(T("Přepisuji stejnou nahrávku…", "Transcribing the same recording…"));
            var result = await TranscribeSelectedAsync(replayWav!, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            ShowRecognition(result, replayWav!);
            SetStatus(T("Testovací přepis hotov. Text zůstává pouze zde.", "Test transcription complete. Text remains here only."));
        }
        catch (OperationCanceledException) { SetStatus(T("Test zrušen.", "Test cancelled.")); }
        catch (Exception error) { Notify(error); }
        finally { cancellation = null; busy = false; ExitActive(); }
    }
}
