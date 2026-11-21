using Microsoft.Win32;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace JLVoice;

internal sealed partial class VoiceForm
{
    private readonly bool reviewMode;
    private Palette colors = Palette.Resolve("dark");
    private string uiLanguage = "cs";
    private readonly List<(Control Control, string Cs, string En)> captions = [], fieldNames = [];
    private readonly HashSet<Control> secondaryText = [];
    private readonly List<string> modelFiles = [];
    private readonly BrandCombo model = new(), threads = new(), theme = new(), interfaceLanguage = new();
    private readonly Panel overview = new(), preferences = new();
    private readonly BrandButton back = new(), settingsButton = new(), copy = new(), clear = new(), advancedToggle = new();
    private readonly Label shortcut = new(), microphoneState = new(), languageSummary = new(), emptyResult = new(), pending = new();
    private readonly TableLayoutPanel lastResult = Stack(), advanced = Stack();
    private readonly PictureBox orbit = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Dictionary<string, Image> brandImages = [];
    private readonly ToolTip hints = new() { AutoPopDelay = 8000 };
    private ToolStripMenuItem? bubbleMenu;
    private bool binding, advancedOpen;
    private int currentPage;
    private string T(string cs, string en) => uiLanguage == "cs" ? cs : en;
    private Label TextLabel(string cs, string en, float size = 10, string role = "body", bool dim = false)
    {
        var label = new Label { AutoSize = true, Dock = DockStyle.Top, Font = BrandFonts.Get(role, size), Margin = new Padding(0, 0, 0, 8) };
        Bind(label, cs, en); if (dim) secondaryText.Add(label); return label;
    }
    private void Bind(Control control, string cs, string en) { captions.Add((control, cs, en)); control.Text = T(cs, en); }
    private static TableLayoutPanel Stack()
    {
        var p = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return p;
    }
    private static void AddRow(TableLayoutPanel p, Control c)
    { var row = p.RowCount++; p.RowStyles.Add(new RowStyle(SizeType.AutoSize)); c.Dock = DockStyle.Top; p.Controls.Add(c, 0, row); }
    private Control Field(string cs, string en, Control c)
    {
        var p = Stack(); p.Margin = new Padding(0, 0, 0, 14); var label = TextLabel(cs, en, 9, dim: true);
        if (c is not CheckBox) { c.AccessibleName = T(cs, en); fieldNames.Add((c, cs, en)); }
        c.Dock = DockStyle.Top; c.Margin = Padding.Empty; AddRow(p, label); AddRow(p, c); return p;
    }
    private static TableLayoutPanel Pair(Control a, Control b, float percent = 50)
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - percent));
        a.Dock = b.Dock = DockStyle.Top; a.Margin = new Padding(0, 0, 16, 0); b.Margin = Padding.Empty; p.Controls.Add(a, 0, 0); p.Controls.Add(b, 1, 0); return p;
    }
    private BrandButton ActionButton(string cs, string en, Action action, int width = 126, bool primary = false)
    {
        var b = new BrandButton { Width = width, Primary = primary, Ghost = !primary, Margin = new Padding(0, 0, 8, 0) };
        Bind(b, cs, en); b.Click += (_, _) => action(); return b;
    }
    private void BuildInterface()
    {
        binding = true; uiLanguage = settings.UiLanguage; Text = "JL Voice";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = BrandFonts.Get("body", 10); ClientSize = new Size(640, 420); MinimumSize = new Size(560, 410); MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "brand", "JLVoice.ico")); tray.Icon = Icon;
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(28, 20, 28, 20) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        orbit.SetBounds(0, 12, 22, 22); header.Controls.Add(orbit);
        header.Controls.Add(new Label { Text = "JL Voice", AutoSize = true, Font = BrandFonts.Get("display", 18), Location = new Point(50, 7) });
        settingsButton.Ghost = back.Ghost = true; settingsButton.Width = 112; back.Width = 112; settingsButton.Height = back.Height = 38;
        Bind(settingsButton, "Nastavení", "Settings"); Bind(back, "Zpět", "Back"); settingsButton.Dock = back.Dock = DockStyle.Right;
        settingsButton.Click += (_, _) => Navigate(1); back.Click += (_, _) => Navigate(0); header.Controls.Add(settingsButton); header.Controls.Add(back);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        foreach (var p in new[] { overview, preferences }) { p.Dock = DockStyle.Fill; p.AutoScroll = true; p.Padding = new Padding(0, 8, 0, 12); pages.Controls.Add(p); }
        BuildOverview(); BuildPreferences(); shell.Controls.Add(header, 0, 0); shell.Controls.Add(pages, 0, 1); Controls.Add(shell);
        var menu = new ContextMenuStrip { Font = BrandFonts.Get("body", 10) };
        menu.Items.Add(T("Otevřít JL Voice", "Open JL Voice"), null, (_, _) => { ShowWorkspace(); Navigate(0); });
        menu.Items.Add(T("Nastavení", "Settings"), null, (_, _) => { ShowWorkspace(); Navigate(1); });
        bubbleMenu = new ToolStripMenuItem(); bubbleMenu.Click += (_, _) => { idleBubbleOverride = !(idleBubbleOverride ?? settings.ShowIdleBubble); if (idleBubbleOverride == true) Hide(); RefreshBubblePresence(); }; menu.Items.Add(bubbleMenu);
        menu.Items.Add(T("Zrušit diktování", "Cancel dictation"), null, (_, _) => RequestCancel());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(T("Ukončit JL Voice", "Exit JL Voice"), null, (_, _) => ExitApplication()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => { ShowWorkspace(); Navigate(0); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
        save.Click += (_, _) => SaveSettings(); result.TextChanged += (_, _) => UpdateResult();
        foreach (var c in new ComboBox[] { language, microphone, key, model, threads, theme, interfaceLanguage }) c.SelectedIndexChanged += (_, _) => MarkPending();
        push.CheckedChanged += (_, _) => MarkPending(); idleBubble.CheckedChanged += (_, _) => MarkPending();
        theme.SelectedIndexChanged += (_, _) => { if (!binding) ApplyTheme(); };
        interfaceLanguage.SelectedIndexChanged += (_, _) => { if (!binding) Localize(); };
        SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
        binding = false; ApplyTheme(); Navigate(0); UpdateSummaries(); UpdateResult(); ReadyStatus(); overlay.SetIdle(CurrentShortcut, uiLanguage);
    }
    private void BuildOverview()
    {
        var body = Stack(); status.Font = BrandFonts.Get("display", 17); status.Margin = new Padding(0, 4, 0, 16); status.AccessibleRole = AccessibleRole.StatusBar; AddRow(body, status);
        shortcut.AutoSize = true; shortcut.Font = BrandFonts.Get("mono", 15); shortcut.Margin = new Padding(0, 0, 0, 14); AddRow(body, shortcut);
        microphoneState.AutoSize = true; microphoneState.Font = BrandFonts.Get("body", 10); secondaryText.Add(microphoneState); AddRow(body, microphoneState);
        languageSummary.AutoSize = true; languageSummary.Font = BrandFonts.Get("body", 9); secondaryText.Add(languageSummary); languageSummary.Margin = new Padding(0, 0, 0, 18); AddRow(body, languageSummary);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 22), WrapContents = false };
        actions.Controls.Add(ActionButton("Zpět k práci", "Back to work", Hide, 152, true)); AddRow(body, actions);
        Bind(emptyResult, "Poslední přepis se objeví tady.", "Your last transcript will appear here."); emptyResult.AutoSize = true; emptyResult.Font = BrandFonts.Get("body", 9.5f); secondaryText.Add(emptyResult); AddRow(body, emptyResult);
        var last = Stack();
        var title = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, Margin = Padding.Empty };
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84)); title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        title.Controls.Add(TextLabel("Poslední přepis", "Last transcript", 13, "display"), 0, 0);
        Bind(copy, "Kopírovat", "Copy"); Bind(clear, "Vymazat", "Clear"); copy.Ghost = clear.Ghost = true; copy.Dock = clear.Dock = DockStyle.Top;
        copy.Click += (_, _) => { try { if (!string.IsNullOrWhiteSpace(result.Text)) Clipboard.SetText(result.Text); } catch (ExternalException) { Notify(T("Schránka je obsazená.", "Clipboard is busy.")); } };
        clear.Click += (_, _) => result.Clear(); title.Controls.Add(copy, 1, 0); title.Controls.Add(clear, 2, 0); AddRow(last, title);
        result.Height = 130; result.BorderStyle = BorderStyle.None; result.Font = BrandFonts.Get("body", 11); result.Margin = new Padding(0, 8, 0, 16); AddRow(last, result);
        lastResult.AutoSize = true; lastResult.Dock = DockStyle.Top; lastResult.Controls.Add(last); AddRow(body, lastResult);
        var privacy = TextLabel("Mikrofon se zapne až při nahrávání. Zvuk se neukládá.", "Microphone activates only when recording. Audio is not saved.", 8.5f, dim: true); privacy.Margin = new Padding(0, 16, 0, 0); AddRow(body, privacy);
        overview.Controls.Add(body);
    }
    private void BuildPreferences()
    {
        var body = Stack();
        language.Items.AddRange([T("Automaticky · experimentální", "Automatic · experimental"), "Čeština", "English"]); language.SelectedIndex = settings.Language switch { "cs" => 1, "en" => 2, _ => 0 };
        RefreshMicrophones(); key.Items.AddRange(["F9", "F10", "F11", "F12"]); key.SelectedIndex = settings.Key - 0x78; push.Checked = settings.PushToTalk; idleBubble.Checked = settings.ShowIdleBubble;
        var micRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = Padding.Empty };
        micRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); micRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        microphone.Dock = DockStyle.Top; micRow.Controls.Add(microphone, 0, 0); var refresh = ActionButton("Obnovit", "Refresh", RefreshMicrophones, 80); refresh.Dock = DockStyle.Top; micRow.Controls.Add(refresh, 1, 0);
        AddRow(body, Pair(Field("Jazyk diktování", "Dictation language", language), Field("Mikrofon", "Microphone", micRow), 42));
        push.Font = idleBubble.Font = BrandFonts.Get("body", 10); Bind(push, "Držet klávesu pro nahrávání", "Hold key to record");
        AddRow(body, Pair(Field("Klávesa", "Shortcut key", key), Field("Režim", "Mode", push), 32));
        AddRow(body, TextLabel("Přepínání: Ctrl + Alt + klávesa. Při držení stačí samotná klávesa.", "Toggle: Ctrl + Alt + key. Hold mode uses the key alone.", 8.5f, dim: true));
        Bind(idleBubble, "Zobrazovat bublinu i v klidu", "Show the bubble when idle"); idleBubble.Margin = new Padding(0, 8, 0, 20); AddRow(body, idleBubble);
        theme.Items.AddRange([T("Tmavý", "Dark"), T("Světlý", "Light"), T("Podle systému", "System")]); theme.SelectedIndex = settings.Theme switch { "light" => 1, "system" => 2, _ => 0 };
        interfaceLanguage.Items.AddRange(["Čeština", "English"]); interfaceLanguage.SelectedIndex = settings.UiLanguage == "en" ? 1 : 0;
        AddRow(body, Pair(Field("Vzhled", "Appearance", theme), Field("Jazyk rozhraní", "Interface language", interfaceLanguage)));
        advancedToggle.Ghost = true; Bind(advancedToggle, "Pokročilé možnosti", "Advanced options"); advancedToggle.Height = 36; advancedToggle.Click += (_, _) => { advancedOpen = !advancedOpen; advanced.Visible = advancedOpen; AdjustWindowSize(); }; AddRow(body, advancedToggle);
        var advancedBody = Stack();
        foreach (var (file, caption) in new[] { ("ggml-base-q5_1.bin", "Base · Q5"), ("ggml-base-q8_0.bin", "Base · Q8"), ("ggml-small-q5_1.bin", "Small · Q5") })
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "models", file)) || file == settings.Model) { modelFiles.Add(file); model.Items.Add(caption); }
        model.SelectedIndex = Math.Max(0, modelFiles.IndexOf(settings.Model)); threads.Items.AddRange(["1", "2"]); threads.SelectedIndex = settings.Threads - 1;
        AddRow(advancedBody, Pair(Field("Model v balíčku", "Bundled model", model), Field("CPU vlákna", "CPU threads", threads), 65));
        AddRow(advancedBody, TextLabel("Automatický jazyk a kvalita modelů se zatím ověřují.", "Automatic detection and model quality are still being evaluated.", 8.5f, dim: true));
        advanced.AutoSize = true; advanced.Dock = DockStyle.Top; advanced.Controls.Add(advancedBody); advanced.Visible = false; AddRow(body, advanced);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 18, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        pending.AutoSize = true; pending.Font = BrandFonts.Get("body", 9); secondaryText.Add(pending); pending.Margin = new Padding(0, 10, 12, 0);
        save.Primary = true; save.Height = 40; Bind(save, "Uložit", "Save changes"); save.Dock = DockStyle.Top;
        footer.Controls.Add(pending, 0, 0); footer.Controls.Add(save, 1, 0); AddRow(body, footer); preferences.Controls.Add(body);
    }
    private void RefreshMicrophones()
    {
        var selected = microphone.SelectedItem?.ToString(); microphone.Items.Clear();
        for (var i = 0; i < WaveInEvent.DeviceCount; ++i) microphone.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
        if (microphone.Items.Count == 0) { microphone.Items.Add(T("Žádný mikrofon", "No microphone")); microphone.Enabled = false; microphone.SelectedIndex = 0; }
        else { microphone.Enabled = true; microphone.SelectedIndex = selected is not null && microphone.Items.Contains(selected) ? microphone.Items.IndexOf(selected) : Math.Clamp(settings.Microphone, 0, microphone.Items.Count - 1); }
        microphone.AccessibleName = T("Mikrofon", "Microphone"); hints.SetToolTip(microphone, microphone.SelectedItem?.ToString());
    }
    private void ShowWorkspace() { Show(); WindowState = FormWindowState.Normal; Activate(); AdjustWindowSize(); }
    private void Navigate(int page)
    {
        currentPage = page; overview.Visible = page == 0; preferences.Visible = page == 1; (page == 0 ? overview : preferences).BringToFront();
        back.Visible = page == 1; settingsButton.Visible = page == 0; AdjustWindowSize();
    }
    private void AdjustWindowSize()
    {
        if (reviewMode || !IsHandleCreated) return;
        var area = Screen.FromControl(this).WorkingArea; var height = (currentPage == 1 ? advancedOpen ? 720 : 630 : result.TextLength > 0 ? 600 : 420) * DeviceDpi / 96;
        Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(height + Height - ClientSize.Height, area.Height - 24));
        if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
    }
    private void MarkPending() { if (!binding) pending.Text = T("Neuložené změny", "Unsaved changes"); }
    private void UpdateResult() { var hasText = !string.IsNullOrEmpty(result.Text); lastResult.Visible = hasText; emptyResult.Visible = !hasText; copy.Enabled = clear.Enabled = hasText; if (Visible) AdjustWindowSize(); }
    private void UpdateSummaries()
    {
        shortcut.Text = CurrentShortcut;
        languageSummary.Text = T("Jazyk: ", "Language: ") + (settings.Language switch { "cs" => "Čeština", "en" => "English", _ => T("Automaticky", "Automatic") });
        microphoneState.Text = T("Zaměřte textové pole. Použijte zkratku nebo bublinu dole na obrazovce.", "Focus a text field. Use the shortcut or the bubble at the bottom of your screen.");
    }
    private void SetStatus(string text) { status.Text = text; status.Parent?.PerformLayout(); }
    private void ReadyStatus()
    {
        var complete = File.Exists(Path.Combine(AppContext.BaseDirectory, "runtime", "JLVoiceAsr.exe")) && File.Exists(Path.Combine(AppContext.BaseDirectory, "models", settings.Model));
        SetStatus(!complete ? T("Chybí součást balíčku.", "A package component is missing.") : WaveInEvent.DeviceCount == 0 ? T("Připojte mikrofon.", "Connect a microphone.") : T("Připraveno, až budete vy.", "Ready when you are."));
    }
    private void UpdateBubbleMenu()
    {
        if (bubbleMenu is null) return;
        bubbleMenu.Text = (idleBubbleOverride ?? settings.ShowIdleBubble) ? T("Skrýt bublinu v klidu", "Hide idle bubble") : T("Zobrazit bublinu", "Show bubble");
    }
    private void Localize()
    {
        uiLanguage = interfaceLanguage.SelectedIndex == 1 ? "en" : "cs";
        foreach (var (c, cs, en) in captions) c.Text = T(cs, en);
        language.Items[0] = T("Automaticky · experimentální", "Automatic · experimental");
        theme.Items[0] = T("Tmavý", "Dark"); theme.Items[1] = T("Světlý", "Light"); theme.Items[2] = T("Podle systému", "System");
        if (WaveInEvent.DeviceCount == 0) microphone.Items[0] = T("Žádný mikrofon", "No microphone");
        foreach (var (c, cs, en) in fieldNames) c.AccessibleName = T(cs, en);
        microphone.AccessibleName = T("Mikrofon", "Microphone"); result.AccessibleName = T("Poslední přepis", "Last transcript");
        if (tray.ContextMenuStrip is { } menu) { menu.Items[0].Text = T("Otevřít JL Voice", "Open JL Voice"); menu.Items[1].Text = T("Nastavení", "Settings"); menu.Items[3].Text = T("Zrušit diktování", "Cancel dictation"); menu.Items[5].Text = T("Ukončit JL Voice", "Exit JL Voice"); }
        UpdateBubbleMenu(); UpdateSummaries(); if (!busy && capture is null) ReadyStatus(); MarkPending();
    }
    private string SelectedTheme => theme.SelectedIndex switch { 1 => "light", 2 => "system", _ => "dark" };
    private void ApplyTheme()
    {
        colors = Palette.Resolve(SelectedTheme); var mode = !SystemInformation.HighContrast && colors.Background.GetBrightness() < .5 ? SystemColorMode.Dark : SystemColorMode.Classic;
        var changed = Application.ColorMode != mode; Application.SetColorMode(mode);
        void Apply(Control c, Color inherited)
        {
            c.BackColor = c is TextBox or ComboBox ? colors.Inset : inherited; c.ForeColor = secondaryText.Contains(c) ? colors.Dim : colors.Ink;
            if (c is BrandButton b) b.Colors = colors;
            if (c is BrandCombo combo) { combo.Colors = colors; if (changed) combo.RefreshNativeTheme(); }
            if (changed && c is BrandCheckBox check) check.RefreshNativeTheme();
            foreach (Control child in c.Controls) Apply(child, c.BackColor); c.Invalidate();
        }
        SuspendLayout(); Apply(this, colors.Background);
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "brand", colors.Background.GetBrightness() < .5 ? "mark-dark.png" : "mark-light.png");
        if (File.Exists(path)) { if (!brandImages.TryGetValue(path, out var image)) { image = Image.FromFile(path); brandImages[path] = image; } orbit.Image = image; }
        overlay.ApplyTheme(colors); ResumeLayout(true); if (IsHandleCreated) ApplyTitleBar();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    private void ApplyTitleBar() { var dark = colors.Background.GetBrightness() < .5 ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, 4); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTitleBar(); }
    private void OnSystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e) { PostToUi(ApplyTheme); }
    private void DisposeView()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemAppearanceChanged; hints.Dispose(); orbit.Image = null; foreach (var image in brandImages.Values) image.Dispose();
    }
}
