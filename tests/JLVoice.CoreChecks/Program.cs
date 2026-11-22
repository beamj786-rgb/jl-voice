using JLVoice;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
Check(PcmLevel.Rms(new byte[1600]) == 0, "silence has zero energy");
Check(PcmLevel.Rms([0, 128, 0, 128]) == 1, "negative full scale is bounded");
Check(PcmLevel.Rms([0]) == 0, "incomplete PCM sample is ignored");
var sine = new byte[1600];
for (var i = 0; i < sine.Length / 2; ++i) { var n = (short)(Math.Sin(i * 2 * Math.PI * 440 / 16000) * 16384); sine[2*i] = (byte)n; sine[2*i+1] = (byte)(n >> 8); }
Check(Math.Abs(PcmLevel.Rms(sine) - .35355) < .001, "known sine RMS calibrates the live meter");
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
