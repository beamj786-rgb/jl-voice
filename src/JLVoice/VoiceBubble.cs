using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace JLVoice;

internal enum BubbleActivity { Idle, Recording, Processing, Success, Cancelled, Error }

// A projection of the controller's state. It never opens audio or runs recognition.
internal sealed class VoiceBubble : Form
{
    private Palette colors = Palette.Resolve("dark");
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer transient = new() { Interval = 1500 };
    private readonly ToolTip hint = new();
    private readonly float[] levels = new float[9];
    private BubbleActivity activity;
    private bool hovered, placing;
    private int frame;
    private string caption = "", shortcut = "Ctrl + Alt + F9", locale = "cs";
    private Screen? screen;
    internal BubbleActivity Activity => activity;
    internal bool AnimationRunning => animation.Enabled;
    internal bool TransientRunning => transient.Enabled;
    internal bool Hovered => hovered;
    internal float[] MeterLevels => (float[])levels.Clone();
    public event Action? RecordRequested, CancelRequested, SettingsRequested, DetailsRequested, IdleHideRequested, Settled;
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint value, ref int result, uint flags);
    private static bool MotionEnabled
    {
        get { var enabled = 1; SystemParametersInfo(0x1042, 0, ref enabled, 0); return enabled != 0; }
    }

    public VoiceBubble()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
        AccessibleName = "JL Voice"; AccessibleRole = AccessibleRole.ToolBar;
        animation.Tick += (_, _) => { frame = (frame + 1) % 12; Invalidate(); };
        transient.Tick += (_, _) => { transient.Stop(); SetIdle(shortcut, locale); Settled?.Invoke(); };
        MouseEnter += (_, _) => SetHover(true); MouseLeave += (_, _) => SetHover(false);
        Place(Screen.FromPoint(Cursor.Position)); SetIdle(shortcut, locale);
    }
    private string T(string cs, string en) => locale == "cs" ? cs : en;
    private float ScaleFactor => DeviceDpi / 96f;
    private int S(float n) => (int)Math.Round(n * ScaleFactor);
    internal void SetIdle(string keys, string language)
    {
        shortcut = keys; locale = language; SetState(BubbleActivity.Idle, "");
    }
    internal void SetRecording(string language)
    {
        locale = language; SetState(BubbleActivity.Recording, "0 s");
    }
    internal void UpdateAudio(float rms, TimeSpan elapsed)
    {
        if (activity != BubbleActivity.Recording) return;
        Array.Copy(levels, 1, levels, 0, levels.Length - 1); levels[^1] = PcmLevel.Display(rms);
        caption = $"{elapsed.TotalSeconds:F0} s"; Invalidate();
    }
    internal void SetProcessing(string language, bool cancelling = false)
    {
        locale = language; SetState(BubbleActivity.Processing, cancelling ? T("Ruším…", "Cancelling…") : T("Přepisuji…", "Transcribing…"));
    }
    internal void SetCompleted(string language, bool cancelled = false)
    {
        locale = language; SetState(cancelled ? BubbleActivity.Cancelled : BubbleActivity.Success, cancelled ? T("Zrušeno", "Cancelled") : T("Hotovo", "Done"));
        transient.Start();
    }
    internal void SetError(string language, bool hasText)
    {
        locale = language; SetState(BubbleActivity.Error, hasText ? T("Otevřít přepis", "Open transcript") : T("Otevřít podrobnosti", "View details"));
    }
    private void SetState(BubbleActivity next, string text)
    {
        animation.Stop(); transient.Stop(); activity = next; caption = text; hovered = false; frame = 0; Array.Clear(levels);
        if (next == BubbleActivity.Processing && MotionEnabled) animation.Start();
        AccessibleDescription = next switch
        {
            BubbleActivity.Idle => T("Mikrofon je vypnutý. Kliknutím začnete diktovat. Pravé tlačítko otevře nabídku.", "Microphone is off. Click to dictate. Right-click for options."),
            BubbleActivity.Recording => T("Nahrávání. Kliknutím zastavíte, křížkem nebo Escape zrušíte.", "Recording. Click to stop; cancel with the cross or Escape."),
            BubbleActivity.Processing => T("Přepisování. Křížkem nebo Escape zrušíte.", "Transcribing. Cancel with the cross or Escape."),
            _ => caption
        };
        hint.SetToolTip(this, AccessibleDescription); ResizeForState(); Invalidate();
    }
    internal void SetHover(bool value)
    {
        if (activity != BubbleActivity.Idle || hovered == value) return;
        hovered = value; ResizeForState(); Invalidate();
    }
    internal void ApplyTheme(Palette palette) { colors = palette; BackColor = colors.Surface; Invalidate(); }
    internal void PlaceForWindow(IntPtr window) => Place(window == IntPtr.Zero ? Screen.FromPoint(Cursor.Position) : Screen.FromHandle(window));
    internal void Place(Screen target)
    {
        screen = target;
        if (placing) return;
        placing = true;
        try
        {
            var area = target.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - S(18));
        }
        finally { placing = false; }
    }
    private void ResizeForState()
    {
        var logical = activity switch
        {
            BubbleActivity.Idle => hovered ? new Size(264, 44) : new Size(84, 34),
            BubbleActivity.Recording => new Size(276, 48),
            BubbleActivity.Processing => new Size(248, 44),
            BubbleActivity.Error => new Size(264, 44),
            _ => new Size(156, 40)
        };
        Size = new Size(S(logical.Width), S(logical.Height));
        Place(screen ?? Screen.FromPoint(Cursor.Position));
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE, including mouse clicks.
        if (m.Msg is 0x201 or 0x204) { m.Result = IntPtr.Zero; return; } // Do not let managed mouse-down focus this form.
        if (m.Msg is 0x202 or 0x205)
        {
            var position = m.LParam.ToInt64();
            OnBubbleClick(this, new MouseEventArgs(m.Msg == 0x205 ? MouseButtons.Right : MouseButtons.Left, 1, (short)position, (short)(position >> 16), 0));
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg is 0x7E or 0x1A) Place(screen ?? Screen.FromPoint(Cursor.Position));
        base.WndProc(ref m);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); ResizeForState(); }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e); if (Width < 2 || Height < 2) return;
        using var path = Shape.Round(ClientRectangle, Math.Max(1, Height / 2 - 1));
        var previous = Region; Region = new Region(path); previous?.Dispose();
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { e.Cancel = true; base.OnFormClosing(e); }
    private void OnBubbleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (activity is BubbleActivity.Recording or BubbleActivity.Processing) return;
            var menu = new ContextMenuStrip { Font = BrandFonts.Get("body", 10) };
            menu.Items.Add(T("Nastavení", "Settings"), null, (_, _) => SettingsRequested?.Invoke());
            menu.Items.Add(T("Skrýt v klidu pro tuto relaci", "Hide idle bubble for this session"), null, (_, _) => IdleHideRequested?.Invoke());
            menu.Closed += (_, _) => menu.Dispose(); menu.Show(this, e.Location); return;
        }
        if (e.Button != MouseButtons.Left) return;
        var right = e.X >= Width - S(46);
        if (activity is BubbleActivity.Recording or BubbleActivity.Processing)
        {
            if (right) CancelRequested?.Invoke();
            else if (activity == BubbleActivity.Recording) RecordRequested?.Invoke();
        }
        else if (activity == BubbleActivity.Error)
        {
            if (right) { SetIdle(shortcut, locale); Settled?.Invoke(); } else DetailsRequested?.Invoke();
        }
        else if (activity is BubbleActivity.Success or BubbleActivity.Cancelled) DetailsRequested?.Invoke();
        else if (hovered && right) SettingsRequested?.Invoke();
        else RecordRequested?.Invoke();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(colors.Surface);
        using var border = new Pen(colors.Border);
        using var path = Shape.Round(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2 - 1); g.DrawPath(border, path);
        using var ink = new Pen(colors.Ink, S(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var accent = new SolidBrush(colors.Accent);
        var middle = Height / 2;
        if (activity == BubbleActivity.Recording)
        {
            g.FillRectangle(accent, S(20), middle - S(5), S(10), S(10));
            for (var i = 0; i < levels.Length; ++i)
            {
                var h = S(3 + levels[i] * 24); var rect = new Rectangle(S(64 + i * 9), middle - h / 2, S(4), h);
                g.FillRectangle(accent, rect);
            }
            DrawText(g, caption, new Rectangle(S(164), 0, S(60), Height), "mono", colors.Dim);
        }
        else if (activity == BubbleActivity.Processing)
        {
            for (var i = 0; i < 3; ++i)
            {
                using var dot = new SolidBrush(animation.Enabled && (frame / 2) % 3 == i ? colors.Accent : colors.Dim);
                g.FillEllipse(dot, S(19 + i * 6), middle - S(2), S(3), S(3));
            }
            DrawText(g, caption, new Rectangle(S(50), 0, Width - S(98), Height), "body", colors.Ink);
        }
        else if (activity == BubbleActivity.Idle)
        {
            using var mic = Shape.Round(new Rectangle(S(20), middle - S(7), S(7), S(11)), S(3)); g.DrawPath(ink, mic);
            g.DrawArc(ink, S(17), middle - S(4), S(13), S(13), 0, 180); g.DrawLine(ink, S(23), middle + S(9), S(23), middle + S(12));
            if (hovered) DrawText(g, shortcut, new Rectangle(S(49), 0, Width - S(98), Height), "mono", colors.Ink);
            else { g.DrawLine(ink, S(50), middle, S(54), middle); g.DrawLine(ink, S(61), middle, S(65), middle); }
        }
        else
        {
            if (activity == BubbleActivity.Success) { g.DrawLine(ink, S(20), middle, S(24), middle + S(4)); g.DrawLine(ink, S(24), middle + S(4), S(32), middle - S(4)); }
            else if (activity == BubbleActivity.Error) DrawText(g, "!", new Rectangle(S(14), 0, S(26), Height), "mono", colors.Accent);
            else DrawCross(g, ink, S(25), middle);
            DrawText(g, caption, new Rectangle(S(46), 0, Width - S(activity == BubbleActivity.Error ? 92 : 56), Height), "body", colors.Ink);
        }
        if (activity is BubbleActivity.Recording or BubbleActivity.Processing or BubbleActivity.Error) DrawCross(g, ink, Width - S(25), middle);
        else if (activity == BubbleActivity.Idle && hovered)
        {
            var center = new Point(Width - S(25), middle); g.DrawEllipse(ink, center.X - S(5), center.Y - S(5), S(10), S(10));
            g.DrawEllipse(ink, center.X - S(1), center.Y - S(1), S(2), S(2));
            for (var i = 0; i < 8; ++i)
            {
                var angle = i * Math.PI / 4;
                g.DrawLine(ink, center.X + S((float)Math.Cos(angle) * 6), center.Y + S((float)Math.Sin(angle) * 6), center.X + S((float)Math.Cos(angle) * 8), center.Y + S((float)Math.Sin(angle) * 8));
            }
        }
    }
    private void DrawCross(Graphics g, Pen pen, int x, int y) { g.DrawLine(pen, x - S(3), y - S(3), x + S(3), y + S(3)); g.DrawLine(pen, x + S(3), y - S(3), x - S(3), y + S(3)); }
    private static void DrawText(Graphics g, string text, Rectangle rectangle, string role, Color color) =>
        TextRenderer.DrawText(g, text, BrandFonts.Get(role, 9.5f), rectangle, color, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { animation.Dispose(); transient.Dispose(); hint.Dispose(); }
        base.Dispose(disposing);
    }
}
