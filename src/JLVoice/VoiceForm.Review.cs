using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace JLVoice;

internal sealed partial class VoiceForm
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    internal void ExportUiReview(string directory)
    {
        if (!reviewMode) throw new InvalidOperationException("Explicit UI review required.");
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var issues = new List<string>(); var states = new List<object>();
        void Capture(string name)
        {
            PerformLayout(); Application.DoEvents(); Refresh();
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            void Inspect(Control c)
            {
                if (!c.Visible) return;
                if (c is not Form && c.Parent is { } p && p is not ScrollableControl { AutoScroll: true })
                {
                    if (c.Left < -3 || c.Right > p.ClientSize.Width + 3) issues.Add($"{name}: horizontal clipping: {c.GetType().Name} '{c.Text}' {c.Bounds} / {p.ClientSize}");
                    if (c.Top < -3 || c.Bottom > p.ClientSize.Height + 3) issues.Add($"{name}: vertical clipping: {c.GetType().Name} '{c.Text}' {c.Bounds} / {p.ClientSize}");
                }
                foreach (Control child in c.Controls) Inspect(child);
            }
            Inspect(this); states.Add(new { name, width = ClientSize.Width, height = ClientSize.Height, language = uiLanguage, theme = SelectedTheme, page = currentPage });
        }
        if (!BrandFonts.AllLoaded) issues.Add("Bundled font loading failed.");
        foreach (var light in new[] { false, true })
        {
            theme.SelectedIndex = light ? 1 : 0; interfaceLanguage.SelectedIndex = 0; ClientSize = new Size(640, 420); Navigate(0); Capture(light ? "light-overview" : "dark-overview");
            settingsButton.PerformClick(); ClientSize = new Size(640, 630); Capture(light ? "light-settings" : "dark-settings");
        }
        theme.SelectedIndex = 0; interfaceLanguage.SelectedIndex = 1; ClientSize = new Size(560, 440); Navigate(0); Capture("compact-english-overview");
        Navigate(1); Capture("compact-english-settings"); advancedToggle.PerformClick();
        preferences.AutoScrollPosition = new Point(0, preferences.DisplayRectangle.Height); Capture("compact-english-settings-bottom"); preferences.AutoScrollPosition = Point.Empty;
        language.Focus(); var oldFocus = ActiveControl; var moved = SelectNextControl(language, true, true, true, true);
        if (!moved || !ContainsFocus || ActiveControl == oldFocus) issues.Add("Native keyboard traversal failed.");
        ExportBubbleReview(directory, issues);
        if (capture is not null || recordingTimer.Enabled || result.TextLength != 0) issues.Add("Review unexpectedly activated capture or retained a transcript.");
        // The real close-to-tray path is exercised before the explicit exit in Program.
        Close(); Application.DoEvents();
        if (IsDisposed || Visible) issues.Add("Closing the main window did not hide it to the tray.");
        File.WriteAllText(Path.Combine(directory, "review.json"), JsonSerializer.Serialize(new
        {
            fonts_loaded = BrandFonts.AllLoaded, states, issues,
            limits = new[] { "Native UI review on hosted Windows, not physical Windows 11 DPI/accessibility qualification.",
                "Recording bubbles use synthetic PCM test input. No real microphone or speech was used.",
                "Focus checks cover native show/state/mouse-message transitions; real applications still require acceptance testing." }
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (issues.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
    }
    private void ExportBubbleReview(string directory, List<string> issues)
    {
        using var probe = new Form { Text = "JL Voice UI focus test", ClientSize = new Size(320, 100) };
        var field = new TextBox { Dock = DockStyle.Top }; probe.Controls.Add(field); probe.Show(); probe.Activate(); field.Focus(); Application.DoEvents();
        var original = WindowsInput.CaptureTarget();
        using var bubble = new VoiceBubble(); bubble.ApplyTheme(colors); bubble.PlaceForWindow(probe.Handle); bubble.SetIdle(CurrentShortcut, "cs"); bubble.Show(); Application.DoEvents();
        if (WindowsInput.CaptureTarget() != original) issues.Add("Idle bubble stole foreground focus.");
        if (bubble.AnimationRunning || bubble.TransientRunning) issues.Add("Idle bubble has an active timer.");
        if (SendMessage(bubble.Handle, 0x21, probe.Handle, IntPtr.Zero) != (IntPtr)3) issues.Add("Bubble mouse activation is not disabled.");
        var recordIntents = 0; bubble.RecordRequested += () => ++recordIntents;
        var point = (IntPtr)((bubble.Height / 2 << 16) | 24);
        SendMessage(bubble.Handle, 0x201, (IntPtr)1, point); SendMessage(bubble.Handle, 0x202, IntPtr.Zero, point);
        if (recordIntents != 1 || WindowsInput.CaptureTarget() != original) issues.Add("Bubble record click stole focus or did not emit its intent.");
        bubble.AccessibilityObject.GetChild(0)?.DoDefaultAction();
        if (recordIntents != 2 || WindowsInput.CaptureTarget() != original) issues.Add("Accessible bubble action failed or stole focus.");
        bubble.SetHover(true); Application.DoEvents();
        if (bubble.AnimationRunning || bubble.TransientRunning || WindowsInput.CaptureTarget() != original) issues.Add("Hover activated timers or stole focus.");
        var area = Screen.FromHandle(probe.Handle).WorkingArea;
        if (Math.Abs((bubble.Left + bubble.Width / 2) - (area.Left + area.Width / 2)) > 1 || area.Bottom - bubble.Bottom != (int)Math.Round(18 * bubble.DeviceDpi / 96f)) issues.Add("Bubble is not bottom-centred in the monitor working area.");
        using var board = new Bitmap(760, 490); using var g = Graphics.FromImage(board); g.Clear(colors.Background);
        TextRenderer.DrawText(g, "Bublina diktování", BrandFonts.Get("display", 22), new Point(24, 14), colors.Ink);
        TextRenderer.DrawText(g, "UI TEST · záznamové stavy používají syntetické PCM", BrandFonts.Get("mono", 9), new Point(24, 58), colors.Dim);
        var samples = new byte[1600];
        for (var i = 0; i < samples.Length / 2; ++i)
        {
            var sample = (short)(Math.Sin(i * 2 * Math.PI * 440 / 16000) * 16000); samples[i * 2] = (byte)sample; samples[i * 2 + 1] = (byte)(sample >> 8);
        }
        var measured = PcmLevel.Rms(samples); Array.Clear(samples);
        var names = new[] { "V klidu · mikrofon vypnutý", "Po najetí · zkratka a nastavení", "Nahrávání · ticho", "Nahrávání · naměřená úroveň PCM", "Přepisování · bez falešného procenta", "Dokončeno", "Zrušeno", "Chyba · text zůstává v aplikaci" };
        for (var i = 0; i < names.Length; ++i)
        {
            if (i <= 1) { bubble.SetIdle(CurrentShortcut, "cs"); bubble.SetHover(i == 1); }
            else if (i <= 3) { bubble.SetRecording("cs"); for (var j = 0; j < 9; ++j) bubble.UpdateAudio(i == 2 ? 0 : measured * (j + 1) / 9, TimeSpan.FromSeconds(12)); }
            else if (i == 4) bubble.SetProcessing("cs");
            else if (i <= 6) bubble.SetCompleted("cs", i == 6);
            else bubble.SetError("cs", true);
            Application.DoEvents();
            if (WindowsInput.CaptureTarget() != original) issues.Add($"Bubble state {i} stole focus.");
            using var image = new Bitmap(bubble.Width, bubble.Height); bubble.DrawToBitmap(image, bubble.ClientRectangle);
            var x = 24 + (i % 2) * 376; var y = 96 + (i / 2) * 94;
            TextRenderer.DrawText(g, names[i], BrandFonts.Get("body", 9), new Point(x, y), colors.Dim); g.DrawImageUnscaled(image, x, y + 24);
        }
        board.Save(Path.Combine(directory, "bubble-states-fixture.png"), ImageFormat.Png);
        bubble.SetIdle(CurrentShortcut, "cs");
        if (bubble.AnimationRunning || bubble.TransientRunning || bubble.MeterLevels.Any(v => v != 0)) issues.Add("Returning to idle did not release animation/meter state.");
        bubble.Hide(); probe.Close();
    }
}
