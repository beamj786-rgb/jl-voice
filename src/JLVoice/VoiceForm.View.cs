using Microsoft.Win32;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace JLVoice;

internal sealed partial class VoiceForm
{
    private readonly bool reviewMode;
    private Palette colors = Palette.Resolve("dark");
    private string uiLanguage = "cs";
    private readonly List<(Control Control, string Cs, string En)> captions = [];
    private readonly HashSet<Control> secondaryText = [];
    private readonly List<string> modelFiles = [];
    private readonly BrandCombo model = new(), threads = new(), theme = new(), interfaceLanguage = new();
    private readonly Panel overview = new(), preferences = new();
    private readonly BrandButton overviewNav = new(), settingsNav = new(), copy = new(), clear = new();
    private readonly Label shortcut = new(), microphoneState = new(), languageSummary = new(), microphoneSummary = new(), emptyResult = new();
    private readonly Label pending = new();
    private readonly PictureBox orbit = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Dictionary<string, Image> brandImages = [];
    private readonly ToolTip hints = new() { AutoPopDelay = 8000 };
    private bool binding;
    private int currentPage;
    private string T(string cs, string en) => uiLanguage == "cs" ? cs : en;

    private Label TextLabel(string cs, string en, float size = 10, string role = "body", bool dim = false)
    {
        var label = new Label { AutoSize = true, Dock = DockStyle.Top, Font = BrandFonts.Get(role, size), Margin = new Padding(0, 0, 0, 8) };
        Bind(label, cs, en); if (dim) secondaryText.Add(label); return label;
    }
    private void Bind(Control control, string cs, string en)
    {
        captions.Add((control, cs, en)); control.Text = T(cs, en);
    }
    private static TableLayoutPanel Stack()
    {
        var panel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return panel;
    }
    private static void AddRow(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Dock = DockStyle.Top;
        panel.Controls.Add(control, 0, row);
    }
    private SurfaceCard Card(Control contents)
    {
        var card = new SurfaceCard { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
        card.Controls.Add(contents); return card;
    }
    private Control Field(string cs, string en, Control control)
    {
        var field = Stack(); field.Margin = new Padding(0, 0, 0, 8);
        var label = TextLabel(cs, en, 9, dim: true); label.Margin = new Padding(0, 0, 0, 6);
        control.AccessibleName = T(cs, en); control.Dock = DockStyle.Top; control.Margin = Padding.Empty;
        AddRow(field, label); AddRow(field, control); return field;
    }
    private static TableLayoutPanel Pair(Control first, Control second, float firstPercent = 50)
    {
        var pair = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, firstPercent)); pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - firstPercent));
        first.Dock = second.Dock = DockStyle.Top; first.Margin = new Padding(0, 0, 12, 0); second.Margin = Padding.Empty;
        pair.Controls.Add(first, 0, 0); pair.Controls.Add(second, 1, 0); return pair;
    }
    private BrandButton ActionButton(string cs, string en, Action action, int width = 126, bool primary = false)
    {
        var button = new BrandButton { Width = width, Primary = primary, Margin = new Padding(0, 0, 8, 0) };
        Bind(button, cs, en); button.Click += (_, _) => action(); return button;
    }

    private void BuildInterface()
    {
        binding = true; uiLanguage = settings.UiLanguage;
        Text = "JL Voice"; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = BrandFonts.Get("body", 10); ClientSize = new Size(940, 720); MinimumSize = new Size(800, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "brand", "JLVoice.ico")); tray.Icon = Icon;

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var rail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 22, 20, 18) };
        var brand = new Panel { Dock = DockStyle.Top, Height = 92 };
        orbit.SetBounds(8, 12, 26, 26); brand.Controls.Add(orbit);
        var name = new Label { Text = "Voice", AutoSize = true, Font = BrandFonts.Get("display", 20), Location = new Point(62, 8) }; brand.Controls.Add(name);
        var owner = TextLabel("JL AUTOMATION", "JL AUTOMATION", 7.8f, "mono", true); owner.Dock = DockStyle.None; owner.Location = new Point(8, 63); brand.Controls.Add(owner);
        var navigation = Stack(); navigation.Dock = DockStyle.Top;
        overviewNav.Height = settingsNav.Height = 42; overviewNav.Margin = settingsNav.Margin = new Padding(0, 0, 8, 8);
        Bind(overviewNav, "Přehled", "Overview"); Bind(settingsNav, "Nastavení", "Settings");
        overviewNav.Click += (_, _) => Navigate(0); settingsNav.Click += (_, _) => Navigate(1);
        AddRow(navigation, overviewNav); AddRow(navigation, settingsNav);
        var railFooter = Stack(); railFooter.Dock = DockStyle.Bottom;
        AddRow(railFooter, TextLabel("OFFLINE · CPU", "OFFLINE · CPU", 8, "mono"));
        AddRow(railFooter, TextLabel("Vývojová verze 0.1", "Development preview 0.1", 8.5f, dim: true));
        rail.Controls.Add(navigation); rail.Controls.Add(brand); rail.Controls.Add(railFooter);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 10, 28, 8) };
        var context = TextLabel("PRACOVNÍ PROSTOR", "WORKSPACE", 8, "mono", true); context.Dock = DockStyle.Left; context.TextAlign = ContentAlignment.MiddleLeft; context.AutoSize = false; context.Width = 240;
        var hide = ActionButton("Na pozadí", "Hide window", Hide, 110); hide.Dock = DockStyle.Right;
        bar.Controls.Add(context); bar.Controls.Add(hide);
        var pages = new Panel { Dock = DockStyle.Fill };
        foreach (var page in new[] { overview, preferences }) { page.Dock = DockStyle.Fill; page.AutoScroll = true; page.Padding = new Padding(28, 10, 28, 24); pages.Controls.Add(page); }
        BuildOverview(); BuildPreferences(); workspace.Controls.Add(bar, 0, 0); workspace.Controls.Add(pages, 0, 1);
        shell.Controls.Add(rail, 0, 0); shell.Controls.Add(workspace, 1, 0); Controls.Add(shell);

        var menu = new ContextMenuStrip { Font = BrandFonts.Get("body", 10) };
        menu.Items.Add(T("Otevřít JL Voice", "Open JL Voice"), null, (_, _) => ShowWorkspace());
        menu.Items.Add(T("Zrušit diktování", "Cancel dictation"), null, async (_, _) => await CancelAsync());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(T("Ukončit", "Exit"), null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWorkspace();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
        save.Click += (_, _) => SaveSettings();
        result.TextChanged += (_, _) => UpdateResult();
        foreach (var combo in new ComboBox[] { language, microphone, key, model, threads, theme, interfaceLanguage })
            combo.SelectedIndexChanged += (_, _) => MarkPending();
        push.CheckedChanged += (_, _) => MarkPending();
        theme.SelectedIndexChanged += (_, _) => { if (!binding) ApplyTheme(); };
        interfaceLanguage.SelectedIndexChanged += (_, _) => { if (!binding) Localize(); };
        SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
        binding = false; ApplyTheme(); Navigate(0); UpdateSummaries(); UpdateResult(); ReadyStatus();
        if (!reviewMode)
        {
            Load += (_, _) =>
            {
                var area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            };
        }
    }

    private void BuildOverview()
    {
        var body = Stack();
        AddRow(body, TextLabel("Stačí začít mluvit.", "Just start speaking.", 28, "display"));
        AddRow(body, TextLabel("Zaměřte textové pole. Zbytek ovládnete klávesou.", "Focus a text field. Your shortcut takes it from there.", 10.5f, dim: true));
        var hero = Stack();
        status.AutoSize = true; status.Font = BrandFonts.Get("body", 12); status.Margin = new Padding(0, 0, 0, 15); status.AccessibleRole = AccessibleRole.StatusBar;
        AddRow(hero, status);
        shortcut.AutoSize = true; shortcut.Font = BrandFonts.Get("mono", 19); shortcut.Margin = new Padding(0, 0, 0, 12); AddRow(hero, shortcut);
        microphoneState.AutoSize = true; microphoneState.Font = BrandFonts.Get("body", 9.5f); secondaryText.Add(microphoneState); AddRow(hero, microphoneState);
        AddRow(body, Card(hero));

        var languageBody = Stack(); AddRow(languageBody, TextLabel("JAZYK DIKTOVÁNÍ", "DICTATION LANGUAGE", 8, "mono", true));
        languageSummary.AutoSize = true; languageSummary.Font = BrandFonts.Get("body", 12); AddRow(languageBody, languageSummary);
        var micBody = Stack(); AddRow(micBody, TextLabel("MIKROFON", "MICROPHONE", 8, "mono", true));
        microphoneSummary.AutoSize = true; microphoneSummary.Font = BrandFonts.Get("body", 10); microphoneSummary.AutoEllipsis = true; AddRow(micBody, microphoneSummary);
        var summaryPair = Pair(Card(languageBody), Card(micBody)); summaryPair.Margin = new Padding(0, 0, 0, 16); AddRow(body, summaryPair);

        var last = Stack(); AddRow(last, TextLabel("Poslední přepis", "Last transcript", 16, "display"));
        var textArea = new Panel { Height = 110, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12) };
        result.Dock = DockStyle.Fill; result.BorderStyle = BorderStyle.None; result.Font = BrandFonts.Get("body", 11); result.AccessibleName = T("Poslední přepis", "Last transcript");
        emptyResult.Dock = DockStyle.Fill; emptyResult.TextAlign = ContentAlignment.MiddleLeft; emptyResult.Font = BrandFonts.Get("body", 10.5f); secondaryText.Add(emptyResult);
        Bind(emptyResult, "Tady se zobrazí vaše slova.\nPřepis zůstává jen do dalšího diktování nebo ukončení aplikace.", "Your words will appear here.\nThe transcript stays until the next dictation or app exit.");
        textArea.Controls.Add(result); textArea.Controls.Add(emptyResult); AddRow(last, textArea);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty, WrapContents = false };
        Bind(copy, "Kopírovat", "Copy text"); copy.Width = 116; copy.Click += (_, _) =>
        {
            try { if (!string.IsNullOrWhiteSpace(result.Text)) { Clipboard.SetText(result.Text); SetStatus(T("Zkopírováno do schránky.", "Copied to clipboard.")); } }
            catch (ExternalException) { Notify(T("Schránka je právě obsazená. Zkuste to znovu.", "Clipboard is busy. Try again.")); }
        };
        Bind(clear, "Vymazat", "Clear"); clear.Width = 100; clear.Click += (_, _) => result.Clear();
        actions.Controls.AddRange([copy, clear]); AddRow(last, actions); AddRow(body, Card(last));
        AddRow(body, TextLabel("Zvuk se neukládá. Model se po přepisu uvolní z paměti.", "Audio is not saved. The model unloads after transcription.", 9, dim: true));
        overview.Controls.Add(body);
    }

    private void BuildPreferences()
    {
        var body = Stack(); AddRow(body, TextLabel("Nastavení", "Settings", 28, "display"));
        AddRow(body, TextLabel("Diktování podle vás. Bez dalších instalací.", "Dictation your way. No extra installations.", 10, dim: true));
        language.Items.AddRange([T("Automaticky · experimentální", "Automatic · experimental"), "Čeština", "English"]);
        language.SelectedIndex = settings.Language switch { "cs" => 1, "en" => 2, _ => 0 };
        RefreshMicrophones(); key.Items.AddRange(["F9", "F10", "F11", "F12"]); key.SelectedIndex = settings.Key - 0x78; push.Checked = settings.PushToTalk;
        var recognition = Stack();
        var micField = Stack();
        var micButtons = Pair(microphone, ActionButton("Obnovit", "Refresh", RefreshMicrophones, 84), 76);
        AddRow(micField, TextLabel("Mikrofon", "Microphone", 9, dim: true)); AddRow(micField, micButtons);
        AddRow(recognition, Pair(Field("Jazyk diktování", "Dictation language", language), micField, 43));
        foreach (var (file, caption) in new[] { ("ggml-base-q5_1.bin", "Base · Q5"), ("ggml-base-q8_0.bin", "Base · Q8"), ("ggml-small-q5_1.bin", "Small · Q5") })
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "models", file)) || file == settings.Model) { modelFiles.Add(file); model.Items.Add(caption); }
        model.SelectedIndex = Math.Max(0, modelFiles.IndexOf(settings.Model)); threads.Items.AddRange(["1", "2"]); threads.SelectedIndex = settings.Threads - 1;
        AddRow(recognition, Pair(Field("Model v tomto balíčku", "Bundled model", model), Field("Vlákna procesoru", "CPU threads", threads), 65));
        AddRow(recognition, TextLabel("Automatický jazyk a kvalita modelů se zatím ověřují.", "Automatic detection and model quality are still being evaluated.", 8.5f, dim: true));
        AddRow(body, Card(recognition));
        var recording = Stack();
        push.AutoSize = true; push.Font = BrandFonts.Get("body", 10); push.Margin = new Padding(0, 5, 0, 4); Bind(push, "Nahrávat při držení klávesy", "Hold the key to record");
        AddRow(recording, Pair(Field("Klávesa", "Shortcut key", key), Field("Režim nahrávání", "Recording mode", push), 32));
        AddRow(recording, TextLabel("Přepínání používá Ctrl + Alt + klávesu. Při držení stačí samotná klávesa.", "Toggle mode uses Ctrl + Alt + key. Hold mode uses the key alone.", 8.5f, dim: true));
        AddRow(body, Card(recording));
        theme.Items.AddRange([T("Tmavý", "Dark"), T("Světlý", "Light"), T("Podle systému", "System")]);
        theme.SelectedIndex = settings.Theme switch { "light" => 1, "system" => 2, _ => 0 };
        interfaceLanguage.Items.AddRange(["Čeština", "English"]); interfaceLanguage.SelectedIndex = settings.UiLanguage == "en" ? 1 : 0;
        AddRow(body, Card(Pair(Field("Vzhled", "Appearance", theme), Field("Jazyk rozhraní", "Interface language", interfaceLanguage))));
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        pending.AutoSize = true; pending.Font = BrandFonts.Get("body", 9); secondaryText.Add(pending); pending.Margin = new Padding(0, 10, 12, 0);
        save.Primary = true; save.Height = 40; Bind(save, "Uložit nastavení", "Save settings"); save.Dock = DockStyle.Top;
        footer.Controls.Add(pending, 0, 0); footer.Controls.Add(save, 1, 0); AddRow(body, footer); preferences.Controls.Add(body);
    }

    private void RefreshMicrophones()
    {
        var selected = microphone.SelectedItem?.ToString();
        microphone.Items.Clear();
        for (var i = 0; i < WaveInEvent.DeviceCount; ++i) microphone.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
        if (microphone.Items.Count == 0) { microphone.Items.Add(T("Žádný mikrofon", "No microphone")); microphone.Enabled = false; microphone.SelectedIndex = 0; }
        else { microphone.Enabled = true; microphone.SelectedIndex = selected is not null && microphone.Items.Contains(selected) ? microphone.Items.IndexOf(selected) : Math.Clamp(settings.Microphone, 0, microphone.Items.Count - 1); }
        microphone.AccessibleName = T("Mikrofon", "Microphone");
        hints.SetToolTip(microphone, microphone.SelectedItem?.ToString());
    }
    private void ShowWorkspace() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Navigate(int page)
    {
        currentPage = page; overview.Visible = page == 0; preferences.Visible = page == 1;
        (page == 0 ? overview : preferences).BringToFront(); overviewNav.Selected = page == 0; settingsNav.Selected = page == 1;
        overviewNav.Invalidate(); settingsNav.Invalidate();
    }
    private void MarkPending() { if (!binding) pending.Text = T("Máte neuložené změny.", "You have unsaved changes."); }
    private void UpdateResult() { emptyResult.Visible = string.IsNullOrEmpty(result.Text); copy.Enabled = clear.Enabled = !emptyResult.Visible; }
    private void UpdateSummaries()
    {
        shortcut.Text = settings.PushToTalk ? ((Keys)settings.Key).ToString() : $"Ctrl + Alt + {(Keys)settings.Key}";
        languageSummary.Text = settings.Language switch { "cs" => "Čeština", "en" => "English", _ => T("Automaticky", "Automatic") };
        microphoneSummary.Text = settings.Microphone >= 0 && settings.Microphone < WaveInEvent.DeviceCount ? WaveInEvent.GetCapabilities(settings.Microphone).ProductName : T("Žádný mikrofon", "No microphone");
        microphoneState.Text = settings.PushToTalk ? T("Držte klávesu a mluvte. Uvolněním spustíte přepis.", "Hold the key and speak. Release to transcribe.") : T("Prvním stiskem začnete. Druhým spustíte přepis.", "Press once to record. Press again to transcribe.");
    }
    private void SetStatus(string text) { status.Text = text; status.Parent?.PerformLayout(); }
    private void ReadyStatus()
    {
        var complete = File.Exists(Path.Combine(AppContext.BaseDirectory, "runtime", "JLVoiceAsr.exe")) && File.Exists(Path.Combine(AppContext.BaseDirectory, "models", settings.Model));
        SetStatus(!complete ? T("Chybí model nebo součást balíčku.", "Model or runtime is missing.") : WaveInEvent.DeviceCount == 0 ? T("Připojte mikrofon a obnovte seznam.", "Connect a microphone and refresh the list.") : T("Připraveno k diktování", "Ready to dictate"));
    }
    private void Localize()
    {
        uiLanguage = interfaceLanguage.SelectedIndex == 1 ? "en" : "cs";
        foreach (var (control, cs, en) in captions) control.Text = T(cs, en);
        var old = language.SelectedIndex; language.Items[0] = T("Automaticky · experimentální", "Automatic · experimental"); language.SelectedIndex = old;
        old = theme.SelectedIndex; theme.Items[0] = T("Tmavý", "Dark"); theme.Items[1] = T("Světlý", "Light"); theme.Items[2] = T("Podle systému", "System"); theme.SelectedIndex = old;
        foreach (var (control, cs, en) in captions) if (control is Label && control.Parent is TableLayoutPanel field && field.Controls.Count == 2) field.Controls[1].AccessibleName = T(cs, en);
        if (tray.ContextMenuStrip is { } menu) { menu.Items[0].Text = T("Otevřít JL Voice", "Open JL Voice"); menu.Items[1].Text = T("Zrušit diktování", "Cancel dictation"); menu.Items[3].Text = T("Ukončit", "Exit"); }
        UpdateSummaries(); if (!busy && capture is null) ReadyStatus(); MarkPending();
    }
    private string SelectedTheme => theme.SelectedIndex switch { 1 => "light", 2 => "system", _ => "dark" };
    private void ApplyTheme()
    {
        colors = Palette.Resolve(SelectedTheme);
        void Apply(Control control, Color inherited)
        {
            control.BackColor = control is SurfaceCard ? colors.Surface : control is TextBox or ComboBox ? colors.Inset : inherited;
            control.ForeColor = secondaryText.Contains(control) ? colors.Dim : colors.Ink;
            if (control is SurfaceCard card) card.BorderColor = colors.Border;
            if (control is BrandButton button) button.Colors = colors;
            if (control is BrandCombo combo) combo.Colors = colors;
            foreach (Control child in control.Controls) Apply(child, control.BackColor);
            control.Invalidate();
        }
        SuspendLayout(); Apply(this, colors.Background);
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "brand", colors.Background.GetBrightness() < .5 ? "mark-dark.png" : "mark-light.png");
        if (File.Exists(path))
        {
            if (!brandImages.TryGetValue(path, out var image)) { image = Image.FromFile(path); brandImages[path] = image; }
            orbit.Image = image;
        }
        overlay.ApplyTheme(colors); ResumeLayout(true);
        if (IsHandleCreated) ApplyTitleBar();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    private void ApplyTitleBar() { var dark = colors.Background.GetBrightness() < .5 ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, 4); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTitleBar(); }
    private void OnSystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!closing && IsHandleCreated) BeginInvoke(() => { if (!closing) ApplyTheme(); });
    }
    private void DisposeView()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemAppearanceChanged;
        hints.Dispose(); orbit.Image = null; foreach (var image in brandImages.Values) image.Dispose();
    }
}
