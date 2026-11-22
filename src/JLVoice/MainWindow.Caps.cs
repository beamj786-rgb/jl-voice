using Microsoft.Win32;
using System.Windows.Threading;

namespace JLVoice;

public partial class MainWindow
{
    private readonly CapsHoldState capsHold = new();
    private readonly DispatcherTimer pendingHold = new();
    private WindowsInput.Target capsTarget;
    private int pendingVersion;
    private bool suspended;

    private void CapsKey(bool down)
    {
        var now = Environment.TickCount64;
        if (down)
        {
            if (!capsHold.Down(now, !busy && capture is null && !closing && !suspended)) return;
            var version = capsHold.Version;
            // Cheap HWND snapshot at physical down; no capture, I/O or STT in the hook.
            var originalTarget = WindowsInput.TryCaptureTarget();
            PostToUi(() =>
            {
                if (version != capsHold.Version || capsHold.Phase != DictationPhase.Pending) return;
                try
                {
                    // A missing text target must not break ordinary short Caps taps.
                    capsTarget = originalTarget;
                    pendingVersion = version;
                    pendingHold.Interval = TimeSpan.FromMilliseconds(Math.Max(1, settings.HoldThresholdMs - (Environment.TickCount64 - capsHold.DownAt)));
                    pendingHold.Start();
                }
                catch (Exception error) { capsHold.Cancel(); Notify(error); }
            });
            return;
        }
        var action = capsHold.Up(now, settings.HoldThresholdMs);
        var current = capsHold.Version;
        var recording = capture;
        PostToUi(() =>
        {
            // A committed tap is not invalidated by a later rapid physical press.
            if (action == CapsAction.Toggle && !suspended)
            {
                try { WindowsInput.ToggleCapsLock(); } catch (Exception error) { Notify(error); }
            }
            if (action == CapsAction.Stop && recording is not null && ReferenceEquals(capture, recording)) QueueToggle();
            if (current != capsHold.Version) return;
            pendingHold.Stop();
        });
    }
    private void PendingHoldTick(object? sender, EventArgs e)
    {
        if (capsHold.Due(Environment.TickCount64, pendingVersion, settings.HoldThresholdMs) != CapsAction.Start) return;
        pendingHold.Stop();
        try
        {
            if (capsTarget.Window == IntPtr.Zero || capsTarget.Window == handle || WindowsInput.CaptureTarget() != capsTarget || busy || capture is not null || suspended || closing)
            { capsHold.Cancel(); return; }
            activeTask = ToggleAsync(capsTarget);
        }
        catch (Exception error) { capsHold.Cancel(); Notify(error); }
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            PostToUi(() => { suspended = true; RequestCancel(); });
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            PostToUi(() => { suspended = false; capsHold.Cancel(); });
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        PostToUi(() =>
        {
            if (e.Mode == PowerModes.Suspend) { suspended = true; RequestCancel(); }
            else if (e.Mode == PowerModes.Resume) { suspended = false; capsHold.Cancel(); }
        });
    }
}
