using System.Diagnostics;
using System.Text.Json;

namespace JLVoice;

// Explicit developer opt-in only. Never includes audio, text, window titles or device names.
internal static class Diagnostics
{
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static string? destination;
    public static bool Enabled => destination is not null;
    public const int ExitProbeMessage = 0x805A;
    public static void Configure(string[] args)
    {
        if (args.Length == 0) return;
        if (args.Length != 2 || args[0] != "--diagnostics") throw new ArgumentException("Usage: JLVoice.exe [--diagnostics NEW_FILE.jsonl]");
        destination = Path.GetFullPath(args[1]);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }
    public static void Mark(string name, object? details = null)
    {
        if (destination is null) return;
        try
        {
            File.AppendAllText(destination, JsonSerializer.Serialize(new { name, elapsed_ms = clock.Elapsed.TotalMilliseconds, details }) + "\n");
        }
        catch (IOException) { destination = null; }
        catch (UnauthorizedAccessException) { destination = null; }
    }
}
