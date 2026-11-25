using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;

namespace JLVoice;

public partial class MainWindow
{
    private string? sessionNvidiaKey;
    private bool cloudApproved;
    private string BubbleShortcut => settings.Provider == "nvidia" ? Shortcut + " · NVIDIA" : Shortcut;
    private void InitializeCloudControls()
    {
        Provider.ItemsSource = new[] { "", "" }; Provider.SelectedIndex = settings.Provider == "nvidia" ? 1 : 0;
        CloudConsent.IsChecked = settings.CloudConsentVersion == 1;
        RememberCloudKey.IsChecked = settings.RememberNvidiaKey;
        cloudApproved = settings.Provider == "nvidia" && settings.CloudConsentVersion == 1;
        if (!reviewMode && settings.RememberNvidiaKey)
        {
            try { sessionNvidiaKey = WindowsCredentialStore.Load(); }
            catch { sessionNvidiaKey = null; }
        }
        Provider.SelectionChanged += (_, _) => { UpdateCloudControls(); MarkPending(); };
        NvidiaKey.PasswordChanged += (_, _) => { UpdateCloudKeyStatus(); MarkPending(); };
        RememberCloudKey.Checked += (_, _) => MarkPending(); RememberCloudKey.Unchecked += (_, _) => MarkPending();
        CloudConsent.Checked += (_, _) => MarkPending();
    }
    private void LocalizeCloud()
    {
        var selection = Provider.SelectedIndex;
        ProviderLabel.Text = T("Rozpoznávání", "Speech recognition");
        Provider.ItemsSource = new[] { T("Offline · Whisper (CPU)", "Offline · Whisper (CPU)"), "NVIDIA cloud · Parakeet TDT" };
        Provider.SelectedIndex = selection;
        CloudExplanation.Text = T("Nahraný zvuk se odešle do NVIDIA přes internet. Platí podmínky a limity vašeho NVIDIA účtu. Pro tento test vyberte Češtinu nebo English; automatický jazyk je zatím pouze offline.",
            "Recorded audio is sent to NVIDIA over the internet. Your NVIDIA account terms and limits apply. Choose Czech or English for this test; Automatic is currently offline only.");
        CloudKeyLabel.Text = "NVIDIA API key";
        RememberCloudKey.Content = Wrapped(T("Zapamatovat klíč ve Správci pověření Windows", "Remember key in Windows Credential Manager"));
        CloudConsent.Content = Wrapped(T("Souhlasím s odesíláním diktování i testovacího přepisu do NVIDIA.", "I allow dictation and test replay audio to be sent to NVIDIA."));
        ForgetCloudKey.Content = T("Zapomenout klíč", "Forget key");
        UpdateCloudControls();
    }
    private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
    private void UpdateCloudControls()
    {
        var cloud = Provider.SelectedIndex == 1;
        CloudPanel.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
        Model.IsEnabled = Threads.IsEnabled = !cloud;
        UpdateCloudKeyStatus(); AdjustSize();
    }
    private void UpdateCloudKeyStatus()
    {
        CloudKeyStatus.Text = NvidiaKey.Password.Length > 0 ? T("Nový klíč se použije po Uložit.", "New key applies after Save.")
            : string.IsNullOrEmpty(sessionNvidiaKey) ? T("Klíč není připravený.", "No key available.")
            : settings.RememberNvidiaKey ? T("Klíč načtený; ponechte pole prázdné, pokud jej neměníte.", "Key available; leave the field empty to keep it.")
            : T("Klíč je připravený pouze pro toto spuštění.", "Key is available for this session only.");
    }
    private string? PrepareCloudSettings(Settings next)
    {
        var key = string.IsNullOrWhiteSpace(NvidiaKey.Password) ? sessionNvidiaKey : NvidiaKey.Password.Trim();
        if (next.Provider == "nvidia")
        {
            if (CloudConsent.IsChecked != true) throw new CloudSpeechException(CloudFailure.Consent);
            if (next.Language == "auto") throw new CloudSpeechException(CloudFailure.Language);
            if (!NvidiaSpeechClient.ValidKey(key)) throw new CloudSpeechException(CloudFailure.Key);
        }
        next.Validate();
        if (!reviewMode)
        {
            if (next.RememberNvidiaKey && !string.IsNullOrEmpty(key)) WindowsCredentialStore.Save(key);
            else if (settings.RememberNvidiaKey && !next.RememberNvidiaKey) WindowsCredentialStore.Forget();
        }
        return key;
    }
    private void CloudConsentRevoked(object sender, RoutedEventArgs e)
    {
        if (binding) return;
        cloudApproved = false; RequestCancel(); MarkPending();
    }
    private void ForgetCloudKeyClick(object sender, RoutedEventArgs e)
    {
        cloudApproved = false; RequestCancel(); sessionNvidiaKey = null; NvidiaKey.Clear(); RememberCloudKey.IsChecked = false;
        try { if (!reviewMode) WindowsCredentialStore.Forget(); }
        catch { Notify(T("Windows nemohl odstranit uložený klíč. Zkontrolujte Správce pověření.", "Windows could not remove the saved key. Check Credential Manager.")); }
        UpdateCloudKeyStatus(); MarkPending();
        if (settings.Provider == "nvidia") ReadyStatus();
    }
    private void RequireCloudReady()
    {
        if (settings.Provider != "nvidia") return;
        if (!cloudApproved || settings.CloudConsentVersion != 1) throw new CloudSpeechException(CloudFailure.Consent);
        if (!NvidiaSpeechClient.ValidKey(sessionNvidiaKey)) throw new CloudSpeechException(CloudFailure.Key);
        if (settings.Language == "auto") throw new CloudSpeechException(CloudFailure.Language);
    }
    private async Task<AsrEngine.Result> TranscribeSelectedAsync(byte[] wav, CancellationToken token)
    {
        if (reviewMode) throw new InvalidOperationException("UI review cannot transcribe or send audio.");
        if (settings.Provider == "offline") return await AsrEngine.TranscribeAsync(wav, settings, token);
        RequireCloudReady();
        try
        {
            Diagnostics.Mark("cloud_request_started", new { provider = "nvidia", language = settings.Language });
            var result = await NvidiaSpeechClient.TranscribeAsync(wav, sessionNvidiaKey!, settings.Language, cloudApproved, token);
            token.ThrowIfCancellationRequested();
            using var reader = new WaveFileReader(new MemoryStream(wav, writable: false));
            Diagnostics.Mark("cloud_request_completed", new { provider = "nvidia", request_ms = result.RequestMs, audio_seconds = reader.TotalTime.TotalSeconds });
            // The hosted endpoint does not report a detected language: Detected stays null (unknown), never the requested value.
            return new(result.Text, "NVIDIA cloud · Parakeet TDT", settings.Language, reader.TotalTime.TotalSeconds, null, result.RequestMs, Backend: "nvidia");
        }
        catch (CloudSpeechException error)
        {
            Diagnostics.Mark("cloud_request_failed", new { provider = "nvidia", category = error.Failure.ToString() }); throw;
        }
    }
}
