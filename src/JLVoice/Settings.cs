using System.Text.Json;

namespace JLVoice;

public sealed record Settings
{
    public string Language { get; init; } = "auto";
    public string Provider { get; init; } = "offline";
    public int CloudConsentVersion { get; init; }
    public bool RememberNvidiaKey { get; init; }
    public int Microphone { get; init; } = 0;
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
    public string Model { get; init; } = "ggml-base-q5_1.bin";
    public bool PushToTalk { get; init; } = true;
    public int HoldThresholdMs { get; init; } = 250;
    public int Key { get; init; } = DictationKeys.CapsLock;
    public string Theme { get; init; } = "dark";
    public string UiLanguage { get; init; } = "cs";
    public bool ShowIdleBubble { get; init; } = true;
    // Experimental, settings.json only: keep recording this long after a hold release (0 = stop at release).
    public int PostRollMs { get; init; }
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
        if (s.Provider is not ("offline" or "nvidia") || s.CloudConsentVersion is < 0 or > 1 ||
            (s.Provider == "nvidia" && (s.CloudConsentVersion != 1 || s.Language == "auto")) ||
            s.HoldThresholdMs is < 100 or > 1000 || s.PostRollMs is < 0 or > 500 || s.Language is not ("cs" or "en" or "auto") || s.Threads is < 1 or > 2 || !DictationKeys.IsSupported(s.Key) || (s.Key == DictationKeys.CapsLock && !s.PushToTalk) ||
            s.Model is not ("ggml-base-q5_1.bin" or "ggml-base-q8_0.bin" or "ggml-small-q5_1.bin" or "ggml-large-v3-turbo-q5_0.bin") ||
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
