using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace JLVoice;

internal static class AsrEngine
{
    internal const string TurboModel = "ggml-large-v3-turbo-q5_0.bin";
    internal const string VadModel = "ggml-silero-v6.2.0.bin";
    // Verdict: "ok" (insert), "no_speech" (insert nothing), "suspicious" (show for manual review, never auto-insert).
    internal sealed record Result(string Text, string Model, string Language, double AudioSeconds, double? InferenceMs, double WorkerWallMs,
        string Verdict = "ok", IReadOnlyList<string>? Reasons = null, double? SpeechSeconds = null, string Backend = "offline", string? Detected = null)
    {
        internal bool Insertable => Verdict == "ok" && !string.IsNullOrWhiteSpace(Text);
    }

    // Parses the worker's single JLVOICE_METRICS line. Never persists raw stderr.
    internal static Result ParseWorkerOutput(string stdout, string stderr, string modelName, string requestedLanguage, double wallMs)
    {
        var metricLine = stderr.Split('\n').Select(line => line.TrimEnd('\r')).LastOrDefault(line => line.StartsWith("JLVOICE_METRICS ", StringComparison.Ordinal))
            ?? throw new InvalidDataException("Speech engine did not report inference metrics.");
        using var metrics = JsonDocument.Parse(metricLine[16..]);
        var root = metrics.RootElement;
        static double? Number(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        var verdict = root.TryGetProperty("verdict", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        if (verdict is not ("ok" or "no_speech" or "suspicious")) throw new InvalidDataException("Speech engine reported an unknown result state.");
        var reasons = root.TryGetProperty("reasons", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
        var text = stdout.Trim();
        // Defense in depth: an empty transcript is never inserted, whatever the worker said.
        if (text.Length == 0) verdict = "no_speech";
        if (verdict == "no_speech") text = "";
        // Language is what was REQUESTED; Detected is what the engine reported (null when it did not run).
        var detected = root.TryGetProperty("detected_language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
        return new Result(text, modelName, requestedLanguage, Number(root, "audio_seconds") ?? throw new InvalidDataException("Speech engine did not report audio length."),
            Number(root, "inference_ms"), wallMs, verdict, reasons, Number(root, "vad_speech_seconds"), "offline", detected);
    }
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
        var vad = Path.Combine(AppContext.BaseDirectory, "models", VadModel);
        if (!File.Exists(exe) || !File.Exists(model) || !File.Exists(vad))
            throw new FileNotFoundException("Portable package is incomplete: bundled whisper runtime, multilingual model or voice activity model is missing.");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        foreach (var arg in new[] { "--model", model, "--language", settings.Language, "--threads", settings.Threads.ToString(), "--cpu-only", "--vad-model", vad })
            start.ArgumentList.Add(arg);
        // Measured on public audio (BENCHMARKS.md): turbo with full 30 s encoder context was ~4x slower;
        // the bounded context kept most of its Czech gain. Other models keep the full context.
        if (settings.Model == TurboModel) start.ArgumentList.Add("--short-context");
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
            var stdout = await output;
            var errorOutput = await errors; // Parse numeric metrics only; never persist raw stderr.
            if (process.ExitCode != 0) throw new IOException($"Speech engine exited with code {process.ExitCode}.");
            var result = ParseWorkerOutput(stdout, errorOutput, settings.Model, settings.Language, wall.Elapsed.TotalMilliseconds);
            Diagnostics.Mark("recognition_completed", new { backend = result.Backend, model = result.Model, requested_language = result.Language, detected_language = result.Detected, audio_seconds = result.AudioSeconds, inference_ms = result.InferenceMs,
                worker_wall_ms = result.WorkerWallMs, verdict = result.Verdict, reasons = result.Reasons, speech_seconds = result.SpeechSeconds });
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
