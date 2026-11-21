using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace JLVoice;

internal static class Appearance
{
    public static bool FontsLoaded { get; private set; }
    public static bool Light { get; private set; }
    public static void Initialize()
    {
        var resources = Application.Current.Resources;
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/JLVoice;component/Brand.xaml", UriKind.Relative) });
        var folder = new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "fonts") + Path.DirectorySeparatorChar);
        var fonts = Fonts.GetFontFamilies(folder).ToArray();
        FontFamily Find(string name) => fonts.FirstOrDefault(f => f.FamilyNames.Values.Any(n => n == name)) ?? new FontFamily("Segoe UI");
        resources["BodyFont"] = Find("Funnel Sans"); resources["DisplayFont"] = Find("Funnel Display"); resources["MonoFont"] = Find("Geist Mono");
        // Static display font can expose its legacy family name instead.
        if (((FontFamily)resources["DisplayFont"]).Source == "Segoe UI") resources["DisplayFont"] = Find("Funnel Display Light");
        FontsLoaded = new[] { "BodyFont", "DisplayFont", "MonoFont" }.All(k => ((FontFamily)resources[k]).Source != "Segoe UI");
        Apply("dark");
    }
    public static void Apply(string preference)
    {
        Light = preference == "light";
        if (preference == "system")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            Light = key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        var values = Light
            ? new[] { "#f4f5f7", "#ffffff", "#eceef2", "#08090a", "#535a66", "#d6dae2", "#4f63c9", "#ffffff" }
            : new[] { "#08090a", "#0e0f11", "#141518", "#f2f3f5", "#8a8f98", "#4b4f57", "#b9c8ff", "#08090a" };
        var keys = new[] { "Background", "Surface", "Inset", "Ink", "Dim", "Border", "Accent", "OnAccent" };
        for (var i = 0; i < keys.Length; ++i)
        {
            Brush brush = SystemParameters.HighContrast ? i switch
            {
                3 or 4 or 5 => SystemColors.WindowTextBrush, 6 => SystemColors.HighlightBrush, 7 => SystemColors.HighlightTextBrush, _ => SystemColors.WindowBrush
            } : new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
            if (brush.CanFreeze) brush.Freeze();
            Application.Current.Resources[keys[i] + "Brush"] = brush;
        }
    }
}
