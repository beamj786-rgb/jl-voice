using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JLVoice;

internal enum BubbleActivity { Idle, Recording, Processing, Success, Cancelled, Error }

public partial class VoiceBubble : Window
{
    private readonly DispatcherTimer animation = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer transient = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly Rectangle[] bars = new Rectangle[9];
    private readonly float[] levels = new float[9];
    private string locale = "cs", shortcut = "Ctrl + Alt + F9";
    private bool hovered, placing;
    private IntPtr targetWindow;
    private int frame;
    internal BubbleActivity Activity { get; private set; }
    internal bool AnimationRunning => animation.IsEnabled;
    internal bool TransientRunning => transient.IsEnabled;
    internal bool MeterCleared => levels.All(v => v == 0);
    internal event Action? RecordRequested, CancelRequested, SettingsRequested, DetailsRequested, IdleHideRequested, Settled;
    public VoiceBubble()
    {
        InitializeComponent();
        for (var i = 0; i < bars.Length; ++i)
        {
            var bar = new Rectangle { Width = 4, Height = 3, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, RadiusX = 1, RadiusY = 1 };
            bar.SetResourceReference(Shape.FillProperty, "AccentBrush"); bars[i] = bar; Meter.Children.Add(bar);
        }
        animation.Tick += (_, _) => { frame = (frame + 1) % 12; Glyph.Opacity = .35 + (frame % 6) * .13; };
        transient.Tick += (_, _) => { transient.Stop(); SetIdle(shortcut, locale); Settled?.Invoke(); };
        SourceInitialized += (_, _) =>
        {
            var handle = NativeDesktop.Handle(this);
            var style = NativeDesktop.GetWindowLongPtr(handle, -20).ToInt64();
            NativeDesktop.SetWindowLongPtr(handle, -20, new IntPtr(style | 0x08000000 | 0x80));
            HwndSource.FromHwnd(handle).AddHook(Hook);
            PlaceForWindow(targetWindow == IntPtr.Zero ? WindowsInput.GetForegroundWindow() : targetWindow);
        };
        SizeChanged += (_, _) => Place();
        SetIdle(shortcut, locale);
    }
    private string T(string cs, string en) => locale == "cs" ? cs : en;
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x21) { handled = true; return new IntPtr(3); }
        if (message is 0x7E or 0x1A) PlaceForWindow(WindowsInput.GetForegroundWindow());
        if (message == 0x2E0) Dispatcher.BeginInvoke(Place);
        return IntPtr.Zero;
    }
    internal void PlaceForWindow(IntPtr window) { targetWindow = window; Place(); }
    private void Place()
    {
        if (placing || !IsInitialized || new WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        placing = true;
        try
        {
            var area = NativeDesktop.WorkingArea(targetWindow);
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = (int)Math.Round(Width * dpi.DpiScaleX); var height = (int)Math.Round(Height * dpi.DpiScaleY);
            NativeDesktop.SetWindowPos(NativeDesktop.Handle(this), new IntPtr(-1), area.Left + (area.Right - area.Left - width) / 2, area.Bottom - height - (int)Math.Round(18 * dpi.DpiScaleY), 0, 0, 0x0001 | 0x0010);
        }
        finally { placing = false; }
    }
    internal void SetIdle(string keys, string language) { shortcut = keys; locale = language; SetState(BubbleActivity.Idle); }
    internal void SetRecording(string language) { locale = language; SetState(BubbleActivity.Recording); }
    // Device opening: speech is not captured yet; switch to Recording on the first PCM buffer.
    internal void SetPreparing(string language)
    {
        locale = language; SetState(BubbleActivity.Processing); BubbleCaption.Text = T("Připravuji mikrofon…", "Preparing microphone…");
    }
    internal void SetProcessing(string language, bool cancelling = false)
    {
        locale = language; SetState(BubbleActivity.Processing); BubbleCaption.Text = cancelling ? T("Ruším…", "Cancelling…") : T("Přepisuji…", "Transcribing…");
    }
    internal void SetCompleted(string language, bool cancelled = false) { locale = language; SetState(cancelled ? BubbleActivity.Cancelled : BubbleActivity.Success); transient.Start(); }
    internal void SetError(string language, bool hasText) { locale = language; SetState(BubbleActivity.Error); BubbleCaption.Text = hasText ? T("Otevřít přepis", "Open transcript") : T("Otevřít podrobnosti", "View details"); }
    private void SetState(BubbleActivity state)
    {
        animation.Stop(); transient.Stop(); Glyph.Opacity = 1; frame = 0; Activity = state; hovered = false;
        Array.Clear(levels); foreach (var bar in bars) if (bar is not null) bar.Height = 3;
        Meter.Visibility = Elapsed.Visibility = state == BubbleActivity.Recording ? Visibility.Visible : Visibility.Collapsed;
        BubbleCaption.Visibility = state == BubbleActivity.Recording ? Visibility.Collapsed : Visibility.Visible;
        Glyph.Fill = null;
        var path = state switch
        {
            BubbleActivity.Idle => "M8,2 C5,2 5,4 5,7 L5,10 C5,14 11,14 11,10 L11,7 C11,3 11,2 8,2 M2,9 L2,11 C2,19 14,19 14,11 L14,9 M8,17 L8,21 M5,21 L11,21",
            BubbleActivity.Recording => "M3,5 L13,5 L13,15 L3,15 Z",
            BubbleActivity.Processing => "M2,10 L3,10 M8,10 L9,10 M14,10 L15,10",
            BubbleActivity.Success => "M2,10 L6,14 L15,5",
            BubbleActivity.Cancelled => "M3,5 L13,15 M13,5 L3,15",
            _ => "M8,3 L8,12 M8,16 L8,17"
        };
        Glyph.Data = Geometry.Parse(path); Glyph.Stretch = Stretch.Uniform;
        Glyph.Width = state == BubbleActivity.Recording ? 10 : 18; Glyph.Height = state == BubbleActivity.Recording ? 10 : 22;
        if (state == BubbleActivity.Recording) Glyph.SetResourceReference(Shape.FillProperty, "AccentBrush");
        BubbleCaption.Text = state switch { BubbleActivity.Idle => "··", BubbleActivity.Success => T("Hotovo", "Done"), BubbleActivity.Cancelled => T("Zrušeno", "Cancelled"), _ => "" };
        Elapsed.Text = "0 s";
        if (state == BubbleActivity.Processing && SystemParameters.ClientAreaAnimation) animation.Start();
        RefreshLayout();
    }
    internal void SetHover(bool value)
    {
        if (Activity != BubbleActivity.Idle || hovered == value) return;
        hovered = value; BubbleCaption.Text = value ? shortcut : "··"; RefreshLayout();
    }
    private void RefreshLayout()
    {
        Width = Activity switch { BubbleActivity.Idle => hovered ? 264 : 84, BubbleActivity.Recording => 276, BubbleActivity.Processing => 248, BubbleActivity.Error => 264, _ => 156 };
        Height = Activity switch { BubbleActivity.Idle => hovered ? 44 : 34, BubbleActivity.Recording => 48, _ => 44 };
        var side = hovered || Activity is BubbleActivity.Recording or BubbleActivity.Processing or BubbleActivity.Error;
        SideAction.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(MainAction, side ? 1 : 2);
        SideGlyph.Text = Activity == BubbleActivity.Idle ? "⚙" : "×";
        MainAction.IsEnabled = Activity != BubbleActivity.Processing;
        AutomationProperties.SetName(MainAction, Activity switch
        {
            BubbleActivity.Idle => T("Spustit diktování", "Start dictation"), BubbleActivity.Recording => T("Ukončit nahrávání", "Stop recording"),
            BubbleActivity.Processing => T("Přepisování", "Transcribing"), _ => T("Otevřít poslední přepis nebo podrobnosti", "Open last transcript or details")
        });
        AutomationProperties.SetName(SideAction, Activity == BubbleActivity.Idle ? T("Nastavení", "Settings") : Activity == BubbleActivity.Error ? T("Zavřít upozornění", "Dismiss message") : T("Zrušit diktování", "Cancel dictation"));
        ToolTip = Activity == BubbleActivity.Idle ? T("Mikrofon je vypnutý. Kliknutím začnete diktovat.", "Microphone is off. Click to dictate.") : null;
        Place();
    }
    internal void UpdateAudio(float rms, TimeSpan elapsed)
    {
        if (Activity != BubbleActivity.Recording) return;
        Array.Copy(levels, 1, levels, 0, levels.Length - 1); levels[^1] = PcmLevel.Display(rms);
        for (var i = 0; i < bars.Length; ++i) bars[i].Height = 3 + levels[i] * 24;
        Elapsed.Text = $"{elapsed.TotalSeconds:F0} s";
    }
    private void MainClick(object sender, RoutedEventArgs e)
    {
        if (Activity is BubbleActivity.Idle or BubbleActivity.Recording) RecordRequested?.Invoke();
        else if (Activity != BubbleActivity.Processing) DetailsRequested?.Invoke();
    }
    private void SideClick(object sender, RoutedEventArgs e)
    {
        if (Activity == BubbleActivity.Idle) SettingsRequested?.Invoke();
        else if (Activity == BubbleActivity.Error) { SetIdle(shortcut, locale); Settled?.Invoke(); }
        else CancelRequested?.Invoke();
    }
    private void EnterBubble(object sender, MouseEventArgs e) => SetHover(true);
    private void LeaveBubble(object sender, MouseEventArgs e) => SetHover(false);
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Activity != BubbleActivity.Idle) return;
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = T("Nastavení", "Settings") }; settings.Click += (_, _) => SettingsRequested?.Invoke();
        var hide = new MenuItem { Header = T("Skrýt v klidu pro tuto relaci", "Hide idle bubble for this session") }; hide.Click += (_, _) => IdleHideRequested?.Invoke();
        menu.Items.Add(settings); menu.Items.Add(hide); menu.IsOpen = true;
    }
    internal void DisposeBubble() { animation.Stop(); transient.Stop(); Close(); }
}
