using System.Text.Json;

namespace JLVoice;

public sealed record Settings
{
    public string Language { get; init; } = "auto";
    public int Microphone { get; init; } = 0;
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
    public string Model { get; init; } = "ggml-base-q5_1.bin";
    public bool PushToTalk { get; init; }
    public int Key { get; init; } = 0x78; // F9; toggle also requires Ctrl+Alt.
    public string Theme { get; init; } = "dark";
    public string UiLanguage { get; init; } = "cs";
    private static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(Path)) return new();
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? throw new InvalidDataException("Empty settings.");
        if (s.Language is not ("cs" or "en" or "auto") || s.Threads is < 1 or > 2 || s.Key is < 0x78 or > 0x7B ||
            s.Model is not ("ggml-base-q5_1.bin" or "ggml-base-q8_0.bin" or "ggml-small-q5_1.bin") ||
            s.Theme is not ("dark" or "light" or "system") || s.UiLanguage is not ("cs" or "en"))
            throw new InvalidDataException("Unsupported settings. Review settings.json in the portable folder.");
        return s;
    }

    public void Save()
    {
        var temp = Path + ".new";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path, true);
    }
}
