using System.Buffers.Binary;
using System.Globalization;

namespace JLVoice;

// Content-free level metrics for a captured WAV. Never retains or logs samples.
// Missing metrics are null, never zero (silence has no finite dBFS).
internal sealed record AudioStats(double Seconds, double? PeakDbfs, double? RmsDbfs, double ClippedFraction, double ActiveFraction, bool LowLevel)
{
    // Heuristic review aid only; not a qualified acceptance threshold.
    internal const double LowPeakDbfs = -20;
    private const double ActiveDbfs = -45;
    private const int Frame = 320; // 20 ms at 16 kHz

    internal static AudioStats? Analyze(byte[] wav)
    {
        // Locate the PCM16 mono 16 kHz "data" chunk; return null on anything unexpected.
        if (wav.Length < 44 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return null;
        int offset = 12, start = -1, length = 0;
        while (offset + 8 <= wav.Length)
        {
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset + 4, 4));
            if (wav.AsSpan(offset, 4).SequenceEqual("data"u8)) { start = offset + 8; length = (int)Math.Min(size, wav.Length - start); break; }
            offset += 8 + (int)Math.Min(size + (size & 1), int.MaxValue / 2);
        }
        var count = length / 2;
        if (start < 0 || count == 0) return null;
        long peak = 0, clipped = 0; double energy = 0; var activeFrames = 0; var frames = 0; double frameEnergy = 0; var inFrame = 0;
        for (var i = 0; i < count; ++i)
        {
            var value = (int)BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(start + i * 2, 2));
            var magnitude = Math.Abs(value);
            if (magnitude > peak) peak = magnitude;
            if (magnitude >= 32767) clipped++;
            var normalized = value / 32768.0;
            energy += normalized * normalized; frameEnergy += normalized * normalized;
            if (++inFrame == Frame)
            {
                frames++; if (Db(Math.Sqrt(frameEnergy / Frame)) is { } level && level > ActiveDbfs) activeFrames++;
                frameEnergy = 0; inFrame = 0;
            }
        }
        var peakDb = Db(peak / 32768.0); var rmsDb = Db(Math.Sqrt(energy / count));
        return new(count / 16000.0, peakDb, rmsDb, (double)clipped / count, frames == 0 ? 0 : (double)activeFrames / frames,
            peakDb is null || peakDb < LowPeakDbfs);
    }
    private static double? Db(double linear) => linear <= 0 ? null : 20 * Math.Log10(Math.Min(linear, 1));

    internal string Describe(bool czech)
    {
        string F(double? v) => v is { } d ? d.ToString("F1", CultureInfo.InvariantCulture) + " dBFS" : "n/a";
        var text = (czech ? "Zvuk: špička " : "Audio: peak ") + F(PeakDbfs) + (czech ? ", RMS " : ", RMS ") + F(RmsDbfs) +
            (czech ? $", clipping {ClippedFraction:P2}, řeč/aktivita {ActiveFraction:P0}" : $", clipping {ClippedFraction:P2}, active {ActiveFraction:P0}");
        if (LowLevel) text += czech ? "\nUpozornění: velmi tichý vstup; model může zhoršit přesnost. Zkontrolujte vstupní úroveň mikrofonu."
            : "\nWarning: very quiet input; recognition accuracy may suffer. Check the microphone input level.";
        return text;
    }
}
