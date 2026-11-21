namespace JLVoice;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Diagnostics.Configure(args); }
        catch (Exception e) { MessageBox.Show(e.Message, "JL Voice diagnostics"); return; }
        Diagnostics.Mark("process_started");
        ApplicationConfiguration.Initialize();
        using var singleton = new Mutex(true, "Local\\JLVoice.Portable", out var created);
        if (!created) return;
        Application.Run(new VoiceForm());
    }
}
