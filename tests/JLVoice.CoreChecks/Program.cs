using JLVoice;
using NAudio.Wave;

// CI-only public speech fixture through the exact serializer used by microphone capture.
if (args.Length == 3 && args[0] == "--roundtrip")
{
    using var source = new WaveFileReader(args[1]);
    if (source.WaveFormat.Encoding != WaveFormatEncoding.Pcm || source.WaveFormat.SampleRate != 16000 || source.WaveFormat.Channels != 1 || source.WaveFormat.BitsPerSample != 16)
        throw new InvalidDataException("Fixture must match the capture PCM format.");
    var samples = new byte[checked((int)source.Length)];
    source.ReadExactly(samples);
    File.WriteAllBytes(args[2], AudioCapture.EncodePcm(samples));
    return;
}
if (args.Length != 0) throw new ArgumentException("Expected no arguments or --roundtrip INPUT OUTPUT.");

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
Check(PcmLevel.Rms(new byte[1600]) == 0, "silence has zero energy");
Check(PcmLevel.Rms([0, 128, 0, 128]) == 1, "negative full scale is bounded");
Check(PcmLevel.Rms([0]) == 0, "incomplete PCM sample is ignored");
var sine = new byte[1600];
for (var i = 0; i < sine.Length / 2; ++i) { var n = (short)(Math.Sin(i * 2 * Math.PI * 440 / 16000) * 16384); sine[2*i] = (byte)n; sine[2*i+1] = (byte)(n >> 8); }
Check(Math.Abs(PcmLevel.Rms(sine) - .35355) < .001, "known sine RMS calibrates the live meter");
var capturedPcm = new byte[32000];
for (var i = 0; i < capturedPcm.Length; ++i) capturedPcm[i] = sine[i % sine.Length];
var encoded = AudioCapture.EncodePcm(capturedPcm);
using (var decoded = new WaveFileReader(new MemoryStream(encoded)))
{
    Check(decoded.WaveFormat.Encoding == WaveFormatEncoding.Pcm && decoded.WaveFormat.SampleRate == 16000 && decoded.WaveFormat.BitsPerSample == 16 && decoded.WaveFormat.Channels == 1 && decoded.TotalTime == TimeSpan.FromSeconds(1), "captured audio WAV declares the actual mono 16 kHz PCM duration");
    var roundtrip = new byte[checked((int)decoded.Length)]; decoded.ReadExactly(roundtrip);
    Check(roundtrip.SequenceEqual(capturedPcm), "capture serialization preserves every signed sample and its order");
}
Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4, 4)) + 8 == encoded.Length, "capture WAV finalizes RIFF length before worker dispatch");
foreach (var length in new[] { 15998, 16001, 16000 * 2 * AudioCapture.MaxSeconds + 2 })
{
    try { AudioCapture.EncodePcm(new byte[length]); throw new Exception("Invalid PCM accepted"); }
    catch (InvalidDataException) { }
}
Check(true, "capture serializer rejects short, partial and oversized PCM");
Check(PcmLevel.Display(0) == 0 && PcmLevel.Display(1) == 1 && PcmLevel.Display(.01f) < PcmLevel.Display(.1f), "display follows measured energy and remains bounded");

// Level diagnostics separate capture problems from model problems.
static byte[] Tone(double amplitude, int samples = 16000, bool clip = false)
{
    var pcm = new byte[samples * 2];
    for (var i = 0; i < samples; ++i) { var n = clip ? short.MaxValue : (short)(Math.Sin(i * 2 * Math.PI * 440 / 16000) * amplitude * 32767); pcm[2*i] = (byte)n; pcm[2*i+1] = (byte)(n >> 8); }
    return AudioCapture.EncodePcm(pcm);
}
var loud = AudioStats.Analyze(Tone(.5))!;
Check(Math.Abs(loud.PeakDbfs!.Value - -6.02) < .1 && Math.Abs(loud.RmsDbfs!.Value - -9.03) < .1 && !loud.LowLevel && loud.ClippedFraction == 0 && loud.ActiveFraction == 1 && Math.Abs(loud.Seconds - 1) < 1e-9, "audio stats calibrate peak/RMS dBFS on a known tone");
var quiet = AudioStats.Analyze(Tone(.0005))!;
Check(quiet.LowLevel && quiet.PeakDbfs < -60 && quiet.ActiveFraction == 0, "very quiet capture (public-clip-like amplitude) is flagged low level");
var silent = AudioStats.Analyze(AudioCapture.EncodePcm(new byte[32000]))!;
Check(silent.PeakDbfs is null && silent.RmsDbfs is null && silent.LowLevel && silent.Describe(false).Contains("n/a"), "digital silence reports unknown dBFS (null), not zero");
Check(AudioStats.Analyze(Tone(1, clip: true))!.ClippedFraction == 1, "full-scale clipping is counted");
Check(AudioStats.Analyze([1, 2, 3]) is null && AudioStats.Analyze(new byte[64]) is null, "malformed audio yields no stats");

Check(new Settings().PostRollMs == 0, "post-release recording is off by default");
foreach (var bad in new[] { -1, 501 })
{
    try { (new Settings { PostRollMs = bad }).Validate(); throw new Exception("Invalid post-roll accepted"); }
    catch (InvalidDataException) { }
}
(new Settings { PostRollMs = 250 }).Validate();
Check(true, "experimental post-roll is bounded to 0..500 ms");
Check(CaptureTiming.Since(1000, 0) is null && CaptureTiming.Since(1000, 1320) == 320, "capture timing: missing ticks stay null, not zero");
var timingText = new CaptureTiming(5, 90, null).Describe(false);
Check(timingText.Contains("+5 ms") && timingText.Contains("+90 ms") && timingText.Contains("delivered +n/a"), "capture timing names delivery, not first-sample time, and keeps unknowns unknown");

// Worker result contract: only "ok" with text is ever insertable.
static string Metrics(string verdict, string extra = "") => "noise\nJLVOICE_METRICS {\"load_ms\":1,\"inference_ms\":" + (verdict == "no_speech" ? "null" : "5") +
    ",\"audio_seconds\":3.5,\"detected_language\":" + (verdict == "no_speech" ? "null" : "\"cs\"") + ",\"vad_speech_seconds\":2.1,\"verdict\":\"" + verdict + "\",\"reasons\":[" + extra + "]}\r\n";
var okResult = AsrEngine.ParseWorkerOutput("Dobrý den.\n", Metrics("ok"), "m.bin", "cs", 10);
Check(okResult.Insertable && okResult.Text == "Dobrý den." && okResult.Language == "cs" && okResult.InferenceMs == 5 && okResult.SpeechSeconds == 2.1, "ok worker result with text is insertable");
var silent = AsrEngine.ParseWorkerOutput("\n", Metrics("no_speech", "\"vad_no_speech\""), "m.bin", "cs", 10);
Check(!silent.Insertable && silent.Text == "" && silent.InferenceMs is null && silent.Language == "cs" && silent.Reasons!.SequenceEqual(new[] { "vad_no_speech" }), "no-speech result inserts nothing and keeps unknown inference null");
Check(!AsrEngine.ParseWorkerOutput("Titulky vytvořil JohnyX.", Metrics("no_speech"), "m.bin", "cs", 1).Insertable, "text under a no_speech verdict is discarded");
var loop = AsrEngine.ParseWorkerOutput("vytvořil vytvořil vytvořil", Metrics("suspicious", "\"word_loop\""), "m.bin", "cs", 1);
Check(!loop.Insertable && loop.Text.Length > 0 && loop.Reasons!.Contains("word_loop"), "suspicious result is kept for review but not insertable");
Check(AsrEngine.ParseWorkerOutput("  \n", Metrics("ok"), "m.bin", "cs", 1).Verdict == "no_speech", "empty transcript is never inserted even if the worker says ok");
foreach (var bad in new[] { Metrics("maybe"), "no metrics here" })
{
    try { AsrEngine.ParseWorkerOutput("x", bad, "m.bin", "cs", 1); throw new Exception("Bad worker output accepted"); }
    catch (InvalidDataException) { }
}
Check(true, "unknown verdicts and missing metrics are rejected");
Check(new AsrEngine.Result("Cloud text", "NVIDIA", "cs", 2, null, 100).Insertable, "cloud results keep the existing insert behavior (verdict ok by default)");

var latch = new HoldKeyState();
Check(latch.Change(true, true) == HoldAction.Start && latch.Change(true, true) == HoldAction.None && latch.Change(false, true) == HoldAction.Stop && latch.Change(false, true) == HoldAction.None, "a hold starts and stops once despite repeat and duplicate release");
Check(latch.Change(true, false) == HoldAction.None && latch.Change(true, true) == HoldAction.None && latch.Change(false, true) == HoldAction.None, "a busy key press cannot start on repeat or stop another recording");
Check(latch.Change(true, true) == HoldAction.Start, "a fresh hold can start");
var pendingVersion = latch.Version;
latch.Disarm();
Check(latch.Version != pendingVersion, "cancellation invalidates input intents already queued for the UI");
Check(latch.Change(true, true) == HoldAction.None && latch.Change(false, true) == HoldAction.None && latch.Change(true, true) == HoldAction.Start, "cancelled hold stays disarmed until release");
latch.Reset();
Check(latch.Change(false, true) == HoldAction.None, "rebinding clears the held state");
new Settings().Validate();
Check(new Settings().Key == DictationKeys.CapsLock && new Settings().PushToTalk, "fresh settings default to holding Caps Lock");
foreach (var option in DictationKeys.Options) (new Settings { Key = option.Code }).Validate();
(new Settings { Key = 0x78, PushToTalk = false }).Validate();
try { (new Settings { Key = DictationKeys.CapsLock, PushToTalk = false }).Validate(); throw new Exception("Caps Lock toggle accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: Caps Lock is hold-only; legacy F9 toggle settings remain valid"); }

foreach (var code in new[] { 4551, 1260, 577 })
{
    var error = new AsrStartException(code, new System.ComponentModel.Win32Exception(code, @"C:\Users\SensitiveName\runtime\JLVoiceAsr.exe blocked"));
    var notice = ErrorNotice.From(error, "cs");
    Check(error.PolicyBlocked && notice.Title.Contains("Windows") && notice.Details.Contains(code.ToString()) && !(notice.Title + notice.Message + notice.Details).Contains("SensitiveName"), $"policy failure {code} has concise localized UI without the profile path");
}
var denied = new AsrStartException(5, new System.ComponentModel.Win32Exception(5));
Check(!denied.PolicyBlocked && !ErrorNotice.From(denied, "en").Message.Contains("unsigned"), "access denied is not misdiagnosed as Smart App Control");
Check(ErrorNotice.From(new IOException(new string('x', 5000)), "cs").Message.Length <= 601, "unexpected technical messages cannot fill the window without bound");

var caps = new CapsHoldState();
Check(caps.Down(0, true) && caps.Phase == DictationPhase.Pending && caps.Up(249, 250) == CapsAction.Toggle && caps.Phase == DictationPhase.Idle, "Caps tap before threshold toggles without recording");
Check(caps.Up(249, 250) == CapsAction.None, "duplicate Caps release is idempotent");
Check(caps.Down(1000, true) && !caps.Down(1100, true) && caps.Due(1249, caps.Version, 250) == CapsAction.None && caps.Due(1250, caps.Version, 250) == CapsAction.Start, "Caps starts once at the monotonic threshold and ignores repeat");
Check(caps.Due(1300, caps.Version, 250) == CapsAction.None && caps.Up(1400, 250) == CapsAction.Stop && caps.Phase == DictationPhase.Transcribing, "Caps long release ends exactly one recording without a toggle");
Check(!caps.Down(1500, false) && caps.Due(1800, caps.Version, 250) == CapsAction.None && caps.Up(1900, 250) == CapsAction.None, "hold during transcription cannot record or toggle");
Check(!caps.Down(2000, false) && caps.Up(2100, 250) == CapsAction.Toggle && caps.Phase == DictationPhase.Transcribing, "short tap during transcription remains a normal Caps toggle");
caps.Complete(); caps.Down(3000, true); var oldCapsVersion = caps.Version; caps.Cancel();
Check(caps.Due(4000, oldCapsVersion, 250) == CapsAction.None && !caps.Down(4100, true) && caps.Up(4200, 250) == CapsAction.None, "session cancellation invalidates timers and disarms until physical release");
caps.Down(5000, true);
Check(caps.Up(5250, 250) == CapsAction.None && caps.Due(5250, caps.Version, 250) == CapsAction.None, "key-up winning the exact-threshold race cannot start a microphone after release");
var rapidTaps = true;
for (var i = 0; i < 100; ++i) rapidTaps &= caps.Down(6000 + i * 100, true) && caps.Up(6001 + i * 100, 250) == CapsAction.Toggle;
Check(rapidTaps, "100 rapid taps each produce exactly one toggle");
caps.Down(20000, true); caps.Cancel(); caps.Up(20020, 250);
Check(caps.Down(21000, true) && caps.Due(21300, caps.Version, 300) == CapsAction.Start, "new hold recovers after failure and respects configured threshold");
caps.Up(21400, 300); caps.Complete();
Check(caps.Phase == DictationPhase.Idle, "successful transcription returns the state machine to idle");
foreach (var threshold in new[] { 99, 1001 })
{
    try { (new Settings { HoldThresholdMs = threshold }).Validate(); throw new Exception("Invalid hold threshold accepted"); }
    catch (InvalidDataException) { }
}
Check(new Settings().HoldThresholdMs == 250, "default Caps hold threshold is 250 ms");
await CloudChecks.RunAsync();
