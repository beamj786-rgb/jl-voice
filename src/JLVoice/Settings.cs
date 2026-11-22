using System.Text.Json;

namespace JLVoice;

public sealed record Settings
{
    public string Language { get; init; } = "auto";
    public int Microphone { get; init; } = 0;
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
    public string Model { get; init; } = "ggml-base-q5_1.bin";
    public bool PushToTalk { get; init; } = true;
    public int Key { get; init; } = DictationKeys.CapsLock;
    public string Theme { get; init; } = "dark";
    public string UiLanguage { get; init; } = "cs";
    public bool ShowIdleBubble { get; init; } = true;
    private static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(Path)) return new();
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? throw new InvalidDataException("Empty settings.");
        s.Validate(); return s;
    }

    internal void Validate()
    {
        var s = this;
        if (s.Language is not ("cs" or "en" or "auto") || s.Threads is < 1 or > 2 || !DictationKeys.IsSupported(s.Key) || (s.Key == DictationKeys.CapsLock && !s.PushToTalk) ||
            s.Model is not ("ggml-base-q5_1.bin" or "ggml-base-q8_0.bin" or "ggml-small-q5_1.bin") ||
            s.Theme is not ("dark" or "light" or "system") || s.UiLanguage is not ("cs" or "en"))
            throw new InvalidDataException("Unsupported settings. Review settings.json in the portable folder.");
    }

    public void Save()
    {
        Validate();
        var temp = Path + ".new";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path, true);
    }
}
