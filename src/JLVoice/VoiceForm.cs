using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace JLVoice;

internal sealed partial class VoiceForm : Form
{
    private Settings settings;
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "JL Voice", Visible = true };
    private readonly Label status = new() { AutoSize = true, Text = "Ready — Ctrl+Alt+F9" };
    private readonly TextBox result = new() { Multiline = true, ReadOnly = true, Width = 480, Height = 110, ScrollBars = ScrollBars.Vertical };
    private readonly BrandCombo language = new(), microphone = new(), key = new();
    private readonly CheckBox push = new() { Text = "Hold selected key to record (no Ctrl+Alt)", AutoSize = true };
    private readonly BrandButton save = new();
    private readonly System.Windows.Forms.Timer recordingTimer = new() { Interval = 100 };
    private readonly Overlay overlay = new();
    private AudioCapture? capture;
    private bool busy, closing, keyHeld;
    private WindowsInput.Target target;
    private IntPtr hook;
    private WindowsInput.Hook? hookCallback;
    private CancellationTokenSource? cancellation;
    private readonly Stopwatch recordingTime = new();
    private Task? activeTask;

    public VoiceForm(bool review = false)
    {
        reviewMode = review;
        try { settings = Settings.Load(); }
        catch (Exception e) { settings = new(); MessageBox.Show(e.Message, "Settings could not be loaded"); }
        BuildInterface();
        recordingTimer.Tick += (_, _) =>
        {
            overlay.SetStatus(T("Nahrávání", "Recording"), $"{recordingTime.Elapsed.TotalSeconds:F0} s");
            if (recordingTime.Elapsed.TotalSeconds >= AudioCapture.MaxSeconds) QueueToggle();
        };
        FormClosing += OnClosing;
        Shown += (_, _) => { if (!reviewMode) { ConfigureShortcut(); Hide(); Diagnostics.Mark("ready"); } };
    }

    private void SaveSettings()
    {
        if (busy || capture is not null) return;
        try
        {
            var next = settings with { Language = language.SelectedIndex switch { 1 => "cs", 2 => "en", _ => "auto" },
                Microphone = microphone.SelectedIndex, PushToTalk = push.Checked, Key = 0x78 + key.SelectedIndex,
                Model = modelFiles[model.SelectedIndex], Threads = threads.SelectedIndex + 1, Theme = SelectedTheme, UiLanguage = uiLanguage };
            next.Save(); settings = next; var registered = reviewMode || ConfigureShortcut();
            pending.Text = T("Nastavení je uložené.", "Settings saved."); UpdateSummaries(); if (registered) ReadyStatus();
        }
        catch (Exception e) { Notify(e.Message); }
    }

    private bool ConfigureShortcut()
    {
        WindowsInput.UnregisterHotKey(Handle, WindowsInput.HotkeyId);
        if (hook != IntPtr.Zero) { WindowsInput.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        keyHeld = false;
        if (!settings.PushToTalk)
        {
            if (!WindowsInput.RegisterHotKey(Handle, WindowsInput.HotkeyId, 0x4000 | 1 | 2, (uint)settings.Key))
            {
                Notify(T("Zkratka je obsazená. Zvolte jinou klávesu v nastavení.", "Shortcut unavailable. Choose a different key in settings."));
                return false;
            }
        }
        else
        {
            hookCallback = (code, message, data) =>
            {
                if (code >= 0 && Marshal.ReadInt32(data) == settings.Key && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
                {
                    var down = message.ToInt64() is 0x100 or 0x104;
                    var up = message.ToInt64() is 0x101 or 0x105;
                    if (down && !keyHeld) { keyHeld = true; if (!busy && capture is null) BeginInvoke(() => QueueToggle()); }
                    if (up && keyHeld) { keyHeld = false; BeginInvoke(() => { if (capture is not null) QueueToggle(); }); }
                    return (IntPtr)1;
                }
                return WindowsInput.CallNextHookEx(hook, code, message, data);
            };
            hook = WindowsInput.SetWindowsHookEx(13, hookCallback, WindowsInput.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { Notify(T("Klávesu pro držení se nepodařilo zaregistrovat.", "Push-to-talk hook could not be registered.")); return false; }
        }
        UpdateSummaries(); ReadyStatus();
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam == WindowsInput.HotkeyId) QueueToggle();
        base.WndProc(ref m);
    }
    private void QueueToggle() { if (!closing && !busy) activeTask = ToggleAsync(); }
    private async Task ToggleAsync()
    {
        if (closing || busy) return;
        if (capture is null)
        {
            try
            {
                target = WindowsInput.CaptureTarget();
                if (target.Window == Handle) throw new InvalidOperationException(T("Před nahráváním zaměřte cílové textové pole.", "Focus the destination text field before recording."));
                result.Clear(); capture = new AudioCapture(settings.Microphone); capture.Start();
                save.Enabled = false; recordingTime.Restart(); recordingTimer.Start(); overlay.SetStatus(T("Nahrávání", "Recording"), "0 s"); overlay.Show(); SetStatus(T("Nahrávání…", "Recording…"));
                Diagnostics.Mark("recording_started");
            }
            catch (Exception e) { capture?.Dispose(); capture = null; Notify(e.Message); }
            return;
        }
        busy = true; recordingTimer.Stop(); recordingTime.Stop(); overlay.SetStatus(T("Zpracovávám hlas…", "Transcribing…"), "CPU"); SetStatus(T("Zpracovávám hlas…", "Transcribing…"));
        Diagnostics.Mark("recording_stop_requested");
        var audio = capture; capture = null;
        byte[]? wav = null;
        using var cts = new CancellationTokenSource(); cancellation = cts;
        var latency = Stopwatch.StartNew();
        try
        {
            wav = await audio.StopAsync(); audio.Dispose();
            var text = await AsrEngine.TranscribeAsync(wav, settings, cts.Token);
            result.Text = text;
            WindowsInput.Insert(text, target);
            Diagnostics.Mark("text_insertion_sent");
            SetStatus(T("Text byl odeslán do původního pole", "Text sent to the original field") + $" · {latency.Elapsed.TotalSeconds:F1} s");
        }
        catch (OperationCanceledException) { Diagnostics.Mark("cancelled"); SetStatus(T("Diktování bylo zrušeno.", "Dictation cancelled.")); }
        catch (Exception e) { Diagnostics.Mark("failed"); Notify(e.Message); }
        finally
        {
            audio.Dispose(); if (wav is not null) Array.Clear(wav);
            cancellation = null; busy = false; save.Enabled = true; overlay.Hide();
        }
    }
    private async Task CancelAsync()
    {
        cancellation?.Cancel(); recordingTimer.Stop();
        if (capture is not null)
        {
            busy = true;
            var audio = capture; capture = null;
            try { var wav = await audio.StopAsync(); Array.Clear(wav); }
            catch (Exception e) { status.Text = e.Message; }
            finally { audio.Dispose(); overlay.Hide(); save.Enabled = true; busy = false; SetStatus(T("Nahrávání bylo zrušeno.", "Recording cancelled.")); }
        }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        e.Cancel = true; closing = true;
        WindowsInput.UnregisterHotKey(Handle, WindowsInput.HotkeyId);
        if (hook != IntPtr.Zero) WindowsInput.UnhookWindowsHookEx(hook);
        await CancelAsync(); if (activeTask is not null) await activeTask;
        tray.Visible = false; tray.Dispose(); overlay.Dispose(); recordingTimer.Dispose(); DisposeView();
        FormClosing -= OnClosing; Close();
    }
    private void Notify(string message) { SetStatus(message); tray.ShowBalloonTip(5000, "JL Voice", message, ToolTipIcon.Info); }
}

internal sealed class Overlay : Form
{
    private Color border = ColorTranslator.FromHtml("#4b4f57");
    private readonly Label label = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = BrandFonts.Get("body", 11) };
    private readonly Label detail = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = BrandFonts.Get("mono", 10) };
    public Overlay()
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        Size = new Size(350, 56); Padding = new Padding(20, 0, 20, 0);
        label.SetBounds(22, 0, 240, 56); detail.SetBounds(264, 0, 64, 56); Controls.AddRange([label, detail]);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea; Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width < 2 || Height < 2) return;
        using var path = Shape.Round(ClientRectangle, 14); var previous = Region; Region = new Region(path); previous?.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = Shape.Round(new Rectangle(0, 0, Width - 1, Height - 1), 14);
        using var pen = new Pen(border); e.Graphics.DrawPath(pen, path);
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { e.Cancel = true; base.OnFormClosing(e); }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
    public void SetStatus(string text, string extra = "") { label.Text = text; detail.Text = extra; }
    public void ApplyTheme(Palette colors) { BackColor = label.BackColor = detail.BackColor = colors.Surface; label.ForeColor = colors.Ink; detail.ForeColor = colors.Accent; border = colors.Border; Invalidate(); }
}
