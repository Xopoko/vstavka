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
    public static TextBlock Text(string text, double size = TextSize.Body, string brush = "InkBrush", bool strong = false)
    {
        var label = PanelVisuals.Label(text, size, brush);
        label.TextWrapping = TextWrapping.Wrap;
        label.TextTrimming = TextTrimming.None;
        label.FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal;
        label.LineHeight = TextSize.LineHeight(size);
        return label;
    }

    public static Button Button(string key, Action action, string kind = "secondary", string? name = null, Symbol? symbol = null)
    {
        var label = Strings.Get(key);
        var button = new Button { Name = name, Classes = { "panel-button", kind }, Content = label };
        if (symbol is { } icon)
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = Space.S,
                Children = { PanelVisuals.Glyph(icon, IconSize.Control), new TextBlock { Text = label } }
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

    public static Border Surface(Control child, CornerRadius? radius = null, string background = "SurfaceBrush", string line = "LineBrush")
    {
        var border = new Border
        {
            CornerRadius = radius ?? Radius.Surface, BorderThickness = new Thickness(Stroke.Hairline), Child = child
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable(background));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable(line));
        return border;
    }

    public static Border Footer(Control controls, Thickness padding)
    {
        var footer = Surface(controls, default(CornerRadius), "WindowBrush");
        footer.BorderThickness = new Thickness(0, Stroke.Hairline, 0, 0);
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

/// <summary>
/// The app's motion. A change animates only when the movement says what the finished state cannot: where a
/// thing came from, where it went, or that an action was heard. Whatever repeats all day (selection, filters,
/// search, paste) is instant, and nothing the user asked for waits for an animation to end.
/// </summary>
internal static class DesktopMotion
{
    // One scale for every window. The steps are the platforms' own: Fluent moves in 83, 167 and 250 ms, and
    // macOS and GNOME transitions sit in the same range.
    /// <summary>Something leaves because the user sent it away.</summary>
    public static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(83);
    /// <summary>Something arrives, or changes state where it stands.</summary>
    public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(167);
    /// <summary>Content moves across the window.</summary>
    public static readonly TimeSpan Travel = TimeSpan.FromMilliseconds(250);
    /// <summary>An illustration in the guide, slow enough to be read.</summary>
    public static readonly TimeSpan Scene = TimeSpan.FromMilliseconds(500);

    /// <summary>Moves at once and lands softly, so the response is immediate. Used for everything that arrives.</summary>
    public static readonly Easing Decelerate = new CubicEaseOut();
    /// <summary>For fades and for key frames that carry their own rhythm; an easing would bend their timing.</summary>
    public static readonly Easing Linear = new LinearEasing();

    /// <summary>The system setting for animations, read on every use so a change applies without a restart.</summary>
    public static bool Enabled => ReadPreference();

    /// <summary>Brings a control to rest from the place it comes from.</summary>
    public static Task Arrive(Visual control, string from, TimeSpan duration, CancellationToken token,
        bool fade = false, TimeSpan delay = default) =>
        Run(control, duration, Decelerate, delay, token, [(0, fade ? 0 : 1, from), (1, 1, "none")]);

    /// <summary>Marks a step the app has just seen completed.</summary>
    public static Task Confirm(Visual control, CancellationToken token = default) =>
        Arrive(control, "scale(.85)", Enter, token, fade: true);

    /// <summary>Shakes a control that refused its input.</summary>
    public static Task Reject(Visual control) => Animate(control, Travel, Linear, CancellationToken.None,
        (0, 1, "none"), (.25, 1, "translateX(-6px)"), (.5, 1, "translateX(5px)"), (.75, 1, "translateX(-3px)"), (1, 1, "none"));

    public static Task Animate(Visual control, TimeSpan duration, Easing easing, CancellationToken token,
        params (double At, double Opacity, string Transform)[] frames) =>
        Run(control, duration, easing, TimeSpan.Zero, token, frames);

    private static async Task Run(Visual control, TimeSpan duration, Easing easing, TimeSpan delay, CancellationToken token,
        (double At, double Opacity, string Transform)[] frames)
    {
        // The first frame holds through the delay; the animation ends on the control's own values.
        var animation = new Animation { Duration = duration, Easing = easing, Delay = delay, FillMode = FillMode.Backward };
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
