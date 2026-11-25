using System.Globalization;
using System.Text.Json;

namespace JLVoice;

public sealed record Settings
{
    // New installs dictate English by default (owner decision 2026-11-25). Existing settings.json files
    // always carry an explicit Language, so upgrading never changes a saved choice.
    public const string DefaultLanguage = "en";
    public string Language { get; init; } = DefaultLanguage;
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
    private static string DefaultPath => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

    // Where the active configuration came from; shown in the UI and diagnostics.
    internal enum LoadSource { File, DefaultsNoFile, RestoredFromBackup, DefaultsInvalidFile }
    internal sealed record LoadResult(Settings Settings, LoadSource Source, string? Problem, string? InvalidFileBackup);

    public static Settings Load() => Load(DefaultPath).Settings;
    internal static LoadResult LoadWithStatus() => Load(DefaultPath);

    // Never throws for bad content: an unreadable or invalid file is preserved as
    // settings.invalid-<timestamp>.json before defaults are used, so a later Save cannot destroy it.
    internal static LoadResult Load(string path)
    {
        if (!File.Exists(path)) return new(new Settings(), LoadSource.DefaultsNoFile, null, null);
        string problem;
        try { return new(Read(path), LoadSource.File, null, null); }
        catch (Exception error) when (error is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        { problem = Describe(error); }
        string? preserved = null;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            // Never overwrite an earlier preserved file: each bad load keeps its own copy.
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string copy;
            for (var n = 0; File.Exists(copy = System.IO.Path.Combine(directory, "settings.invalid-" + stamp + (n == 0 ? "" : "-" + n) + ".json")); ++n) { }
            File.Copy(path, copy, overwrite: false); preserved = System.IO.Path.GetFileName(copy);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { problem += " (the invalid file could not be copied aside)"; }
        var backup = path + ".bak";
        if (File.Exists(backup))
        {
            try { return new(Read(backup), LoadSource.RestoredFromBackup, problem, preserved); }
            catch (Exception error) when (error is InvalidDataException or JsonException or IOException or UnauthorizedAccessException) { }
        }
        return new(new Settings(), LoadSource.DefaultsInvalidFile, problem, preserved);
    }

    private static Settings Read(string path)
    {
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings.");
        s.Validate(); return s;
    }
    private static string Describe(Exception error) => error is JsonException ? "settings.json is not valid JSON or has a value of the wrong type" : error.Message;

    internal void Validate()
    {
        var s = this; var bad = new List<string>();
        if (s.Provider is not ("offline" or "nvidia")) bad.Add(nameof(Provider));
        if (s.CloudConsentVersion is < 0 or > 1) bad.Add(nameof(CloudConsentVersion));
        if (s.Provider == "nvidia" && (s.CloudConsentVersion != 1 || s.Language == "auto")) bad.Add("Provider (NVIDIA needs consent and an explicit Czech/English language)");
        if (s.HoldThresholdMs is < 100 or > 1000) bad.Add(nameof(HoldThresholdMs));
        if (s.PostRollMs is < 0 or > 500) bad.Add(nameof(PostRollMs));
        if (s.Language is not ("cs" or "en" or "auto")) bad.Add(nameof(Language));
        if (s.Threads is < 1 or > 2) bad.Add(nameof(Threads));
        if (!DictationKeys.IsSupported(s.Key) || (s.Key == DictationKeys.CapsLock && !s.PushToTalk)) bad.Add(nameof(Key));
        if (s.Model is not ("ggml-base-q5_1.bin" or "ggml-base-q8_0.bin" or "ggml-small-q5_1.bin" or "ggml-large-v3-turbo-q5_0.bin")) bad.Add(nameof(Model));
        if (s.Theme is not ("dark" or "light" or "system")) bad.Add(nameof(Theme));
        if (s.UiLanguage is not ("cs" or "en")) bad.Add(nameof(UiLanguage));
        if (bad.Count != 0) throw new InvalidDataException("Unsupported settings value(s): " + string.Join(", ", bad) + ".");
    }

    public void Save() => Save(DefaultPath);

    // Crash/power-loss safe: the new content is fully written and flushed to a temporary file first,
    // then swapped in atomically (File.Replace keeps the previous file as settings.json.bak). A failure
    // at any point leaves the existing settings.json intact and throws to the caller.
    internal void Save(string path)
    {
        Validate();
        var temp = path + ".new";
        try
        {
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(file, this, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    // Secret-free view of what the app is actually running with (diagnostics and UI).
    internal object DiagnosticView(string? microphoneName, LoadSource source) => new
    {
        provider = Provider, language = Language, model = Provider == "offline" ? Model : null, threads = Provider == "offline" ? Threads : (int?)null,
        microphone_index = Microphone, microphone_name = microphoneName, push_to_talk = PushToTalk, key = Key, hold_threshold_ms = HoldThresholdMs,
        post_roll_ms = PostRollMs, cloud_consent = CloudConsentVersion == 1, remember_key = RememberNvidiaKey, ui_language = UiLanguage,
        settings_source = source.ToString()
    };
}
