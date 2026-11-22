using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JLVoice;

public partial class MainWindow
{
    private static async Task SettleAsync() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Snapshot(FrameworkElement element, string path)
    {
        element.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void InvokeButton(Button button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }
    internal async Task ExportUiReviewAsync(string directory)
    {
        if (!reviewMode) throw new InvalidOperationException("Explicit UI review required.");
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var issues = new List<string>(); var states = new List<object>();
        async Task Capture(string name)
        {
            await SettleAsync(); UpdateLayout();
            Snapshot(this, Path.Combine(directory, name + ".png"));
            // A scrollable page may overflow vertically; its content must still fit horizontally.
            foreach (var page in new[] { Overview, Preferences })
                if (page.IsVisible && page.ActualWidth > PageScroll.ViewportWidth + 2) issues.Add(name + ": horizontal page overflow.");
            if (NavigationButton.ActualWidth < 30 || StatusText.ActualHeight < 1 && Overview.IsVisible) issues.Add(name + ": missing navigation/status.");
            states.Add(new { name, width = ActualWidth, height = ActualHeight, language = locale, theme = ThemeName, settings = preferencesOpen });
        }
        if (!Appearance.FontsLoaded) issues.Add("Bundled font loading failed.");
        if (ReviewAudio.IsChecked == true) issues.Add("Audio playback check must be opt-in and off on startup.");
        if (RetainAudio.IsChecked == true || ReplayButton.IsEnabled || replayWav is not null) issues.Add("Audio replay retention must be opt-in and empty at startup.");
        var replayFixture = AudioCapture.EncodePcm(new byte[16000]);
        RetainAudio.IsChecked = true; replayWav = replayFixture; ReplayButton.IsEnabled = true;
        RetainAudio.IsChecked = false;
        if (replayWav is not null || ReplayButton.IsEnabled || replayFixture.Any(value => value != 0)) issues.Add("Disabling replay retention did not erase the owned buffer.");
        Navigate(true); ShortcutKey.SelectedValue = 0x78; PushToTalk.IsChecked = false;
        InvokeButton(SaveButton); await SettleAsync();
        if (settings.Key != 0x78 || settings.PushToTalk || !PushToTalk.IsEnabled) issues.Add("F9 toggle selection did not round-trip through settings.");
        ShortcutKey.SelectedValue = DictationKeys.CapsLock; InvokeButton(SaveButton); await SettleAsync();
        if (settings.Key != DictationKeys.CapsLock || !settings.PushToTalk || PushToTalk.IsEnabled || ShortcutKey.ActualHeight > 45) issues.Add("Caps Lock selection did not enforce hold mode or its caption overflowed.");
        var smallIndex = modelFiles.IndexOf("ggml-small-q5_1.bin");
        if (smallIndex >= 0)
        {
            Model.SelectedIndex = smallIndex; InvokeButton(SaveButton); await SettleAsync();
            if (settings.Model != "ggml-small-q5_1.bin") issues.Add("Small model selection was not saved.");
            Model.SelectedIndex = modelFiles.IndexOf("ggml-base-q5_1.bin"); InvokeButton(SaveButton); await SettleAsync();
            if (settings.Model != "ggml-base-q5_1.bin") issues.Add("Base model selection was not restored.");
        }
        foreach (var light in new[] { false, true })
        {
            Theme.SelectedIndex = light ? 1 : 0; UiLanguage.SelectedIndex = 0; Width = 650; Height = 430; Navigate(false);
            await Capture(light ? "light-overview" : "dark-overview");
            Navigate(true); Height = 650; await Capture(light ? "light-settings" : "dark-settings");
        }
        Theme.SelectedIndex = 0; Navigate(false); Width = 650; Height = 620;
        ResultText.Text = "UI TEST · Toto je ukázka přepisu s českou diakritikou.\nMixed Czech/English: otevři Visual Studio Code a spusť build.";
        await Capture("transcript-fixture");
        InvokeButton(ClearButton); await SettleAsync();
        if (ResultText.Text.Length != 0 || LastResult.IsVisible) issues.Add("Clearing did not hide the last transcript.");
        Width = 650; Height = 600;
        Notify(new AsrStartException(4551, new System.ComponentModel.Win32Exception(4551, @"C:\Users\FixtureUser\runtime\JLVoiceAsr.exe policy blocked")));
        NoticeBody.Text = "UI TEST · simulovaná chyba Windows 4551.\n" + NoticeBody.Text;
        await Capture("policy-block-fixture");
        Width = 540; Height = 440; await Capture("compact-policy-block-fixture");
        if (!NoticePanel.IsVisible || StatusText.ActualHeight > 65 || (StatusText.Text + NoticeBody.Text + NoticeDetailsText.Text).Contains("FixtureUser")) issues.Add("Policy error layout leaked the profile path or lost its bounded heading.");
        ReadyStatus(); ResetBubble();
        UiLanguage.SelectedIndex = 1; Width = 540; Height = 440; Navigate(false); await Capture("compact-english-overview");
        Navigate(true); await Capture("compact-english-settings"); Advanced.IsExpanded = true; await SettleAsync(); PageScroll.ScrollToEnd(); await Capture("compact-english-settings-bottom"); PageScroll.ScrollToTop();
        DictationLanguage.Focus(); var originalFocus = Keyboard.FocusedElement;
        if (!DictationLanguage.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) || Keyboard.FocusedElement == originalFocus) issues.Add("Keyboard traversal failed.");
        await ExportBubbleReviewAsync(directory, issues);
        if (capture is not null || recordingTimer.IsEnabled || ResultText.Text.Length != 0) issues.Add("UI review unexpectedly activated capture or retained text.");
        Close(); await SettleAsync(); if (IsVisible) issues.Add("Closing did not hide to the tray.");
        File.WriteAllText(Path.Combine(directory, "review.json"), JsonSerializer.Serialize(new
        {
            framework = "WPF", software_rendering = true, fonts_loaded = Appearance.FontsLoaded, states, issues,
            limits = new[] { "Hosted Windows render/focus checks, not physical Windows 11 DPI or screen-reader qualification.", "Recording states use synthetic PCM; transcript fixture is authored test text, not ASR output.", "No microphone, speech-quality, external destination app or representative 8 GB acceptance was performed.", "Policy-block screenshots simulate Win32 4551; enforced Smart App Control was not tested on the hosted runner." }
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (issues.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
    }
    private async Task ExportBubbleReviewAsync(string directory, List<string> issues)
    {
        var field = new TextBox(); var probe = new Window { Title = "JL Voice focus probe", Width = 320, Height = 100, Content = field };
        probe.Show(); probe.Activate(); field.Focus(); await SettleAsync(); var original = WindowsInput.CaptureTarget();
        bool FocusPreserved() => WindowsInput.CaptureTarget() == original && Keyboard.FocusedElement == field;
        var test = new VoiceBubble(); test.PlaceForWindow(NativeDesktop.Handle(probe)); test.SetIdle(Shortcut, "cs");
        try
        {
            test.Show(); await SettleAsync(); if (!FocusPreserved()) issues.Add("Idle bubble stole focus.");
            test.Hide(); test.Show(); await SettleAsync(); if (!FocusPreserved()) issues.Add("Showing the bubble again stole focus.");
            if (test.AnimationRunning || test.TransientRunning) issues.Add("Idle bubble has an active timer.");
            if (NativeDesktop.SendMessage(NativeDesktop.Handle(test), 0x21, NativeDesktop.Handle(probe), IntPtr.Zero) != new IntPtr(3)) issues.Add("Bubble mouse activation is not disabled.");
            var recordIntents = 0; var cancelIntents = 0;
            test.RecordRequested += () => ++recordIntents; test.CancelRequested += () => ++cancelIntents;
            InvokeButton(test.MainAction); await SettleAsync();
            if (recordIntents != 1 || !FocusPreserved()) issues.Add("Bubble invocation failed or stole focus.");
            var area = NativeDesktop.WorkingArea(NativeDesktop.Handle(probe)); var dpi = VisualTreeHelper.GetDpi(test);
            if (Math.Abs(test.Left * dpi.DpiScaleX + test.Width * dpi.DpiScaleX / 2 - (area.Left + area.Right) / 2.0) > 2) issues.Add("Bubble is not horizontally centred.");
            if (Math.Abs(test.Top * dpi.DpiScaleY + test.Height * dpi.DpiScaleY - (area.Bottom - 18 * dpi.DpiScaleY)) > 2) issues.Add("Bubble is not above the taskbar.");
            var canvas = new Canvas { Width = 760, Height = 510, Background = (Brush)FindResource("BackgroundBrush") };
            void Label(string text, double x, double y, bool title = false)
            {
                var label = new TextBlock { Text = text, FontSize = title ? 28 : 12, Foreground = (Brush)FindResource(title ? "InkBrush" : "DimBrush"), FontFamily = (FontFamily)FindResource(title ? "DisplayFont" : "BodyFont") };
                Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label);
            }
            Label("Bublina diktování · WPF", 24, 18, true); Label("UI TEST · záznamové stavy používají syntetické PCM", 24, 62);
            var names = new[] { "V klidu · mikrofon vypnutý", "Po najetí · zkratka a nastavení", "Nahrávání · ticho", "Nahrávání · naměřená úroveň PCM", "Přepisování · bez falešného procenta", "Dokončeno", "Zrušeno", "Chyba · text zůstává v aplikaci" };
            var pcm = new byte[1600];
            for (var i = 0; i < pcm.Length / 2; ++i) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(16000 * Math.Sin(i * 2 * Math.PI * 440 / 16000)));
            var measured = PcmLevel.Rms(pcm);
            for (var i = 0; i < names.Length; ++i)
            {
                if (i <= 1) { test.SetIdle(Shortcut, "cs"); test.SetHover(i == 1); }
                else if (i <= 3) { test.SetRecording("cs"); for (var j = 0; j < 9; ++j) test.UpdateAudio(i == 2 ? 0 : measured * (j + 1) / 9, TimeSpan.FromSeconds(12)); }
                else if (i == 4) test.SetProcessing("cs"); else if (i <= 6) test.SetCompleted("cs", i == 6); else test.SetError("cs", true);
                await SettleAsync(); if (!FocusPreserved()) issues.Add($"Bubble state {i} stole focus.");
                var path = Path.Combine(directory, $"bubble-{i}.png"); Snapshot(test, path);
                var image = new Image { Source = new BitmapImage(new Uri(path)), Width = test.Width, Height = test.Height };
                var x = 24 + i % 2 * 376; var y = 106 + i / 2 * 94; Label(names[i], x, y); Canvas.SetLeft(image, x); Canvas.SetTop(image, y + 24); canvas.Children.Add(image);
            }
            test.SetRecording("cs"); InvokeButton(test.MainAction); InvokeButton(test.SideAction); await SettleAsync();
            if (recordIntents != 2 || cancelIntents != 1 || !FocusPreserved()) issues.Add("Stop/cancel actions failed or stole focus.");
            test.SetCompleted("cs"); await Task.Delay(1700); await SettleAsync();
            if (test.Activity != BubbleActivity.Idle || test.AnimationRunning || test.TransientRunning || !test.MeterCleared) issues.Add("Feedback did not settle to static idle.");
            canvas.Measure(new Size(760, 510)); canvas.Arrange(new Rect(0, 0, 760, 510)); Snapshot(canvas, Path.Combine(directory, "bubble-states-fixture.png"));
        }
        finally { test.DisposeBubble(); probe.Close(); }
    }
}
