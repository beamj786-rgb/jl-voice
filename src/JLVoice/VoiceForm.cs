using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace JLVoice;

internal sealed class VoiceForm : Form
{
    private Settings settings;
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "JL Voice", Visible = true };
    private readonly Label status = new() { AutoSize = true, Text = "Ready — Ctrl+Alt+F9" };
    private readonly TextBox result = new() { Multiline = true, ReadOnly = true, Width = 480, Height = 110, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ComboBox microphone = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 480 };
    private readonly ComboBox key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckBox push = new() { Text = "Hold selected key to record (no Ctrl+Alt)", AutoSize = true };
    private readonly Button save = new() { Text = "Save settings", AutoSize = true };
    private readonly System.Windows.Forms.Timer recordingTimer = new() { Interval = 100 };
    private readonly Overlay overlay = new();
    private AudioCapture? capture;
    private bool busy, closing, keyHeld;
    private IntPtr target, hook;
    private WindowsInput.Hook? hookCallback;
    private CancellationTokenSource? cancellation;
    private readonly Stopwatch recordingTime = new();
    private Task? activeTask;

    public VoiceForm()
    {
        try { settings = Settings.Load(); }
        catch (Exception e) { settings = new(); MessageBox.Show(e.Message, "Settings could not be loaded"); }
        Text = "JL Voice — development preview"; ClientSize = new Size(520, 390); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
        language.Items.AddRange(["Automatic (experimental)", "Čeština", "English"]);
        language.SelectedIndex = settings.Language switch { "cs" => 1, "en" => 2, _ => 0 };
        for (var i = 0; i < WaveInEvent.DeviceCount; i++) microphone.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
        if (microphone.Items.Count > 0) microphone.SelectedIndex = Math.Clamp(settings.Microphone, 0, microphone.Items.Count - 1);
        key.Items.AddRange(["F9", "F10", "F11", "F12"]); key.SelectedIndex = settings.Key - 0x78;
        push.Checked = settings.PushToTalk;
        layout.Controls.AddRange([status, new Label { Text = "Language", AutoSize = true }, language,
            new Label { Text = "Microphone", AutoSize = true }, microphone, key, push, save, result]);
        Controls.Add(layout);
        save.Click += (_, _) => SaveSettings();
        var menu = new ContextMenuStrip(); menu.Items.Add("Settings / last result", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("Cancel recording / transcription", null, async (_, _) => await CancelAsync());
        menu.Items.Add("Exit", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
        recordingTimer.Tick += (_, _) =>
        {
            overlay.SetStatus($"Recording · {recordingTime.Elapsed.TotalSeconds:F0}s");
            if (recordingTime.Elapsed.TotalSeconds >= AudioCapture.MaxSeconds) QueueToggle();
        };
        FormClosing += OnClosing;
        Shown += (_, _) => { ConfigureShortcut(); Hide(); Diagnostics.Mark("ready"); };
    }

    private void SaveSettings()
    {
        if (busy || capture is not null) return;
        try
        {
            var next = settings with { Language = language.SelectedIndex switch { 1 => "cs", 2 => "en", _ => "auto" },
                Microphone = microphone.SelectedIndex, PushToTalk = push.Checked, Key = 0x78 + key.SelectedIndex };
            next.Save(); settings = next; ConfigureShortcut();
        }
        catch (Exception e) { Notify(e.Message); }
    }

    private void ConfigureShortcut()
    {
        WindowsInput.UnregisterHotKey(Handle, WindowsInput.HotkeyId);
        if (hook != IntPtr.Zero) { WindowsInput.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        keyHeld = false;
        if (!settings.PushToTalk)
        {
            if (!WindowsInput.RegisterHotKey(Handle, WindowsInput.HotkeyId, 0x4000 | 1 | 2, (uint)settings.Key))
                Notify("Shortcut unavailable. Choose a different key in settings.");
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
            if (hook == IntPtr.Zero) Notify("Push-to-talk hook could not be registered.");
        }
        status.Text = settings.PushToTalk ? $"Ready — hold {(Keys)settings.Key}" : $"Ready — Ctrl+Alt+{(Keys)settings.Key}";
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
                target = WindowsInput.GetForegroundWindow();
                if (target == Handle) throw new InvalidOperationException("Focus the destination text field before recording.");
                result.Clear(); capture = new AudioCapture(settings.Microphone); capture.Start();
                save.Enabled = false; recordingTime.Restart(); recordingTimer.Start(); overlay.SetStatus("Recording"); overlay.Show(); status.Text = "Recording";
                Diagnostics.Mark("recording_started");
            }
            catch (Exception e) { capture?.Dispose(); capture = null; Notify(e.Message); }
            return;
        }
        busy = true; recordingTimer.Stop(); recordingTime.Stop(); overlay.SetStatus("Processing on CPU…");
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
            status.Text = $"Inserted · {latency.Elapsed.TotalSeconds:F1}s after stop";
        }
        catch (OperationCanceledException) { Diagnostics.Mark("cancelled"); status.Text = "Cancelled"; }
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
            finally { audio.Dispose(); overlay.Hide(); save.Enabled = true; busy = false; }
        }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        e.Cancel = true; closing = true;
        WindowsInput.UnregisterHotKey(Handle, WindowsInput.HotkeyId);
        if (hook != IntPtr.Zero) WindowsInput.UnhookWindowsHookEx(hook);
        await CancelAsync(); if (activeTask is not null) await activeTask;
        tray.Visible = false; tray.Dispose(); overlay.Dispose(); recordingTimer.Dispose();
        FormClosing -= OnClosing; Close();
    }
    private void Notify(string message) { status.Text = message; tray.ShowBalloonTip(5000, "JL Voice", message, ToolTipIcon.Info); }
}

internal sealed class Overlay : Form
{
    private readonly Label label = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = Color.FromArgb(28, 30, 38); Size = new Size(260, 44); Controls.Add(label);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea; Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnFormClosing(FormClosingEventArgs e) { e.Cancel = true; base.OnFormClosing(e); }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
    public void SetStatus(string text) => label.Text = text;
}
