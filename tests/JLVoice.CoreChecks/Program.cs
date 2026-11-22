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
