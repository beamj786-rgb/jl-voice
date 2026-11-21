using System.Buffers.Binary;

namespace JLVoice;

internal static class PcmLevel
{
    public static float Rms(ReadOnlySpan<byte> pcm)
    {
        var count = pcm.Length / 2;
        if (count == 0) return 0;
        double energy = 0;
        for (var i = 0; i < count; ++i)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2)) / 32768.0;
            energy += value * value;
        }
        return (float)Math.Clamp(Math.Sqrt(energy / count), 0, 1);
    }
    public static float Display(float rms) => rms <= .001f ? 0 : (float)Math.Clamp((20 * Math.Log10(rms) + 60) / 60, 0, 1);
}
