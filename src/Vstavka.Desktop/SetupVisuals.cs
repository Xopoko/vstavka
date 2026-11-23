using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Animators;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using FluentIcons.Common;
using Vstavka.Core.Localization;

namespace Vstavka.Desktop;

internal static class SetupVisuals
{
    public static TextBlock Text(string text, double size = 14, string brush = "InkBrush", bool strong = false)
    {
        var label = PanelVisuals.Label(text, size, brush);
        label.TextWrapping = TextWrapping.Wrap;
        label.TextTrimming = TextTrimming.None;
        label.FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal;
        label.LineHeight = size switch { 12 => 16, 14 => 20, 24 => 30, 28 => 34, _ => double.NaN };
        return label;
    }

    public static Button Button(string key, Action action, string kind = "secondary", string? name = null, Symbol? symbol = null)
    {
        var label = Strings.Get(key);
        var button = new Button { Name = name, Classes = { "panel-button", kind }, Content = label };
        if (symbol is { } icon)
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 7,
                Children = { PanelVisuals.Glyph(icon, 16), new TextBlock { Text = label } }
            };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    public static StackPanel Horizontal(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    public static Border Surface(Control child, double radius = 12, string background = "SurfaceBrush", string line = "LineBrush")
    {
        var border = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Child = child };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable(background));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable(line));
        return border;
    }

    public static Border Footer(Control controls, Thickness padding)
    {
        var footer = Surface(controls, radius: 0, background: "WindowBrush");
        footer.BorderThickness = new Thickness(0, 1, 0, 0);
        footer.Height = 62;
        footer.Padding = padding;
        return footer;
    }

    public static void Add(Grid grid, Control child, int column = 0, int row = 0)
    {
        Grid.SetColumn(child, column);
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    public static void Localize(Window window) =>
        window.FlowDirection = Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static string ShortcutInText(string shortcut) => Strings.IsRightToLeft ? "\u2066" + shortcut + "\u2069" : shortcut;

    public static void ApplyTheme(string theme)
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = theme switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
    }

    public static async Task Animate(Visual control, int milliseconds, CancellationToken token,
        params (double At, double Opacity, string Transform)[] frames)
    {
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new ExponentialEaseOut() };
        foreach (var (at, opacity, transform) in frames)
            animation.Children.Add(new KeyFrame
            {
                Cue = new Cue(at), Setters =
                {
                    new Setter(Visual.OpacityProperty, opacity),
                    new Setter(Visual.RenderTransformProperty, TransformOperations.Parse(transform))
                }
            });
        try { await animation.RunAsync(control, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public static async Task Reveal(Visual control, int delay, int duration, string transform, CancellationToken token)
    {
        control.Opacity = 0;
        try
        {
            if (delay > 0)
                await Task.Delay(delay, token);
            control.Opacity = 1;
            await Animate(control, duration, token, (0, 0, transform), (1, 1, "translateY(0px)"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { control.Opacity = 1; }
    }
}

/// <summary>
/// Interpolates <see cref="Visual.RenderTransform"/> between key frames. Avalonia ships this only for
/// transitions; without it, key frame animations that move or scale a control throw.
/// </summary>
internal sealed class TransformKeyFrameAnimator : InterpolatingAnimator<ITransform>
{
    public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue) =>
        TransformOperations.Interpolate(oldValue as TransformOperations ?? TransformOperations.Identity,
            newValue as TransformOperations ?? TransformOperations.Identity, progress);
}

internal static class DesktopMotion
{
    public static bool Enabled => ReadPreference();

    private static bool ReadPreference()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled;
            if (OperatingSystem.IsMacOS())
            {
                var workspace = objc_msgSend(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
                return workspace == 0 || !SendBool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion"));
            }
            if (OperatingSystem.IsLinux())
            {
                var source = g_settings_schema_source_get_default();
                var schema = source == 0 ? 0 : g_settings_schema_source_lookup(source, "org.gnome.desktop.interface", 1);
                if (schema == 0)
                    return true;
                try
                {
                    if (g_settings_schema_has_key(schema, "enable-animations") == 0)
                        return true;
                    var settings = g_settings_new_full(schema, 0, 0);
                    if (settings == 0)
                        return true;
                    try { return g_settings_get_boolean(settings, "enable-animations") != 0; }
                    finally { g_object_unref(settings); }
                }
                finally { g_settings_schema_unref(schema); }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC)] private static extern nint objc_msgSend(nint target, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(nint target, nint selector);

    private const string Gio = "libgio-2.0.so.0";
    [DllImport(Gio)] private static extern nint g_settings_schema_source_get_default();
    [DllImport(Gio)] private static extern nint g_settings_schema_source_lookup(nint source, string id, int recursive);
    [DllImport(Gio)] private static extern int g_settings_schema_has_key(nint schema, string key);
    [DllImport(Gio)] private static extern nint g_settings_new_full(nint schema, nint backend, nint path);
    [DllImport(Gio)] private static extern int g_settings_get_boolean(nint settings, string key);
    [DllImport(Gio)] private static extern void g_settings_schema_unref(nint schema);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(nint settings);
}
