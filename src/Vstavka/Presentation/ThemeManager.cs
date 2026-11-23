using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
using Vstavka.Core;

namespace Vstavka.Presentation;

/// <summary>App-scoped colors. Windows preferences are read only; history content is never changed.</summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PaletteColor> Palette = new(StringComparer.Ordinal);
    private static readonly UISettings WindowsUiSettings = new();
    private static string _theme = "system", _accent = "windows";
    private static bool _initialized;
    private static bool _forceHighContrast;
    public static bool IsDark { get; private set; }
    public static string CurrentTheme => _theme;
    public static string CurrentAccent => _accent;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameterChanged;
        WindowsUiSettings.ColorValuesChanged += WindowsColorsChanged;
        Apply(_theme, _accent);
    }

    public static void Apply(string theme, string accent)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(() => Apply(theme, accent)));
            return;
        }
        _theme = theme?.ToLowerInvariant() is "light" or "dark" ? theme.ToLowerInvariant() : "system";
        _accent = accent?.ToLowerInvariant() ?? "windows";
        if (_forceHighContrast || SystemParameters.HighContrast)
        {
            IsDark = Contrast(SystemColors.WindowColor, Colors.White) > Contrast(SystemColors.WindowColor, Colors.Black);
            ApplyHighContrast();
        }
        else
        {
            IsDark = _theme == "dark" || (_theme == "system" && ReadWindowsDark());
            ApplyStandardPalette();
        }
        if (Application.Current is { } app)
        {
            app.Resources["ShadowColor"] = Colors.Black;
            foreach (Window window in app.Windows) UpdateWindowChrome(window);
        }
    }

    internal static void SetPreviewHighContrast(bool enabled)
    {
        _forceHighContrast = enabled;
        RefreshFromSystem();
    }

    private static void ApplyStandardPalette()
    {
        var surface = C(IsDark ? "#26262A" : "#FFFFFF");
        var window = C(IsDark ? "#1C1C1F" : "#F5F5F6");
        var ink = C(IsDark ? "#F3F3F5" : "#1F1F23");
        var muted = C(IsDark ? "#ABABB3" : "#5F5F66");
        var line = C(IsDark ? "#3A3A40" : "#E3E3E6");
        Color primary = _accent switch
        {
            "windows" => ReadWindowsAccent(),
            "blue" => C("#246AC5"), "teal" => C("#008173"), "rose" => C("#AD3B70"),
            "amber" => C("#A76608"), _ => C("#7450C5")
        };
        primary.A = 255;
        primary = EnsureContrast(primary, Colors.Black, Colors.White);
        var selection = Mix(surface, primary, IsDark ? .36 : .13);
        var selectedSurface = Mix(surface, primary, IsDark ? .17 : .08);
        var accentForeground = IsDark ? Mix(primary, Colors.White, .50) : primary;
        accentForeground = EnsureContrast(accentForeground, IsDark ? Colors.White : Colors.Black,
            window, surface, selection, selectedSurface, Mix(surface, ink, IsDark ? .14 : .10));

        Set("WindowBrush", window);
        Set("SurfaceBrush", surface);
        Set("InkBrush", ink);
        Set("MutedBrush", muted);
        Set("LineBrush", line);
        Set("HoverLineBrush", C(IsDark ? "#696970" : "#ABABB3"));
        Set("InputBrush", C(IsDark ? "#2B2B30" : "#FBFBFC"));
        Set("AccentBrush", accentForeground);
        Set("FocusBrush", accentForeground);
        Set("PrimaryBrush", primary);
        Set("OnAccentBrush", Colors.White);
        Set("PrimaryHoverBrush", Mix(primary, Colors.Black, .12)); Set("PrimaryPressedBrush", Mix(primary, Colors.Black, .23));
        Set("HoverBrush", Mix(surface, ink, IsDark ? .08 : .06));
        Set("PressedBrush", Mix(surface, ink, IsDark ? .14 : .10));
        Set("ControlBrush", Mix(surface, ink, IsDark ? .035 : .018));
        Set("ControlHoverBrush", Mix(surface, ink, IsDark ? .09 : .06));
        Set("ControlPressedBrush", Mix(surface, ink, IsDark ? .15 : .10));
        Set("FlatHoverLineBrush", Colors.Transparent);
        Set("SelectionBrush", selection);
        Set("SelectionForegroundBrush", accentForeground);
        Set("SelectionTextBrush", ink);
        Set("SelectedSurfaceBrush", selectedSurface);
        Set("KeycapBrush", surface);
        Set("ScrollBrush", Mix(surface, muted, .70));
        Set("MediaBrush", C(IsDark ? "#17171A" : "#F0F0F2"));
        Set("DangerBrush", C(IsDark ? "#F5A4B3" : "#AA3F55"));
        Set("DangerLineBrush", C(IsDark ? "#B37180" : "#BF7283"));
        Set("DangerSurfaceBrush", C(IsDark ? "#482831" : "#FFF0F2"));
        var dangerFill = C(IsDark ? "#AD344B" : "#B52F49");
        Set("DangerFillBrush", dangerFill);
        Set("DangerHoverBrush", Mix(dangerFill, Colors.Black, .12));
        Set("DangerPressedBrush", Mix(dangerFill, Colors.Black, .23));
        Set("OnDangerBrush", Colors.White);
        Set("SuccessBrush", C(IsDark ? "#91D09D" : "#527E5B"));
        Set("WarningBrush", C(IsDark ? "#EDBE76" : "#98600F"));
        Set("TextTypeBrush", muted);
        Set("LinkTypeBrush", C(IsDark ? "#9EC9F3" : "#35658B"));
        Set("ImageTypeBrush", C(IsDark ? "#EBBC98" : "#87532F"));
        Set("ColorTypeBrush", C(IsDark ? "#E3ABD2" : "#884B76"));
        Set("FilesTypeBrush", C(IsDark ? "#A4D1AB" : "#476E4D"));
        Set("CollectionBlue", C(IsDark ? "#8BBEFF" : "#2368B9"));
        Set("CollectionTeal", C(IsDark ? "#65D4D0" : "#087F82"));
        Set("CollectionGreen", C(IsDark ? "#9AD886" : "#438539"));
        Set("CollectionAmber", C(IsDark ? "#F2CC72" : "#977000"));
        Set("CollectionOrange", C(IsDark ? "#FFAA74" : "#B45120"));
        Set("CollectionRose", C(IsDark ? "#F6A1BB" : "#B44067"));
        Set("CollectionViolet", C(IsDark ? "#C9A9FF" : "#774DBC"));
        Set("CollectionSlate", C(IsDark ? "#ABB8C9" : "#5F6F83"));
    }

    private static void ApplyHighContrast()
    {
        SetColors(SystemColors.WindowColor, "WindowBrush", "SurfaceBrush", "InputBrush", "MediaBrush", "HoverBrush", "PressedBrush",
            "ControlBrush", "ControlHoverBrush", "ControlPressedBrush", "SelectedSurfaceBrush", "KeycapBrush", "DangerSurfaceBrush");
        SetColors(SystemColors.WindowTextColor, "InkBrush", "MutedBrush", "LineBrush", "HoverLineBrush", "FlatHoverLineBrush", "ScrollBrush",
            "DangerBrush", "DangerLineBrush", "SuccessBrush", "WarningBrush",
            "TextTypeBrush", "LinkTypeBrush", "ImageTypeBrush", "ColorTypeBrush", "FilesTypeBrush");
        SetColors(SystemColors.HighlightColor, "AccentBrush", "FocusBrush", "SelectionBrush", "PrimaryBrush", "PrimaryHoverBrush",
            "PrimaryPressedBrush", "DangerFillBrush", "DangerHoverBrush", "DangerPressedBrush");
        SetColors(SystemColors.HighlightTextColor, "OnAccentBrush", "SelectionForegroundBrush", "SelectionTextBrush", "OnDangerBrush");
        foreach (var color in ClipCollection.Colors) Set(color, SystemColors.WindowTextColor);
    }

    public static void RefreshFromSystem() => Apply(_theme, _accent);
    public static Brush GetBrush(string resourceKey)
    {
        EnsureInitialized();
        return Brushes.TryGetValue(resourceKey, out var brush) ? brush : Brushes["InkBrush"];
    }

    public static void UpdateWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        try { DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); }
        catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameterChanged;
        WindowsUiSettings.ColorValuesChanged -= WindowsColorsChanged;
        _initialized = false;
    }

    private static void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => ScheduleRefresh();
    private static void WindowsColorsChanged(UISettings sender, object args) => ScheduleRefresh();
    private static void SystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WindowGlassColor) or nameof(SystemParameters.HighContrast)) ScheduleRefresh();
    }
    private static void ScheduleRefresh()
    {
        if (Application.Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            dispatcher.BeginInvoke(new Action(RefreshFromSystem));
    }
    private static bool ReadWindowsDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }
    private static Color ReadWindowsAccent()
    {
        var accent = WindowsUiSettings.GetColorValue(UIColorType.Accent);
        return Color.FromRgb(accent.R, accent.G, accent.B);
    }
    private static void SetColors(Color color, params string[] keys)
    {
        foreach (var key in keys) Set(key, color);
    }
    private static void Set(string key, Color color)
    {
        if (Palette.TryGetValue(key, out var source)) source.Value = color;
        else { source = new PaletteColor(color); Palette.Add(key, source); }
        if (!Brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush();
            // A data binding prevents WPF styles and renderers from freezing this brush.
            // Existing cards keep the same brush while its bound color follows the palette.
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(PaletteColor.Value)) { Source = source, Mode = BindingMode.OneWay });
            Brushes.Add(key, brush);
        }
        if (Application.Current is { } app && !ReferenceEquals(app.Resources[key], brush)) app.Resources[key] = brush;
    }
    private sealed class PaletteColor : INotifyPropertyChanged
    {
        private Color _value;
        public PaletteColor(Color value) => _value = value;
        public Color Value
        {
            get => _value;
            set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color Mix(Color from, Color to, double fraction) => Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * fraction), (byte)Math.Round(from.G + (to.G - from.G) * fraction), (byte)Math.Round(from.B + (to.B - from.B) * fraction));
    private static Color EnsureContrast(Color color, Color toward, params Color[] backgrounds)
    {
        while (Array.Exists(backgrounds, background => Contrast(color, background) < 4.5))
        {
            var adjusted = Mix(color, toward, .06);
            if (adjusted == color) break;
            color = adjusted;
        }
        return color;
    }
    internal static double Contrast(Color a, Color b)
    {
        static double Channel(byte n) { double value = n / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        static double L(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double x = L(a), y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
