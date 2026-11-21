using System.Diagnostics;
using System.Text;

namespace JLVoice;

internal static class AsrEngine
{
    // One bounded CPU-only worker per dictation. Exit releases all model memory.
    // Audio goes through stdin; it is never written to a temporary audio file.
    public static async Task<string> TranscribeAsync(byte[] wav, Settings settings, CancellationToken token)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "runtime", "JLVoiceAsr.exe");
        var model = Path.Combine(AppContext.BaseDirectory, "models", settings.Model);
        if (!File.Exists(exe) || !File.Exists(model))
            throw new FileNotFoundException("Portable package is incomplete: bundled whisper runtime or multilingual model is missing.");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        foreach (var arg in new[] { "--model", model, "--language", settings.Language, "--threads", settings.Threads.ToString(), "--cpu-only" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start the bundled speech engine.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(wav, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var text = (await output).Trim();
            _ = await errors; // Drain pipe; do not persist audio-derived diagnostic content.
            if (process.ExitCode != 0) throw new IOException($"Speech engine exited with code {process.ExitCode}.");
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("No speech recognized. Try again or explicitly select Czech / English.");
            return text;
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            throw new OperationCanceledException(timeout.Token);
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
        }
    }
}
