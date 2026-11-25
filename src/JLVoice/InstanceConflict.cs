using System.ComponentModel;
using System.Diagnostics;

namespace JLVoice;

// A second launch (for example a freshly unpacked test ZIP while the old build is still in the tray)
// must not exit silently: the user would keep testing the old build without knowing it.
internal static class InstanceConflict
{
    internal sealed record Other(int ProcessId, string? Directory, BuildIdentity? Build);

    // Another JLVoice.exe of the same user. Folder and BUILD-INFO.json are read only when Windows allows it.
    internal static IReadOnlyList<Other> FindOthers()
    {
        var result = new List<Other>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "JLVoice.exe")))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                string? directory = null; BuildIdentity? build = null;
                try { directory = Path.GetDirectoryName(process.MainModule?.FileName); }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { }
                if (directory is not null) build = BuildInfo.Read(directory, "?");
                result.Add(new(process.Id, directory, build));
            }
        }
        return result;
    }

    // Bilingual because settings have not been loaded when this is needed.
    internal static string Describe(IReadOnlyList<Other> others, BuildIdentity own, string? ownDirectory)
    {
        var running = others.Count == 0 ? "(nelze zjistit / unknown)"
            : string.Join("\n", others.Select(o => $"  • {(o.Directory ?? "(složka nedostupná / folder unavailable)")} — {(o.Build is { PackageInfoPresent: true } b ? "build " + b.ShortCommit : "build n/a")}"));
        var same = others.Any(o => o.Build?.Commit is { } c && c == own.Commit);
        return $"JL Voice už běží. Tato kopie se proto ukončila a nic nezměnila.\nJL Voice is already running. This copy exited without changing anything.\n\n" +
               $"Běžící / Running:\n{running}\n\n" +
               $"Tato kopie / This copy: {ownDirectory ?? "(unknown)"} — build {own.ShortCommit}" + (same ? "  (stejný build / same build)" : "") + "\n\n" +
               "Chcete-li zkoušet tuto kopii, ukončete běžící instanci (ikona v oznamovací oblasti → Ukončit JL Voice) a spusťte ji znovu.\n" +
               "To test this copy, exit the running instance (tray icon → Exit JL Voice) and start it again.";
    }
}
