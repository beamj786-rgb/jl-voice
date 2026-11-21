namespace JLVoice;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--ui-review")
        {
            ApplicationConfiguration.Initialize();
            Application.SetColorMode(SystemColorMode.Dark);
            try
            {
                using var form = new VoiceForm(review: true);
                form.Show(); Application.DoEvents(); form.ExportUiReview(args[1]); form.ExitApplication(); Application.DoEvents();
            }
            catch (Exception e) { Console.Error.WriteLine(e.ToString()); Environment.ExitCode = 1; }
            finally { BrandFonts.Shutdown(); }
            return;
        }
        try { Diagnostics.Configure(args); }
        catch (Exception e) { MessageBox.Show(e.Message, "JL Voice diagnostics"); return; }
        Diagnostics.Mark("process_started");
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);
        using var singleton = new Mutex(true, "Local\\JLVoice.Portable", out var created);
        if (!created) return;
        try { Application.Run(new VoiceForm()); }
        finally { BrandFonts.Shutdown(); }
    }
}
