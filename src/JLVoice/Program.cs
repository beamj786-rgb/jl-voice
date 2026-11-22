using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace JLVoice;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // This small utility does not need a GPU rendering context. Measure CPU rendering
        // on target hardware before changing this policy or adding idle animations.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var review = args.Length == 2 && args[0] == "--ui-review";
        if (!review)
        {
            try { Diagnostics.Configure(args); }
            catch (Exception error) { MessageBox.Show(error.Message, "JL Voice"); return; }
            Diagnostics.Mark("process_started");
        }
        using var singleton = new Mutex(true, "Local\\JLVoice.Portable", out var created);
        if (!created) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Appearance.Initialize();
        var window = new MainWindow(review); app.MainWindow = window;
        app.SessionEnding += (_, _) => { _ = window.ExitAsync(); };
        if (review) window.Loaded += async (_, _) =>
        {
            try { await window.ExportUiReviewAsync(args[1]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { await window.ExitAsync(); }
        };
        app.Run(window);
    }
}
