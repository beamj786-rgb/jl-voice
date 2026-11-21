using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace JLVoice;

internal sealed record Palette(Color Background, Color Surface, Color Inset, Color Ink, Color Dim, Color Border, Color Accent, Color OnAccent)
{
    private static Color C(string hex) => ColorTranslator.FromHtml(hex);
    public static Palette Resolve(string preference)
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.Window, SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText);
        var light = preference == "light";
        if (preference == "system")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        return light
            ? new(C("#f4f5f7"), Color.White, C("#eceef2"), C("#08090a"), C("#535a66"), C("#d6dae2"), C("#4f63c9"), Color.White)
            : new(C("#08090a"), C("#0e0f11"), C("#141518"), C("#f2f3f5"), C("#8a8f98"), C("#4b4f57"), C("#b9c8ff"), C("#08090a"));
    }
}

internal static class BrandFonts
{
    private static readonly PrivateFontCollection collection = new();
    private static readonly Dictionary<string, Font> cache = new();
    private static readonly List<string> files = [];
    private static bool initialized;
    public static bool AllLoaded { get; private set; }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool RemoveFontResourceEx(string path, uint flags, IntPtr reserved);

    public static Font Get(string role, float size)
    {
        if (!initialized)
        {
            initialized = true;
            foreach (var file in new[] { "FunnelDisplay-Light.ttf", "FunnelSans-Regular.ttf", "GeistMono-Regular.ttf" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", file);
                if (!File.Exists(path) || AddFontResourceEx(path, 0x10, IntPtr.Zero) == 0) continue;
                files.Add(path); collection.AddFontFile(path);
            }
            AllLoaded = files.Count == 3;
        }
        var family = role switch { "display" => "Funnel Display Light", "mono" => "Geist Mono", _ => "Funnel Sans" };
        var id = family + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!cache.TryGetValue(id, out var font))
        {
            var available = collection.Families.FirstOrDefault(f => f.Name == family || (role == "display" && f.Name == "Funnel Display"));
            font = available is null ? new Font("Segoe UI", size) : new Font(available, size, FontStyle.Regular, GraphicsUnit.Point);
            cache[id] = font;
        }
        return font;
    }
    public static void Shutdown()
    {
        foreach (var font in cache.Values) font.Dispose();
        collection.Dispose();
        foreach (var path in files) RemoveFontResourceEx(path, 0x10, IntPtr.Zero);
    }
}

internal static class Shape
{
    public static GraphicsPath Round(Rectangle rectangle, int radius)
    {
        var p = new GraphicsPath(); var d = radius * 2;
        p.AddArc(rectangle.Left, rectangle.Top, d, d, 180, 90);
        p.AddArc(rectangle.Right - d, rectangle.Top, d, d, 270, 90);
        p.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        p.AddArc(rectangle.Left, rectangle.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}

internal sealed class SurfaceCard : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color BorderColor { get; set; }
    public SurfaceCard() { DoubleBuffered = true; Padding = new Padding(20); Margin = new Padding(0, 0, 0, 16); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shape.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var brush = new SolidBrush(BackColor); e.Graphics.FillPath(brush, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shape.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var pen = new Pen(BorderColor); e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class BrandButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Palette Colors { get; set; } = Palette.Resolve("dark");
    private bool hovered;
    public BrandButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        Height = 38; Font = BrandFonts.Get("body", 10); UseVisualStyleBackColor = false;
        MouseEnter += (_, _) => { hovered = true; Invalidate(); }; MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = !Enabled ? Colors.Inset : Primary ? Colors.Accent : Selected || hovered ? Colors.Inset : BackColor;
        using var path = Shape.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Focused && ShowFocusCues ? Colors.Accent : Primary ? fill : Colors.Border, Focused && ShowFocusCues ? 2 : 1);
        e.Graphics.DrawPath(pen, path);
        if (Selected)
        {
            using var active = new Pen(Colors.Accent, 2); e.Graphics.DrawLine(active, 4, 12, 4, Height - 12);
        }
        if (Primary && Focused && ShowFocusCues)
        {
            using var ring = Shape.Round(Rectangle.Inflate(ClientRectangle, -4, -4), 5);
            using var focus = new Pen(Colors.OnAccent); e.Graphics.DrawPath(focus, ring);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? Primary ? Colors.OnAccent : Colors.Ink : Colors.Dim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class BrandCombo : ComboBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Palette Colors { get; set; } = Palette.Resolve("dark");
    public BrandCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList; FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed;
        Font = BrandFonts.Get("body", 10); ItemHeight = 30; IntegralHeight = false; DropDownHeight = 240;
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selected ? Colors.Accent : Colors.Inset); e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index >= 0)
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -8, 0),
                selected ? Colors.OnAccent : Colors.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}
