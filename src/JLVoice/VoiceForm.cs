using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JLVoice;

internal sealed partial class VoiceForm : Form
{
    private Settings settings;
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "JL Voice", Visible = true };
    private readonly Label status = new() { AutoSize = true };
    private readonly TextBox result = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly BrandCombo language = new(), microphone = new(), key = new();
    private readonly BrandCheckBox push = new() { AutoSize = true }, idleBubble = new() { AutoSize = true };
    private readonly BrandButton save = new();
    private readonly System.Windows.Forms.Timer recordingTimer = new() { Interval = 50 };
    private readonly VoiceBubble overlay = new();
    private AudioCapture? capture;
    private bool busy, closing, exitRequested, keyHeld, cancelRegistered;
    private bool? idleBubbleOverride;
    private WindowsInput.Target target;
    private IntPtr hook;
    private WindowsInput.Hook? hookCallback;
    private CancellationTokenSource? cancellation;
    private readonly Stopwatch recordingTime = new();
    private Task? activeTask, cancelTask;
    private const int CancelHotkeyId = 2;
    private string CurrentShortcut => settings.PushToTalk ? ((Keys)settings.Key).ToString() : $"Ctrl + Alt + {(Keys)settings.Key}";

    public VoiceForm(bool review = false)
    {
        reviewMode = review;
        try { settings = Settings.Load(); }
        catch (Exception e) { settings = new(); MessageBox.Show(e.Message, "Settings could not be loaded"); }
        BuildInterface();
        overlay.RecordRequested += QueueToggle;
        overlay.CancelRequested += RequestCancel;
        overlay.SettingsRequested += () => { ShowWorkspace(); Navigate(1); };
        overlay.DetailsRequested += () => { ShowWorkspace(); Navigate(0); };
        overlay.IdleHideRequested += () => { idleBubbleOverride = false; RefreshBubblePresence(); };
        overlay.Settled += RefreshBubblePresence;
        recordingTimer.Tick += (_, _) =>
        {
            if (capture is { } audio) overlay.UpdateAudio(audio.Level, recordingTime.Elapsed);
            if (recordingTime.Elapsed.TotalSeconds >= AudioCapture.MaxSeconds) QueueToggle();
        };
        VisibleChanged += (_, _) => RefreshBubblePresence();
        FormClosing += OnClosing;
        Shown += (_, _) =>
        {
            if (reviewMode) return;
            ConfigureShortcut(); Hide(); overlay.PlaceForWindow(WindowsInput.GetForegroundWindow()); RefreshBubblePresence(); Diagnostics.Mark("ready");
        };
    }
    private void RefreshBubblePresence()
    {
        if (reviewMode || closing) return;
        var active = overlay.Activity != BubbleActivity.Idle;
        var show = !Visible && (active || (idleBubbleOverride ?? settings.ShowIdleBubble));
        if (show) { if (!overlay.Visible) overlay.Show(); } else overlay.Hide();
        UpdateBubbleMenu();
    }
    private void ResetBubble() { overlay.SetIdle(CurrentShortcut, uiLanguage); RefreshBubblePresence(); }
    private void SaveSettings()
    {
        if (busy || capture is not null) return;
        try
        {
            var next = settings with { Language = language.SelectedIndex switch { 1 => "cs", 2 => "en", _ => "auto" },
                Microphone = microphone.SelectedIndex, PushToTalk = push.Checked, Key = 0x78 + key.SelectedIndex,
                Model = modelFiles[model.SelectedIndex], Threads = threads.SelectedIndex + 1, Theme = SelectedTheme,
                UiLanguage = uiLanguage, ShowIdleBubble = idleBubble.Checked };
            next.Save(); settings = next; idleBubbleOverride = null;
            var registered = reviewMode || ConfigureShortcut();
            pending.Text = T("Uloženo.", "Saved."); UpdateSummaries(); if (registered) { ReadyStatus(); ResetBubble(); }
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
            { Notify(T("Zkratka je obsazená. Zvolte jinou klávesu.", "Shortcut unavailable. Choose another key.")); return false; }
        }
        else
        {
            hookCallback = (code, message, data) =>
            {
                if (code >= 0 && Marshal.ReadInt32(data) == settings.Key && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
                {
                    var down = message.ToInt64() is 0x100 or 0x104;
                    var up = message.ToInt64() is 0x101 or 0x105;
                    if (down && !keyHeld) { keyHeld = true; if (!busy && capture is null) PostToUi(QueueToggle); }
                    if (up && keyHeld) { keyHeld = false; PostToUi(() => { if (capture is not null) QueueToggle(); }); }
                    return (IntPtr)1;
                }
                return WindowsInput.CallNextHookEx(hook, code, message, data);
            };
            hook = WindowsInput.SetWindowsHookEx(13, hookCallback, WindowsInput.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { Notify(T("Klávesu se nepodařilo zaregistrovat.", "Could not register the key.")); return false; }
        }
        UpdateSummaries(); ReadyStatus(); ResetBubble(); return true;
    }
    private void PostToUi(Action action)
    {
        try { if (!closing && !IsDisposed && IsHandleCreated) BeginInvoke(action); }
        catch (InvalidOperationException) when (closing || IsDisposed || Disposing) { }
    }
    private void EnterActiveMode()
    {
        save.Enabled = preferences.Enabled = false;
        cancelRegistered = WindowsInput.RegisterHotKey(Handle, CancelHotkeyId, 0x4000, (uint)Keys.Escape);
    }
    private void ExitActiveMode()
    {
        recordingTimer.Stop(); save.Enabled = preferences.Enabled = true;
        if (cancelRegistered) WindowsInput.UnregisterHotKey(Handle, CancelHotkeyId);
        cancelRegistered = false; RefreshBubblePresence();
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Diagnostics.ExitProbeMessage && Diagnostics.Enabled) { ExitApplication(); return; }
        if (m.Msg == 0x312)
        {
            if (m.WParam == WindowsInput.HotkeyId) QueueToggle();
            else if (m.WParam == CancelHotkeyId) RequestCancel();
        }
        base.WndProc(ref m);
    }
    private void QueueToggle() { if (!reviewMode && !closing && !busy) activeTask = ToggleAsync(); }
    private void RequestCancel() { if (cancelTask is null || cancelTask.IsCompleted) cancelTask = CancelAsync(); }
    private async Task ToggleAsync()
    {
        if (closing || busy) return;
        if (capture is null)
        {
            try
            {
                target = WindowsInput.CaptureTarget();
                if (target.Window == Handle) throw new InvalidOperationException(T("Zaměřte cílové textové pole.", "Focus the destination text field."));
                result.Clear();
                var audio = new AudioCapture(settings.Microphone); capture = audio;
                audio.Faulted += error => PostToUi(() => { if (capture == audio) activeTask = FailCaptureAsync(audio, error); });
                audio.Start(); EnterActiveMode(); recordingTime.Restart(); recordingTimer.Start();
                overlay.PlaceForWindow(target.Window); overlay.SetRecording(uiLanguage); RefreshBubblePresence();
                SetStatus(T("Nahrávám…", "Recording…")); Diagnostics.Mark("recording_started");
            }
            catch (Exception e) { capture?.Dispose(); capture = null; ExitActiveMode(); Notify(e.Message); }
            return;
        }
        busy = true; recordingTimer.Stop(); recordingTime.Stop();
        overlay.SetProcessing(uiLanguage); RefreshBubblePresence(); SetStatus(T("Přepisuji…", "Transcribing…")); Diagnostics.Mark("recording_stop_requested");
        var audioSession = capture; capture = null; byte[]? wav = null;
        using var cts = new CancellationTokenSource(); cancellation = cts;
        var latency = Stopwatch.StartNew();
        try
        {
            wav = await audioSession.StopAsync(); audioSession.Dispose();
            var text = await AsrEngine.TranscribeAsync(wav, settings, cts.Token); result.Text = text;
            WindowsInput.Insert(text, target); Diagnostics.Mark("text_insertion_sent");
            SetStatus(T("Text odeslán do původního pole.", "Text sent to the original field.") + $"  {latency.Elapsed.TotalSeconds:F1} s");
            overlay.SetCompleted(uiLanguage);
        }
        catch (OperationCanceledException)
        {
            if (cts.IsCancellationRequested) { Diagnostics.Mark("cancelled"); SetStatus(T("Zrušeno.", "Cancelled.")); overlay.SetCompleted(uiLanguage, cancelled: true); }
            else Notify(T("Přepis překročil časový limit.", "Transcription timed out."));
        }
        catch (Exception e) { Diagnostics.Mark("failed"); Notify(e.Message); }
        finally
        {
            audioSession.Dispose(); if (wav is not null) Array.Clear(wav);
            cancellation = null; busy = false; ExitActiveMode();
        }
    }
    private async Task FailCaptureAsync(AudioCapture audio, Exception error)
    {
        busy = true; capture = null; recordingTimer.Stop();
        try { await audio.StopAsync(); } catch { /* The observed device failure is shown below. */ }
        finally { audio.Dispose(); busy = false; ExitActiveMode(); }
        Notify(error.Message);
    }
    private async Task CancelAsync()
    {
        if (cancellation is { } job) { overlay.SetProcessing(uiLanguage, cancelling: true); job.Cancel(); return; }
        if (capture is not { } audio) return;
        busy = true; capture = null; recordingTimer.Stop();
        try { var wav = await audio.StopAsync(); Array.Clear(wav); SetStatus(T("Zrušeno.", "Cancelled.")); overlay.SetCompleted(uiLanguage, cancelled: true); }
        catch (Exception e) { Notify(e.Message); }
        finally { audio.Dispose(); busy = false; ExitActiveMode(); }
    }
    internal void ExitApplication() { exitRequested = true; Close(); }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        if (e.CloseReason == CloseReason.UserClosing && !exitRequested) { e.Cancel = true; Hide(); return; }
        e.Cancel = true; closing = true;
        WindowsInput.UnregisterHotKey(Handle, WindowsInput.HotkeyId);
        if (cancelRegistered) WindowsInput.UnregisterHotKey(Handle, CancelHotkeyId);
        if (hook != IntPtr.Zero) WindowsInput.UnhookWindowsHookEx(hook);
        await CancelAsync(); if (cancelTask is not null) await cancelTask; if (activeTask is not null) await activeTask;
        tray.Visible = false; tray.Dispose(); overlay.Dispose(); recordingTimer.Dispose(); DisposeView();
        FormClosing -= OnClosing; BeginInvoke(Close);
    }
    private void Notify(string message)
    {
        SetStatus(message); overlay.SetError(uiLanguage, !string.IsNullOrWhiteSpace(result.Text)); RefreshBubblePresence();
        tray.ShowBalloonTip(5000, "JL Voice", message, ToolTipIcon.Info);
    }
}
