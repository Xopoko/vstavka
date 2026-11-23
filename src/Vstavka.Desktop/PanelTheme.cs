using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Vstavka.Desktop;

internal static class PanelTheme
{
    public static void Apply(Control target, string? accentName = null)
    {
        var colors = Application.Current?.PlatformSettings?.GetColorValues();
        var dark = target.ActualThemeVariant == ThemeVariant.Dark;
        var highContrast = colors?.ContrastPreference == ColorContrastPreference.High;
        var surface = Color.Parse(dark ? "#26262A" : "#FFFFFF");
        var window = Color.Parse(dark ? "#1C1C1F" : "#F5F5F6");
        var ink = Color.Parse(dark ? "#F3F3F5" : "#1F1F23");
        var muted = Color.Parse(dark ? "#ABABB3" : "#5F5F66");
        var line = Color.Parse(dark ? "#3A3A40" : "#E3E3E6");
        var hoverLine = Color.Parse(dark ? "#696970" : "#ABABB3");
        if (highContrast)
        {
            surface = window = dark ? Colors.Black : Colors.White;
            ink = muted = line = hoverLine = dark ? Colors.White : Colors.Black;
        }

        var systemAccent = colors?.AccentColor1 ?? Colors.Transparent;
        var accent = accentName switch
        {
            "violet" => Color.Parse("#7147BE"),
            "blue" => Color.Parse("#246AC5"),
            "teal" => Color.Parse("#127B77"),
            "rose" => Color.Parse("#AC4268"),
            "amber" => Color.Parse("#9B640F"),
            _ => systemAccent.A > 0 ? Color.FromRgb(systemAccent.R, systemAccent.G, systemAccent.B) : Color.Parse("#246AC5")
        };
        var selection = Mix(surface, accent, highContrast ? .10 : dark ? .36 : .13);
        var selectedSurface = Mix(surface, accent, dark ? .17 : .08);
        var foregroundAccent = dark ? Mix(accent, Colors.White, .50) : accent;
        var toward = dark ? Colors.White : Colors.Black;
        while (Contrast(foregroundAccent, window) < 4.5 || Contrast(foregroundAccent, selection) < 4.5 ||
               Contrast(foregroundAccent, selectedSurface) < 4.5 || Contrast(foregroundAccent, surface) < 4.5)
        {
            var next = Mix(foregroundAccent, toward, .08);
            if (next == foregroundAccent)
                break;
            foregroundAccent = next;
        }

        Set("WindowBrush", window);
        Set("SurfaceBrush", surface);
        Set("InkBrush", ink);
        Set("MutedBrush", muted);
        Set("LineBrush", line);
        Set("HoverLineBrush", hoverLine);
        Set("InputBrush", highContrast ? surface : Color.Parse(dark ? "#2B2B30" : "#FBFBFC"));
        Set("MediaBrush", highContrast ? surface : Color.Parse(dark ? "#17171A" : "#F0F0F2"));
        Set("AccentBrush", foregroundAccent);
        var primary = accent;
        while (Contrast(primary, Colors.White) < 4.5)
            primary = Mix(primary, Colors.Black, .08);
        Set("PrimaryBrush", primary);
        Set("PrimaryHoverBrush", Mix(primary, Colors.Black, .13));
        Set("PrimaryInkBrush", Colors.White);
        Set("HoverBrush", Mix(surface, ink, .07));
        Set("PressedBrush", Mix(surface, ink, .13));
        Set("SelectionBrush", selection);
        Set("SelectedSurfaceBrush", selectedSurface);
        Set("SuccessBrush", highContrast ? ink : Color.Parse(dark ? "#91D09D" : "#527E5B"));
        Set("WarningBrush", highContrast ? ink : Color.Parse(dark ? "#EDBE76" : "#98600F"));
        var dangerFill = highContrast ? ink : Color.Parse(dark ? "#AD344B" : "#B52F49");
        Set("DangerBrush", highContrast ? ink : Color.Parse(dark ? "#F5A4B3" : "#AA3F55"));
        Set("DangerLineBrush", highContrast ? ink : Color.Parse(dark ? "#B37180" : "#BF7283"));
        Set("DangerSurfaceBrush", highContrast ? surface : Color.Parse(dark ? "#482831" : "#FFF0F2"));
        Set("DangerFillBrush", dangerFill);
        Set("DangerHoverBrush", highContrast ? dangerFill : Mix(dangerFill, Colors.Black, .12));
        Set("DangerPressedBrush", highContrast ? dangerFill : Mix(dangerFill, Colors.Black, .23));
        Set("OnDangerBrush", highContrast ? surface : Colors.White);
        Set("TextTypeBrush", muted);
        SetTint("LinkTypeBrush", "#35658B", "#9EC9F3");
        SetTint("ImageTypeBrush", "#87532F", "#EBBC98");
        SetTint("ColorTypeBrush", "#884B76", "#E3ABD2");
        SetTint("FilesTypeBrush", "#476E4D", "#A4D1AB");
        SetTint("CollectionBlue", "#2368B9", "#8BBEFF");
        SetTint("CollectionTeal", "#087F82", "#65D4D0");
        SetTint("CollectionGreen", "#438539", "#9AD886");
        SetTint("CollectionAmber", "#977000", "#F2CC72");
        SetTint("CollectionOrange", "#B45120", "#FFAA74");
        SetTint("CollectionRose", "#B44067", "#F6A1BB");
        SetTint("CollectionViolet", "#774DBC", "#C9A9FF");
        SetTint("CollectionSlate", "#5F6F83", "#ABB8C9");

        void Set(string key, Color value) => target.Resources[key] = new SolidColorBrush(value);
        void SetTint(string key, string light, string night) => Set(key, highContrast ? ink : Color.Parse(dark ? night : light));
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var x = Luminance(a);
        var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
