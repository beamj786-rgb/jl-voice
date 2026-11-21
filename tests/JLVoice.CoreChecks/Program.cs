using JLVoice;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
Check(PcmLevel.Rms(new byte[1600]) == 0, "silence has zero energy");
Check(PcmLevel.Rms([0, 128, 0, 128]) == 1, "negative full scale is bounded");
Check(PcmLevel.Rms([0]) == 0, "incomplete PCM sample is ignored");
var sine = new byte[1600];
for (var i = 0; i < sine.Length / 2; ++i) { var n = (short)(Math.Sin(i * 2 * Math.PI * 440 / 16000) * 16384); sine[2*i] = (byte)n; sine[2*i+1] = (byte)(n >> 8); }
Check(Math.Abs(PcmLevel.Rms(sine) - .35355) < .001, "known sine RMS calibrates the live meter");
Check(PcmLevel.Display(0) == 0 && PcmLevel.Display(1) == 1 && PcmLevel.Display(.01f) < PcmLevel.Display(.1f), "display follows measured energy and remains bounded");
