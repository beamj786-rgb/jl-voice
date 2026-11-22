using System.Text.Json;

namespace JLVoice;

// Identifies the packaged build from BUILD-INFO.json beside the executable. Null when absent (source run).
internal static class BuildInfo
{
    internal static readonly string? Commit = Read();
    private static string? Read()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "BUILD-INFO.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var value = json.RootElement.TryGetProperty("source_commit", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return value is { Length: >= 7 } && value.All(Uri.IsHexDigit) ? value[..8] : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
