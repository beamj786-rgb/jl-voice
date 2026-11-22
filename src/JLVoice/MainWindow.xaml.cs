using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;

namespace JLVoice;

public partial class MainWindow : Window
{
    private Settings settings;
    private readonly bool reviewMode;
    private bool binding = true, preferencesOpen, busy, closing, cancelRegistered;
    private bool? idleBubbleOverride;
    private readonly VoiceBubble bubble = new();
    private readonly DispatcherTimer recordingTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly HoldKeyState holdKey = new();
    private readonly Stopwatch recordingTime = new();
    private readonly List<string> modelFiles = [];
    private AudioCapture? capture;
    private CancellationTokenSource? cancellation;
    private Task? activeTask, cancelTask;
    private WindowsInput.Target target;
    private IntPtr handle, hook;
    private WindowsInput.Hook? hookCallback;
    private TrayIcon? tray;
    private readonly uint taskbarCreated = TrayIcon.RegisterWindowMessage("TaskbarCreated");
    private string locale = "cs";
    private const int CancelHotkeyId = 2;
    private string Shortcut => settings.PushToTalk ? DictationKeys.Name(settings.Key) : $"Ctrl + Alt + {DictationKeys.Name(settings.Key)}";
    private string T(string cs, string en) => locale == "cs" ? cs : en;
    public MainWindow(bool review = false)
    {
        reviewMode = review;
        try { settings = review ? new() : Settings.Load(); }
        catch (Exception e) { settings = new(); MessageBox.Show(e.Message, "JL Voice"); }
        locale = settings.UiLanguage;
        InitializeComponent(); Icon = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "brand", "JLVoice.ico")));
        DictationLanguage.ItemsSource = new[] { "", "Čeština", "English" }; DictationLanguage.SelectedIndex = settings.Language switch { "cs" => 1, "en" => 2, _ => 0 };
        ShortcutKey.DisplayMemberPath = nameof(DictationKey.Name); ShortcutKey.SelectedValuePath = nameof(DictationKey.Code);
        ShortcutKey.ItemsSource = DictationKeys.Options; ShortcutKey.SelectedValue = settings.Key;
        PushToTalk.IsChecked = settings.PushToTalk; IdleBubble.IsChecked = settings.ShowIdleBubble;
        Theme.ItemsSource = new[] { "", "", "" }; Theme.SelectedIndex = settings.Theme switch { "light" => 1, "system" => 2, _ => 0 };
        UiLanguage.ItemsSource = new[] { "Čeština", "English" }; UiLanguage.SelectedIndex = locale == "en" ? 1 : 0;
        foreach (var (file, name) in new[] { ("ggml-base-q5_1.bin", "Base · Q5"), ("ggml-base-q8_0.bin", "Base · Q8"), ("ggml-small-q5_1.bin", "Small · Q5") })
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "models", file)) || file == settings.Model) { modelFiles.Add(file); Model.Items.Add(name); }
        Model.SelectedIndex = Math.Max(0, modelFiles.IndexOf(settings.Model)); Threads.ItemsSource = new[] { "1", "2" }; Threads.SelectedIndex = settings.Threads - 1;
        RefreshMicrophones();
        foreach (var control in new[] { DictationLanguage, Microphone, ShortcutKey, Model, Threads, Theme, UiLanguage }) control.SelectionChanged += (_, _) => MarkPending();
        ShortcutKey.SelectionChanged += (_, _) => UpdateShortcutMode();
        UpdateShortcutMode();
        PushToTalk.Checked += (_, _) => MarkPending(); PushToTalk.Unchecked += (_, _) => MarkPending();
        IdleBubble.Checked += (_, _) => MarkPending(); IdleBubble.Unchecked += (_, _) => MarkPending();
        Advanced.Expanded += (_, _) => AdjustSize(); Advanced.Collapsed += (_, _) => AdjustSize();
        bubble.RecordRequested += QueueToggle; bubble.CancelRequested += RequestCancel;
        bubble.SettingsRequested += () => OpenWorkspace(true); bubble.DetailsRequested += () => OpenWorkspace(false);
        bubble.IdleHideRequested += () => { idleBubbleOverride = false; RefreshBubble(); }; bubble.Settled += RefreshBubble;
        recordingTimer.Tick += (_, _) =>
        {
            if (capture is { } audio) bubble.UpdateAudio(audio.Level, recordingTime.Elapsed);
            if (recordingTime.Elapsed.TotalSeconds >= AudioCapture.MaxSeconds) QueueToggle();
        };
        SourceInitialized += (_, _) =>
        {
            handle = NativeDesktop.Handle(this); HwndSource.FromHwnd(handle).AddHook(WindowHook);
            if (!reviewMode) tray = new TrayIcon(handle);
        };
        Loaded += (_, _) =>
        {
            if (reviewMode) return;
            ConfigureShortcut(); Hide(); bubble.PlaceForWindow(WindowsInput.GetForegroundWindow()); RefreshBubble(); Diagnostics.Mark("ready");
        };
        IsVisibleChanged += (_, _) => RefreshBubble();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { Hide(); WindowState = WindowState.Normal; } };
        Closing += OnClosing;
        SystemEvents.UserPreferenceChanged += OnSystemAppearance;
        languageSelection = DictationLanguage.SelectedIndex; themeSelection = Theme.SelectedIndex;
        Localize(); binding = false; ApplyTheme(); ReadyStatus(); bubble.SetIdle(Shortcut, locale); PendingText.Text = "";
    }
    private IntPtr WindowHook(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == Diagnostics.ExitProbeMessage && Diagnostics.Enabled) { handled = true; _ = ExitAsync(); }
        else if ((uint)message == taskbarCreated && !closing) tray?.Add();
        else if (message == 0x312)
        {
            handled = true; if (w == WindowsInput.HotkeyId) QueueToggle(); else if (w == CancelHotkeyId) RequestCancel();
        }
        else if (message == TrayIcon.Message)
        {
            if ((l.ToInt64() & 0xffff) == 0x203) OpenWorkspace(false);
            else if ((l.ToInt64() & 0xffff) == 0x205) OpenTrayMenu();
        }
        return IntPtr.Zero;
    }
    private void OpenTrayMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        void Add(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add(T("Otevřít JL Voice", "Open JL Voice"), () => OpenWorkspace(false));
        Add(T("Nastavení", "Settings"), () => OpenWorkspace(true));
        Add((idleBubbleOverride ?? settings.ShowIdleBubble) ? T("Skrýt bublinu v klidu", "Hide idle bubble") : T("Zobrazit bublinu", "Show bubble"), () => { idleBubbleOverride = !(idleBubbleOverride ?? settings.ShowIdleBubble); if (idleBubbleOverride == true) Hide(); RefreshBubble(); });
        Add(T("Zrušit diktování", "Cancel dictation"), RequestCancel);
        menu.Items.Add(new Separator()); Add(T("Ukončit JL Voice", "Exit JL Voice"), () => _ = ExitAsync()); menu.IsOpen = true;
    }
    private void OpenWorkspace(bool preferences)
    {
        Show(); WindowState = WindowState.Normal; Activate(); Navigate(preferences);
    }
    private void Navigate(bool preferences)
    {
        preferencesOpen = preferences; Overview.Visibility = preferences ? Visibility.Collapsed : Visibility.Visible;
        Preferences.Visibility = preferences ? Visibility.Visible : Visibility.Collapsed;
        NavigationButton.Content = preferences ? T("Zpět", "Back") : T("Nastavení", "Settings"); PageScroll.ScrollToTop(); AdjustSize();
    }
    private void AdjustSize()
    {
        if (reviewMode || !IsInitialized) return;
        Height = Math.Min(SystemParameters.WorkArea.Height - 30, preferencesOpen ? Advanced.IsExpanded ? 760 : 650 : string.IsNullOrEmpty(ResultText.Text) ? 430 : 620);
    }
    private void UpdateShortcutMode()
    {
        var caps = ShortcutKey.SelectedValue is int code && code == DictationKeys.CapsLock;
        if (caps) PushToTalk.IsChecked = true;
        PushToTalk.IsEnabled = !caps;
        ShortcutHelp.Text = caps
            ? T("Držte Caps Lock a mluvte. Uvolněním spustíte přepis. Velká písmena se nepřepínají.", "Hold Caps Lock to speak. Release to transcribe. Letter case is not toggled.")
            : T("Přepínání: Ctrl + Alt + klávesa. Při držení stačí samotná klávesa.", "Toggle: Ctrl + Alt + key. Hold mode uses the key alone.");
    }
    private void MarkPending() { if (!binding) PendingText.Text = T("Neuložené změny", "Unsaved changes"); }
    private void Localize()
    {
        locale = UiLanguage.SelectedIndex == 1 ? "en" : "cs";
        NavigationButton.Content = preferencesOpen ? T("Zpět", "Back") : T("Nastavení", "Settings");
        DictationLanguage.ItemsSource = new[] { T("Automaticky", "Automatic"), "Čeština", "English" };
        // Preserve selection while replacing translated labels.
        DictationLanguage.SelectedIndex = languageSelection;
        DictationLanguage.ToolTip = T("Automatická detekce je experimentální, zejména u krátkých a smíšených nahrávek.", "Automatic detection is experimental, especially for short and mixed-language recordings.");
        Theme.ItemsSource = new[] { T("Tmavý", "Dark"), T("Světlý", "Light"), T("Podle systému", "System") }; Theme.SelectedIndex = themeSelection;
        LanguageLabel.Text = T("Jazyk diktování", "Dictation language"); MicrophoneLabel.Text = T("Mikrofon", "Microphone");
        KeyLabel.Text = T("Klávesa", "Shortcut key"); ThemeLabel.Text = T("Vzhled", "Appearance"); UiLanguageLabel.Text = T("Jazyk rozhraní", "Interface language");
        ModelLabel.Text = T("Model v balíčku", "Bundled model"); ThreadsLabel.Text = T("CPU vlákna", "CPU threads");
        RefreshButton.Content = T("Obnovit", "Refresh"); PushToTalk.Content = T("Držet klávesu pro nahrávání", "Hold key to record");
        UpdateShortcutMode();
        IdleBubble.Content = T("Zobrazovat bublinu i v klidu", "Show the bubble when idle"); Advanced.Header = T("Pokročilé možnosti", "Advanced options");
        ModelHelp.Text = T("Automatický jazyk a kvalita modelů se zatím ověřují.", "Automatic detection and model quality are still being evaluated.");
        SaveButton.Content = T("Uložit", "Save changes"); BackToWorkButton.Content = T("Zpět k práci", "Back to work");
        EmptyResultText.Text = T("Poslední přepis se objeví tady.", "Your last transcript will appear here."); ResultHeading.Text = T("Poslední přepis", "Last transcript");
        CopyButton.Content = T("Kopírovat", "Copy"); ClearButton.Content = T("Vymazat", "Clear");
        PrivacyText.Text = T("Mikrofon se zapne až při nahrávání. Zvuk se neukládá.", "Microphone activates only when recording. Audio is not saved.");
        InstructionText.Text = T("Zaměřte textové pole. Použijte zkratku nebo bublinu dole na obrazovce.", "Focus a text field. Use the shortcut or the bubble at the bottom of the screen.");
        System.Windows.Automation.AutomationProperties.SetName(ResultText, ResultHeading.Text);
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, T("Skrýt do systémové lišty", "Hide to tray"));
        if (!Microphone.IsEnabled) { Microphone.Items.Clear(); Microphone.Items.Add(T("Žádný mikrofon", "No microphone")); Microphone.SelectedIndex = 0; }
        UpdateSummaries(); if (!busy && capture is null) { ReadyStatus(); bubble.SetIdle(Shortcut, locale); } MarkPending();
    }
    private int languageSelection, themeSelection;
    private void InterfaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (binding) return;
        languageSelection = DictationLanguage.SelectedIndex; themeSelection = Theme.SelectedIndex;
        binding = true; Localize(); binding = false; MarkPending();
    }
    private string ThemeName => Theme.SelectedIndex switch { 1 => "light", 2 => "system", _ => "dark" };
    private void ApplyTheme()
    {
        Appearance.Apply(ThemeName);
        Orbit.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "brand", Appearance.Light ? "mark-light.png" : "mark-dark.png")));
    }
    private void AppearanceChanged(object sender, SelectionChangedEventArgs e) { if (!binding) ApplyTheme(); }
    private void OnSystemAppearance(object sender, UserPreferenceChangedEventArgs e) => PostToUi(ApplyTheme);
    private void UpdateSummaries()
    {
        ShortcutText.Text = Shortcut;
        LanguageSummary.Text = T("Jazyk: ", "Language: ") + (settings.Language switch { "cs" => "Čeština", "en" => "English", _ => T("Automaticky", "Automatic") });
    }
    private void RefreshMicrophones()
    {
        var selected = Microphone.SelectedItem?.ToString(); Microphone.Items.Clear();
        for (var i = 0; i < WaveInEvent.DeviceCount; ++i) Microphone.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
        Microphone.IsEnabled = Microphone.Items.Count > 0;
        if (!Microphone.IsEnabled) Microphone.Items.Add(T("Žádný mikrofon", "No microphone"));
        Microphone.SelectedIndex = selected is not null && Microphone.Items.Contains(selected) ? Microphone.Items.IndexOf(selected) : Math.Clamp(settings.Microphone, 0, Microphone.Items.Count - 1);
    }
    private void RefreshBubble()
    {
        if (reviewMode || closing) return;
        var show = !IsVisible && (bubble.Activity != BubbleActivity.Idle || (idleBubbleOverride ?? settings.ShowIdleBubble));
        if (show) { if (!bubble.IsVisible) bubble.Show(); } else bubble.Hide();
    }
    private void ResetBubble() { bubble.SetIdle(Shortcut, locale); RefreshBubble(); }
    private void ReadyStatus() => StatusText.Text = Microphone.IsEnabled ? T("Připraveno k diktování.", "Ready to dictate.") : T("Připojte mikrofon.", "Connect a microphone.");
    private void Notify(string text) { StatusText.Text = text; bubble.SetError(locale, !string.IsNullOrWhiteSpace(ResultText.Text)); RefreshBubble(); }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (busy || capture is not null) return;
        try
        {
            var next = settings with { Language = DictationLanguage.SelectedIndex switch { 1 => "cs", 2 => "en", _ => "auto" }, Microphone = Microphone.SelectedIndex,
                PushToTalk = PushToTalk.IsChecked == true, Key = (int)ShortcutKey.SelectedValue, ShowIdleBubble = IdleBubble.IsChecked == true,
                Model = modelFiles[Model.SelectedIndex], Threads = Threads.SelectedIndex + 1, Theme = ThemeName, UiLanguage = locale };
            if (!reviewMode) next.Save(); settings = next; idleBubbleOverride = null;
            if (reviewMode || ConfigureShortcut()) { ReadyStatus(); ResetBubble(); }
            UpdateSummaries(); PendingText.Text = T("Uloženo.", "Saved.");
        }
        catch (Exception error) { Notify(error.Message); }
    }
    private bool ConfigureShortcut()
    {
        WindowsInput.UnregisterHotKey(handle, WindowsInput.HotkeyId);
        if (hook != IntPtr.Zero) { WindowsInput.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } holdKey.Reset();
        if (!settings.PushToTalk)
        {
            if (!WindowsInput.RegisterHotKey(handle, WindowsInput.HotkeyId, 0x4000 | 1 | 2, (uint)settings.Key)) { Notify(T("Zkratka je obsazená. Zvolte jinou klávesu.", "Shortcut unavailable. Choose another key.")); return false; }
        }
        else
        {
            hookCallback = (code, message, data) =>
            {
                if (code >= 0 && Marshal.ReadInt32(data) == settings.Key && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
                {
                    var down = message.ToInt64() is 0x100 or 0x104; var up = message.ToInt64() is 0x101 or 0x105;
                    if (down || up)
                    {
                        var action = holdKey.Change(down, !busy && capture is null && !closing);
                        // Cancel/rebind invalidates already queued input intents as well.
                        var version = holdKey.Version;
                        if (action == HoldAction.Start) PostToUi(() => { if (version == holdKey.Version && capture is null) QueueToggle(); });
                        if (action == HoldAction.Stop) PostToUi(() => { if (version == holdKey.Version && capture is not null) QueueToggle(); });
                    }
                    return new IntPtr(1);
                }
                return WindowsInput.CallNextHookEx(hook, code, message, data);
            };
            hook = WindowsInput.SetWindowsHookEx(13, hookCallback, WindowsInput.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { Notify(T("Klávesu se nepodařilo zaregistrovat.", "Could not register the key.")); return false; }
        }
        return true;
    }
    private void PostToUi(Action action) { if (!closing && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action); }
    private void EnterActive()
    {
        Preferences.IsEnabled = false; cancelRegistered = WindowsInput.RegisterHotKey(handle, CancelHotkeyId, 0x4000, 0x1B);
    }
    private void ExitActive()
    {
        recordingTimer.Stop(); Preferences.IsEnabled = true;
        if (cancelRegistered) WindowsInput.UnregisterHotKey(handle, CancelHotkeyId); cancelRegistered = false; RefreshBubble();
    }
    private void QueueToggle() { if (!reviewMode && !closing && !busy) activeTask = ToggleAsync(); }
    private void RequestCancel() { holdKey.Disarm(); if (cancelTask is null || cancelTask.IsCompleted) cancelTask = CancelAsync(); }
    private async Task ToggleAsync()
    {
        if (closing || busy) return;
        if (capture is null)
        {
            try
            {
                target = WindowsInput.CaptureTarget();
                if (target.Window == handle) throw new InvalidOperationException(T("Zaměřte cílové textové pole.", "Focus the destination text field."));
                var audio = new AudioCapture(settings.Microphone); capture = audio;
                audio.Faulted += error => PostToUi(() => { if (capture == audio) activeTask = FailCaptureAsync(audio, error); });
                audio.Start(); ResultText.Clear(); EnterActive(); recordingTime.Restart(); recordingTimer.Start();
                bubble.PlaceForWindow(target.Window); bubble.SetRecording(locale); RefreshBubble(); StatusText.Text = T("Nahrávám…", "Recording…"); Diagnostics.Mark("recording_started");
            }
            catch (Exception error) { capture?.Dispose(); capture = null; ExitActive(); Notify(error.Message); }
            return;
        }
        holdKey.Disarm(); busy = true; recordingTimer.Stop(); recordingTime.Stop();
        bubble.SetProcessing(locale); RefreshBubble(); StatusText.Text = T("Přepisuji…", "Transcribing…"); Diagnostics.Mark("recording_stop_requested");
        var audioSession = capture; capture = null; byte[]? wav = null;
        using var cts = new CancellationTokenSource(); cancellation = cts; var latency = Stopwatch.StartNew();
        try
        {
            wav = await audioSession.StopAsync(); audioSession.Dispose();
            var text = await AsrEngine.TranscribeAsync(wav, settings, cts.Token); ResultText.Text = text;
            WindowsInput.Insert(text, target); Diagnostics.Mark("text_insertion_sent");
            StatusText.Text = T("Text odeslán do původního pole.", "Text sent to the original field.") + $"  {latency.Elapsed.TotalSeconds:F1} s"; bubble.SetCompleted(locale);
        }
        catch (OperationCanceledException)
        {
            if (cts.IsCancellationRequested) { Diagnostics.Mark("cancelled"); StatusText.Text = T("Zrušeno.", "Cancelled."); bubble.SetCompleted(locale, true); }
            else Notify(T("Přepis překročil časový limit.", "Transcription timed out."));
        }
        catch (Exception error) { Diagnostics.Mark("failed"); Notify(error.Message); }
        finally { audioSession.Dispose(); if (wav is not null) Array.Clear(wav); cancellation = null; busy = false; ExitActive(); }
    }
    private async Task FailCaptureAsync(AudioCapture audio, Exception error)
    {
        holdKey.Disarm(); busy = true; capture = null; recordingTimer.Stop();
        try { await audio.StopAsync(discard: true); } catch { /* Display the original device failure below. */ }
        finally { audio.Dispose(); busy = false; ExitActive(); } Notify(error.Message);
    }
    private async Task CancelAsync()
    {
        if (cancellation is { } job) { bubble.SetProcessing(locale, true); job.Cancel(); return; }
        if (capture is not { } audio) return;
        busy = true; capture = null; recordingTimer.Stop();
        try { await audio.StopAsync(discard: true); StatusText.Text = T("Zrušeno.", "Cancelled."); bubble.SetCompleted(locale, true); }
        catch (Exception error) { Notify(error.Message); }
        finally { audio.Dispose(); busy = false; ExitActive(); }
    }
    internal async Task ExitAsync()
    {
        if (closing) return; closing = true;
        WindowsInput.UnregisterHotKey(handle, WindowsInput.HotkeyId); if (cancelRegistered) WindowsInput.UnregisterHotKey(handle, CancelHotkeyId);
        if (hook != IntPtr.Zero) WindowsInput.UnhookWindowsHookEx(hook);
        await CancelAsync(); if (cancelTask is not null) await cancelTask; if (activeTask is not null) await activeTask;
        recordingTimer.Stop(); tray?.Dispose(); bubble.DisposeBubble(); SystemEvents.UserPreferenceChanged -= OnSystemAppearance;
        Closing -= OnClosing; Close(); Application.Current.Shutdown(Environment.ExitCode);
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!closing) { e.Cancel = true; Hide(); } }
    private void NavigateClick(object sender, RoutedEventArgs e) => Navigate(!preferencesOpen);
    private void BackToWorkClick(object sender, RoutedEventArgs e) => Hide();
    private void MinimizeClick(object sender, RoutedEventArgs e) => Hide();
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void RefreshClick(object sender, RoutedEventArgs e) { RefreshMicrophones(); if (!busy && capture is null) ReadyStatus(); }
    private void ClearClick(object sender, RoutedEventArgs e) => ResultText.Clear();
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try { if (!string.IsNullOrWhiteSpace(ResultText.Text)) Clipboard.SetText(ResultText.Text); }
        catch (ExternalException) { Notify(T("Schránka je obsazená.", "Clipboard is busy.")); }
    }
    private void ResultChanged(object sender, TextChangedEventArgs e)
    {
        if (LastResult is null || EmptyResultText is null) return;
        var hasText = !string.IsNullOrEmpty(ResultText.Text); LastResult.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        EmptyResultText.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible; AdjustSize();
    }
}
