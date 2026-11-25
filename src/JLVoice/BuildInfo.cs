using System.Reflection;
using System.Text.Json;

namespace JLVoice;

// Identifies the running build. Packaged builds carry BUILD-INFO.json beside the executable
// (written by scripts/package.ps1); a source/IDE run has none and says so instead of guessing.
internal sealed record BuildIdentity(string Version, string? Commit, string? BuiltUtc, string? PackagedModel, bool PackageInfoPresent)
{
    internal string ShortCommit => Commit is { Length: >= 8 } c ? c[..8] : Commit ?? "n/a";
    internal string Describe() => PackageInfoPresent
        ? $"v{Version} · {ShortCommit}" + (BuiltUtc is { Length: >= 16 } b ? " · " + b[..16].Replace('T', ' ') + " UTC" : "")
        : $"v{Version} · no BUILD-INFO.json (not a packaged build)";
}

internal static class BuildInfo
{
    internal static readonly BuildIdentity Identity = Read(AppContext.BaseDirectory, CurrentVersion());
    internal static string? Commit => Identity.PackageInfoPresent ? Identity.ShortCommit : null;

    private static string CurrentVersion() =>
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    // Pure and bounded so it can be tested and also used on another instance's folder.
    internal static BuildIdentity Read(string directory, string version)
    {
        try
        {
            var path = Path.Combine(directory, "BUILD-INFO.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return new(version, null, null, null, false);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            string? Text(string name) => json.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var commit = Text("source_commit");
            if (commit is not { Length: >= 7 and <= 40 } || !commit.All(Uri.IsHexDigit)) commit = null;
            var built = Text("built_utc");
            if (built is { Length: > 40 }) built = null;
            var model = Text("model");
            if (model is { Length: > 100 }) model = null;
            return new(version, commit, built, model, commit is not null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(version, null, null, null, false); }
    }

    internal static object DiagnosticView() => new
    {
        version = Identity.Version, commit = Identity.Commit, built_utc = Identity.BuiltUtc, packaged_default_model = Identity.PackagedModel,
        package_info_present = Identity.PackageInfoPresent, process_id = Environment.ProcessId,
        executable = Path.GetFileName(Environment.ProcessPath)
    };
}
