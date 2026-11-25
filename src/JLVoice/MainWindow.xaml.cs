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
    private Settings.LoadSource settingsSource;
    private bool hasPending;
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
        var loaded = review ? new Settings.LoadResult(new Settings(), Settings.LoadSource.DefaultsNoFile, null, null) : Settings.LoadWithStatus();
        settings = loaded.Settings; settingsSource = loaded.Source; locale = settings.UiLanguage;
        if (loaded.Problem is not null)
            MessageBox.Show((loaded.Source == Settings.LoadSource.RestoredFromBackup
                ? "settings.json was invalid; the previous settings (settings.json.bak) were restored.\nsettings.json byl neplatný; obnovena předchozí nastavení (settings.json.bak)."
                : "settings.json was invalid; DEFAULT settings are active.\nsettings.json byl neplatný; platí VÝCHOZÍ nastavení.") +
                "\n\n" + loaded.Problem + (loaded.InvalidFileBackup is null ? "" : $"\n\nSaved aside / Uloženo bokem: {loaded.InvalidFileBackup}"), "JL Voice");
        InitializeComponent(); Icon = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "brand", "JLVoice.ico")));
        DictationLanguage.ItemsSource = new[] { "", "Čeština", "English" }; DictationLanguage.SelectedIndex = settings.Language switch { "cs" => 1, "en" => 2, _ => 0 };
        ShortcutKey.SelectedValuePath = nameof(DictationKey.Code);
        ShortcutKey.ItemsSource = DictationKeys.Options; ShortcutKey.SelectedValue = settings.Key;
        PushToTalk.IsChecked = settings.PushToTalk; IdleBubble.IsChecked = settings.ShowIdleBubble;
        HoldThreshold.Text = settings.HoldThresholdMs.ToString(); HoldThreshold.TextChanged += (_, _) => MarkPending();
        Theme.ItemsSource = new[] { "", "", "" }; Theme.SelectedIndex = settings.Theme switch { "light" => 1, "system" => 2, _ => 0 };
        UiLanguage.ItemsSource = new[] { "Čeština", "English" }; UiLanguage.SelectedIndex = locale == "en" ? 1 : 0;
        foreach (var (file, name) in new[] { ("ggml-base-q5_1.bin", "Base · Q5"), ("ggml-base-q8_0.bin", "Base · Q8"), ("ggml-small-q5_1.bin", "Small · Q5"), ("ggml-large-v3-turbo-q5_0.bin", "Large v3 Turbo · Q5 (test)") })
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "models", file)) || file == settings.Model) { modelFiles.Add(file); Model.Items.Add(name); }
        Model.SelectedIndex = Math.Max(0, modelFiles.IndexOf(settings.Model)); Threads.ItemsSource = new[] { "1", "2" }; Threads.SelectedIndex = settings.Threads - 1;
        InitializeCloudControls();
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
            if (capture is not null)
            {
                try { if (WindowsInput.CaptureTarget() != target) { RequestCancel(); return; } }
                catch { RequestCancel(); return; }
            }
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
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; HideConfirmed(); } };
        Closing += OnClosing;
        SystemEvents.UserPreferenceChanged += OnSystemAppearance;
        pendingHold.Tick += PendingHoldTick;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        languageSelection = DictationLanguage.SelectedIndex; themeSelection = Theme.SelectedIndex;
        Localize(); binding = false; ApplyTheme(); ReadyStatus(); bubble.SetIdle(BubbleShortcut, locale); PendingText.Text = ""; hasPending = false;
        if (BuildInfo.Identity.PackageInfoPresent) Title = "JL Voice · " + BuildInfo.Identity.ShortCommit;
        if (!reviewMode) { Diagnostics.Mark("build", BuildInfo.DiagnosticView()); Diagnostics.Mark("config_active", settings.DiagnosticView(MicrophoneName(settings.Microphone), settingsSource)); }
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
        Height = Math.Min(SystemParameters.WorkArea.Height - 30, preferencesOpen ? Advanced.IsExpanded || Provider.SelectedIndex == 1 ? 760 : 650 : NoticePanel.IsVisible ? 600 : string.IsNullOrEmpty(ResultText.Text) ? 430 : 620);
    }
    private void UpdateShortcutMode()
    {
        var caps = ShortcutKey.SelectedValue is int code && code == DictationKeys.CapsLock;
        if (caps) PushToTalk.IsChecked = true;
        PushToTalk.IsEnabled = !caps;
        ShortcutHelp.Text = caps
            ? T($"Krátký stisk přepne Caps Lock. Držení od {settings.HoldThresholdMs} ms nahrává; uvolnění spustí přepis.", $"Tap toggles Caps Lock. Hold for {settings.HoldThresholdMs} ms to record; release to transcribe.")
            : T("Přepínání: Ctrl + Alt + klávesa. Při držení stačí samotná klávesa.", "Toggle: Ctrl + Alt + key. Hold mode uses the key alone.");
    }
    private void MarkPending()
    {
        if (binding) return;
        hasPending = true;
        PendingText.Text = T("Neuložené změny – zatím platí: ", "Unsaved changes – still active: ") + ActiveSummary();
    }
    private static string? MicrophoneName(int index)
    {
        try { return index >= 0 && index < WaveInEvent.DeviceCount ? WaveInEvent.GetCapabilities(index).ProductName : null; }
        catch (Exception error) when (error is MmException or InvalidOperationException) { return null; }
    }
    private static string ModelLabel(string file) => file switch
    {
        "ggml-base-q5_1.bin" => "Base · Q5", "ggml-base-q8_0.bin" => "Base · Q8", "ggml-small-q5_1.bin" => "Small · Q5",
        "ggml-large-v3-turbo-q5_0.bin" => "Large v3 Turbo · Q5 (test)", _ => file
    };
    // What the app is ACTUALLY using for the next recording (the saved, active configuration — never the draft).
    private string ActiveSummary() =>
        (settings.Provider == "nvidia" ? "NVIDIA cloud · Parakeet TDT" : "Offline · " + ModelLabel(settings.Model)) + " · " +
        (settings.Language switch { "cs" => "Čeština", "en" => "English", _ => T("Automaticky", "Automatic") }) + " · " +
        T("mikrofon: ", "microphone: ") + (MicrophoneName(settings.Microphone) ?? T("nedostupný", "unavailable"));
    private void SetSaveFeedback(string? text)
    {
        SaveFeedback.Text = text ?? ""; SaveFeedback.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    // Leaving the page/window with a draft: the user decides, so a different backend is never used unnoticed.
    private bool ConfirmLeave()
    {
        if (reviewMode || !hasPending) return true;
        var answer = MessageBox.Show(this,
            T("Máte neuložené změny nastavení. Dokud je neuložíte, aplikace dál používá:\n", "You have unsaved settings changes. Until saved, the app keeps using:\n") + ActiveSummary() +
            T("\n\nUložit změny? (Ano = uložit, Ne = zahodit, Zrušit = zůstat v Nastavení)", "\n\nSave changes? (Yes = save, No = discard, Cancel = stay in Settings)"),
            "JL Voice", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) { SaveClick(this, new RoutedEventArgs()); return !hasPending; }
        DiscardDraft(); return true;
    }
    // Puts every control (including live-previewed theme and interface language) back to the active settings.
    private void DiscardDraft()
    {
        binding = true;
        try
        {
            languageSelection = settings.Language switch { "cs" => 1, "en" => 2, _ => 0 };
            themeSelection = settings.Theme switch { "light" => 1, "system" => 2, _ => 0 };
            UiLanguage.SelectedIndex = settings.UiLanguage == "en" ? 1 : 0;
            ShortcutKey.SelectedValue = settings.Key; PushToTalk.IsChecked = settings.PushToTalk; IdleBubble.IsChecked = settings.ShowIdleBubble;
            HoldThreshold.Text = settings.HoldThresholdMs.ToString();
            Model.SelectedIndex = Math.Max(0, modelFiles.IndexOf(settings.Model)); Threads.SelectedIndex = settings.Threads - 1;
            Provider.SelectedIndex = settings.Provider == "nvidia" ? 1 : 0; CloudConsent.IsChecked = settings.CloudConsentVersion == 1;
            RememberCloudKey.IsChecked = settings.RememberNvidiaKey; NvidiaKey.Clear();
            Microphone.SelectedIndex = Microphone.Items.Count == 0 ? -1 : Math.Clamp(settings.Microphone, 0, Microphone.Items.Count - 1);
            Localize(); ApplyTheme(); UpdateCloudControls();
        }
        finally { binding = false; }
        hasPending = false; PendingText.Text = ""; SetSaveFeedback(null);
    }
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
        ReviewAudio.Content = T("Před přepisem přehrát nahrávku (test zvuku)", "Play recording before transcription (audio check)");
        ReviewAudio.ToolTip = T("Dočasně pro toto spuštění. Přehraje přesně zvuk posílaný modelu. Nic se neukládá; po přepisu se zvuk uvolní. Zrušení: Escape.", "For this session only. Plays the exact audio sent to the model. Nothing is saved; audio is released after transcription. Cancel with Escape.");
        HoldThresholdLabel.Text = T("Délka držení Caps Lock (ms, 100–1000)", "Caps Lock hold threshold (ms, 100–1000)");
        RetainAudio.Content = new TextBlock { Text = T("Vývojový test: ponechat poslední zvuk v paměti", "Developer check: retain last audio in memory"), TextWrapping = TextWrapping.Wrap, MaxWidth = 370 };
        RetainAudio.ToolTip = T("Pouze po výslovném zapnutí a jen do odškrtnutí nebo ukončení aplikace. Opakovaný přepis nic nevkládá.", "Explicit opt-in only, until unchecked or app exit. Replay transcription never inserts text.");
        ReplayButton.Content = T("Přepsat stejný zvuk znovu", "Transcribe the same audio again");
        LocalizeCloud();
        SaveButton.Content = T("Uložit", "Save changes"); BackToWorkButton.Content = T("Zpět k práci", "Back to work");
        EmptyResultText.Text = T("Poslední přepis se objeví tady.", "Your last transcript will appear here."); ResultHeading.Text = T("Poslední přepis", "Last transcript");
        CopyButton.Content = T("Kopírovat", "Copy"); ClearButton.Content = T("Vymazat", "Clear");
        PrivacyText.Text = T("Mikrofon se zapne až při nahrávání. Zvuk se neukládá.", "Microphone activates only when recording. Audio is not saved.");
        InstructionText.Text = T("Zaměřte textové pole. Použijte zkratku nebo bublinu dole na obrazovce.", "Focus a text field. Use the shortcut or the bubble at the bottom of the screen.");
        System.Windows.Automation.AutomationProperties.SetName(ResultText, ResultHeading.Text);
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, T("Skrýt do systémové lišty", "Hide to tray"));
        if (!Microphone.IsEnabled) { Microphone.Items.Clear(); Microphone.Items.Add(T("Žádný mikrofon", "No microphone")); Microphone.SelectedIndex = 0; }
        UpdateSummaries(); if (!busy && capture is null) { ReadyStatus(); bubble.SetIdle(BubbleShortcut, locale); } MarkPending();
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
        ActiveConfigText.Text = T("Aktivní konfigurace: ", "Active configuration: ") + ActiveSummary() + "\n" + settingsSource switch
        {
            Settings.LoadSource.File => T("Zdroj: settings.json", "Source: settings.json"),
            Settings.LoadSource.DefaultsNoFile => T("Zdroj: výchozí hodnoty (settings.json zatím neexistuje)", "Source: built-in defaults (no settings.json yet)"),
            Settings.LoadSource.RestoredFromBackup => T("Zdroj: záloha settings.json.bak (settings.json byl neplatný)", "Source: settings.json.bak (settings.json was invalid)"),
            _ => T("Zdroj: VÝCHOZÍ hodnoty (settings.json byl neplatný; uložen bokem jako settings.invalid-*.json)", "Source: DEFAULTS (settings.json was invalid; saved aside as settings.invalid-*.json)")
        };
        BuildText.Text = "Build: " + BuildInfo.Identity.Describe();
        LanguageSummary.Text = T("Jazyk: ", "Language: ") + (settings.Language switch { "cs" => "Čeština", "en" => "English", _ => T("Automaticky", "Automatic") }) + " · " + (settings.Provider == "nvidia" ? "NVIDIA cloud" : settings.Model);
        PrivacyText.Text = settings.Provider == "nvidia" ? T("Cloud zapnutý: nahraný zvuk se odesílá do NVIDIA.", "Cloud enabled: recorded audio is sent to NVIDIA.")
            : T("Offline: zvuk se zpracovává pouze na tomto počítači.", "Offline: audio is processed only on this computer.");
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
    private void ResetBubble() { bubble.SetIdle(BubbleShortcut, locale); RefreshBubble(); }
    private void ReadyStatus() => SetStatus(settings.Provider == "nvidia" && (!cloudApproved || !NvidiaSpeechClient.ValidKey(sessionNvidiaKey))
        ? T("Doplňte cloudový klíč a souhlas v Nastavení.", "Set the cloud key and consent in Settings.")
        : Microphone.IsEnabled ? T("Připraveno k diktování.", "Ready to dictate.") : T("Připojte mikrofon.", "Connect a microphone."));
    private void SetStatus(string text)
    {
        StatusText.Text = text; NoticePanel.Visibility = Visibility.Collapsed;
        NoticeDetails.IsExpanded = false; NoticeBody.Text = NoticeDetailsText.Text = ""; AdjustSize();
    }
    private void Notify(string text) => ShowNotice(new ErrorNotice(text, "", ""));
    private void Notify(Exception error) => ShowNotice(ErrorNotice.From(error, locale));
    private void ShowNotice(ErrorNotice notice)
    {
        SetStatus(notice.Title); NoticeBody.Text = notice.Message; NoticeDetailsText.Text = notice.Details;
        NoticeDetails.Header = T("Technické podrobnosti", "Technical details");
        NoticeDetails.Visibility = string.IsNullOrEmpty(notice.Details) ? Visibility.Collapsed : Visibility.Visible;
        NoticePanel.Visibility = string.IsNullOrEmpty(notice.Message) && string.IsNullOrEmpty(notice.Details) ? Visibility.Collapsed : Visibility.Visible;
        AdjustSize(); bubble.SetError(locale, !string.IsNullOrWhiteSpace(ResultText.Text)); RefreshBubble();
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (busy || capture is not null) return;
        SetSaveFeedback(null);
        try
        {
            var next = settings with { Language = DictationLanguage.SelectedIndex switch { 1 => "cs", 2 => "en", _ => "auto" }, Microphone = Microphone.SelectedIndex,
                Provider = Provider.SelectedIndex == 1 ? "nvidia" : "offline", CloudConsentVersion = Provider.SelectedIndex == 1 && CloudConsent.IsChecked == true ? 1 : 0,
                RememberNvidiaKey = RememberCloudKey.IsChecked == true,
                PushToTalk = PushToTalk.IsChecked == true, Key = (int)ShortcutKey.SelectedValue, HoldThresholdMs = int.TryParse(HoldThreshold.Text, out var holdMs) ? holdMs : throw new InvalidDataException(T("Délka držení musí být celé číslo 100–1000 ms.", "The hold threshold must be a whole number from 100 to 1000 ms.")), ShowIdleBubble = IdleBubble.IsChecked == true,
                Model = modelFiles[Model.SelectedIndex], Threads = Threads.SelectedIndex + 1, Theme = ThemeName, UiLanguage = locale };
            var key = PrepareCloudSettings(next);
            // Atomic write first: only a configuration that reached disk (or review mode) becomes active.
            if (!reviewMode) next.Save();
            settings = next; settingsSource = Settings.LoadSource.File; idleBubbleOverride = null;
            sessionNvidiaKey = key; cloudApproved = next.Provider == "nvidia" && next.CloudConsentVersion == 1;
            NvidiaKey.Clear(); UpdateCloudControls();
            var shortcutOk = reviewMode || ConfigureShortcut();
            if (shortcutOk) { ReadyStatus(); ResetBubble(); }
            UpdateSummaries(); hasPending = false;
            PendingText.Text = T("Uloženo. Aktivní: ", "Saved. Active: ") + ActiveSummary();
            if (!shortcutOk) SetSaveFeedback(T("⚠ Nastavení je uloženo, ale zkratka není aktivní. Zvolte jinou klávesu a uložte znovu.", "⚠ Settings are saved, but the shortcut is not active. Choose another key and save again."));
            if (!reviewMode) Diagnostics.Mark("config_active", settings.DiagnosticView(MicrophoneName(settings.Microphone), settingsSource));
        }
        catch (Exception error)
        {
            // The visible, persistent explanation lives next to the Save button, not only on the hidden Overview page.
            var notice = ErrorNotice.From(error, locale);
            SetSaveFeedback("⚠ " + T("Nastavení NEBYLO uloženo. ", "Settings were NOT saved. ") + notice.Title + (string.IsNullOrEmpty(notice.Message) ? "" : " " + notice.Message) +
                "\n" + T("Stále platí: ", "Still active: ") + ActiveSummary());
            Diagnostics.Mark("settings_save_failed", new { error_type = error.GetType().Name });
            Notify(error);
        }
    }
    private bool ConfigureShortcut()
    {
        pendingHold.Stop(); capsHold.Reset();
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
                if (code >= 0 && Marshal.ReadInt32(data) == settings.Key && !WindowsInput.IsOwnInput(data) && !WindowsInput.IsInjected(data))
                {
                    var down = message.ToInt64() is 0x100 or 0x104; var up = message.ToInt64() is 0x101 or 0x105;
                    if (down || up)
                    {
                        if (settings.Key == DictationKeys.CapsLock) { CapsKey(down); return new IntPtr(1); }
                        var action = holdKey.Change(down, !busy && capture is null && !closing);
                        // Cancel/rebind invalidates already queued input intents as well.
                        var version = holdKey.Version;
                        if (action == HoldAction.Start) PostToUi(() => { if (version == holdKey.Version && capture is null) QueueToggle(); });
                        if (action == HoldAction.Stop) PostToUi(() => { if (version == holdKey.Version && capture is not null) QueueToggle(); });
                    }
                    if (down || up) return new IntPtr(1);
                }
                return WindowsInput.CallNextHookEx(hook, code, message, data);
            };
            hook = WindowsInput.SetWindowsHookEx(13, hookCallback, WindowsInput.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { Notify(T("Klávesu se nepodařilo zaregistrovat. Použijte bublinu nebo Ctrl + Alt + F9 v nastavení.", "Could not register the key. Use the bubble or select Ctrl + Alt + F9 in settings.")); return false; }
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
        capsHold.Complete();
        recordingTimer.Stop(); Preferences.IsEnabled = true;
        if (cancelRegistered) WindowsInput.UnregisterHotKey(handle, CancelHotkeyId); cancelRegistered = false; RefreshBubble();
    }
    private void QueueToggle() { if (!reviewMode && !closing && !busy) activeTask = ToggleAsync(); }
    private void RequestCancel() { pendingHold.Stop(); capsHold.Cancel(); holdKey.Disarm(); if (cancelTask is null || cancelTask.IsCompleted) cancelTask = CancelAsync(); }
    private async Task ToggleAsync(WindowsInput.Target? original = null)
    {
        if (closing || busy || suspended) return;
        if (capture is null)
        {
            try
            {
                if (original is null) holdPressedAt = null; // Only the Caps path supplies a measured press time.
                RequireCloudReady();
                target = original ?? WindowsInput.CaptureTarget();
                if (target.Window == handle) throw new InvalidOperationException(T("Zaměřte cílové textové pole.", "Focus the destination text field."));
                var audio = new AudioCapture(settings.Microphone); capture = audio;
                audio.Faulted += error => PostToUi(() => { if (capture == audio) activeTask = FailCaptureAsync(audio, error); });
                // Signal "speak now" only once audio is actually arriving, not when the device call returns.
                audio.FirstBuffer += () => PostToUi(() =>
                {
                    if (capture != audio || busy) return;
                    bubble.SetRecording(locale); RefreshBubble(); SetStatus(T("Nahrávám – mluvte.", "Recording – speak now.")); Diagnostics.Mark("first_pcm_buffer");
                });
                bubble.PlaceForWindow(target.Window); bubble.SetPreparing(locale); SetStatus(T("Připravuji mikrofon…", "Preparing microphone…"));
                audio.Start(); ResultText.Clear(); EnterActive(); recordingTime.Restart(); recordingTimer.Start();
                if (audio.FirstBufferTick != 0 && capture == audio) { bubble.SetRecording(locale); SetStatus(T("Nahrávám – mluvte.", "Recording – speak now.")); }
                RefreshBubble(); Diagnostics.Mark("recording_started"); Diagnostics.Mark("config_at_recording", settings.DiagnosticView(MicrophoneName(settings.Microphone), settingsSource));
            }
            catch (Exception error) { capture?.Dispose(); capture = null; holdPressedAt = null; ExitActive(); Notify(error); }
            return;
        }
        holdKey.Disarm(); busy = true; recordingTimer.Stop(); recordingTime.Stop();
        bubble.SetProcessing(locale); RefreshBubble(); SetStatus(T("Přepisuji…", "Transcribing…")); Diagnostics.Mark("recording_stop_requested");
        var audioSession = capture; capture = null; byte[]? wav = null;
        var pressedAt = holdPressedAt; holdPressedAt = null;
        using var cts = new CancellationTokenSource(); cancellation = cts; var latency = Stopwatch.StartNew();
        try
        {
            // Experimental opt-in (settings.json PostRollMs, default 0): records after the key is released.
            // Cancellation, lock/suspend and exit cancel this delay through cts; new holds are refused while busy.
            var postRoll = settings.PushToTalk ? settings.PostRollMs : 0;
            if (postRoll > 0) await Task.Delay(postRoll, cts.Token);
            wav = await audioSession.StopAsync(); audioSession.Dispose();
            var timing = pressedAt is { } down ? CaptureTiming.From(down, audioSession) : null;
            Diagnostics.Mark("capture_window", new { open_started_after_press_ms = timing?.OpenStartedMs, start_returned_after_press_ms = timing?.StartReturnedMs,
                first_buffer_delivered_after_press_ms = timing?.FirstBufferMs, buffer_ms = AudioCapture.BufferMilliseconds, post_roll_ms = postRoll });
            if (RetainAudio.IsChecked == true) { ForgetReplay(); replayWav = (byte[])wav.Clone(); ReplayButton.IsEnabled = true; }
            if (ReviewAudio.IsChecked == true)
            {
                SetStatus(T("Přehrávám zachycený zvuk…", "Playing captured audio…"));
                await AudioPlayback.PlayAsync(wav, cts.Token);
                SetStatus(T("Přepisuji…", "Transcribing…"));
            }
            var result = await TranscribeSelectedAsync(wav, cts.Token); cts.Token.ThrowIfCancellationRequested(); ShowRecognition(result, wav, timing);
            // Only a plausible transcript of detected speech is inserted automatically.
            if (!result.Insertable) { ShowWithheld(result); return; }
            await WindowsInput.InsertAsync(result.Text, target, cts.Token, handle); Diagnostics.Mark("text_insertion_sent");
            SetStatus(T("Text odeslán do původního pole.", "Text sent to the original field.") + $"  {latency.Elapsed.TotalSeconds:F1} s"); bubble.SetCompleted(locale);
        }
        catch (OperationCanceledException)
        {
            if (cts.IsCancellationRequested) { Diagnostics.Mark("cancelled"); SetStatus(T("Zrušeno.", "Cancelled.")); bubble.SetCompleted(locale, true); }
            else Notify(T("Přepis překročil časový limit.", "Transcription timed out."));
        }
        catch (Exception error) { Diagnostics.Mark("failed"); Notify(error); }
        finally { audioSession.Dispose(); if (wav is not null) Array.Clear(wav); cancellation = null; busy = false; ExitActive(); }
    }
    private async Task FailCaptureAsync(AudioCapture audio, Exception error)
    {
        holdKey.Disarm(); busy = true; capture = null; recordingTimer.Stop();
        try { await audio.StopAsync(discard: true); } catch { /* Display the original device failure below. */ }
        finally { audio.Dispose(); busy = false; ExitActive(); } Notify(error);
    }
    private async Task CancelAsync()
    {
        if (cancellation is { } job) { bubble.SetProcessing(locale, true); job.Cancel(); return; }
        if (capture is not { } audio) return;
        busy = true; capture = null; recordingTimer.Stop();
        try { await audio.StopAsync(discard: true); SetStatus(T("Zrušeno.", "Cancelled.")); bubble.SetCompleted(locale, true); }
        catch (Exception error) { Notify(error); }
        finally { audio.Dispose(); busy = false; ExitActive(); }
    }
    internal async Task ExitAsync()
    {
        if (closing) return; closing = true;
        pendingHold.Stop(); capsHold.Cancel();
        WindowsInput.UnregisterHotKey(handle, WindowsInput.HotkeyId); if (cancelRegistered) WindowsInput.UnregisterHotKey(handle, CancelHotkeyId);
        if (hook != IntPtr.Zero) WindowsInput.UnhookWindowsHookEx(hook);
        await CancelAsync(); if (cancelTask is not null) await cancelTask; if (activeTask is not null) await activeTask;
        ForgetReplay();
        sessionNvidiaKey = null; NvidiaKey.Clear(); cloudApproved = false;
        recordingTimer.Stop(); tray?.Dispose(); bubble.DisposeBubble(); SystemEvents.UserPreferenceChanged -= OnSystemAppearance;
        SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Closing -= OnClosing; Close(); Application.Current.Shutdown(Environment.ExitCode);
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!closing) { e.Cancel = true; HideConfirmed(); } }
    private void NavigateClick(object sender, RoutedEventArgs e) { if (preferencesOpen && !ConfirmLeave()) return; Navigate(!preferencesOpen); }
    private void HideConfirmed() { if (ConfirmLeave()) Hide(); }
    private void BackToWorkClick(object sender, RoutedEventArgs e) => HideConfirmed();
    private void MinimizeClick(object sender, RoutedEventArgs e) => HideConfirmed();
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
