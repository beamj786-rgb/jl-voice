using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace JLVoice;

internal static class AsrEngine
{
    internal sealed record Result(string Text, string Model, string Language, double AudioSeconds, double InferenceMs, double WorkerWallMs);
    private static Process StartWorker(ProcessStartInfo start)
    {
        try { return Process.Start(start) ?? throw new IOException("Cannot start the bundled speech engine."); }
        catch (Win32Exception error)
        {
            Diagnostics.Mark("recognition_start_failed", new { native_code = error.NativeErrorCode });
            throw new AsrStartException(error.NativeErrorCode, error);
        }
    }

    // One bounded CPU-only worker per dictation. Exit releases all model memory.
    // Audio goes through stdin; it is never written to a temporary audio file.
    public static async Task<Result> TranscribeAsync(byte[] wav, Settings settings, CancellationToken token)
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
        var wall = Stopwatch.StartNew();
        using var process = StartWorker(start);
        Diagnostics.Mark("recognition_started", new { model = settings.Model, language = settings.Language, threads = settings.Threads });
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
            var errorOutput = await errors; // Parse numeric metrics only; never persist raw stderr.
            if (process.ExitCode != 0) throw new IOException($"Speech engine exited with code {process.ExitCode}.");
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("No speech recognized. Try again or explicitly select Czech / English.");
            var metricLine = errorOutput.Split('\n').LastOrDefault(line => line.StartsWith("JLVOICE_METRICS ", StringComparison.Ordinal))
                ?? throw new InvalidDataException("Speech engine did not report inference metrics.");
            using var metrics = JsonDocument.Parse(metricLine[16..]);
            var result = new Result(text, settings.Model, metrics.RootElement.GetProperty("detected_language").GetString()!,
                metrics.RootElement.GetProperty("audio_seconds").GetDouble(), metrics.RootElement.GetProperty("inference_ms").GetDouble(), wall.Elapsed.TotalMilliseconds);
            Diagnostics.Mark("recognition_completed", new { model = result.Model, language = result.Language, audio_seconds = result.AudioSeconds, inference_ms = result.InferenceMs, worker_wall_ms = result.WorkerWallMs });
            return result;
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            throw new OperationCanceledException(timeout.Token);
        }
        catch (Exception error)
        {
            Diagnostics.Mark("recognition_failed", new { model = settings.Model, language = settings.Language, error_type = error.GetType().Name, exit_code = process.HasExited ? (int?)process.ExitCode : null });
            throw;
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
        }
    }
}
