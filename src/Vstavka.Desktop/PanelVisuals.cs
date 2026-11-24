using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Vstavka.Core.Localization;

namespace Vstavka.Desktop;

internal static class PanelVisuals
{
    public static SymbolIcon Glyph(Symbol symbol, double size, string? brush = null, bool filled = false)
    {
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = size,
            IsHitTestVisible = false,
            IconVariant = filled ? IconVariant.Filled : IconVariant.Regular
        };
        if (brush is not null)
            icon.Bind(TemplatedControl.ForegroundProperty, icon.GetResourceObservable(brush));
        return icon;
    }

    public static TextBlock Label(string text, double size = TextSize.Body, string brush = "InkBrush")
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(brush));
        return label;
    }

    public static Button IconButton(Symbol symbol, string label, Action action)
    {
        var button = new Button { Classes = { "panel-button", "icon-button" }, Content = Glyph(symbol, IconSize.Control) };
        NameButton(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    public static void NameButton(Button button, string label)
    {
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
    }

    public static Border Dot(string brush, double size = Space.M)
    {
        var dot = new Border
        {
            Width = size, Height = size, CornerRadius = Radius.Pill(size),
            VerticalAlignment = VerticalAlignment.Center
        };
        dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(brush));
        return dot;
    }

    public static Control Hint(string key, string? actionKey = null)
    {
        var hint = Strings.Get(key).Split("  ", 2, StringSplitOptions.None);
        if (hint.Length != 2)
            return Label(hint[0], TextSize.Caption, "MutedBrush");

        var keycap = new Border
        {
            BorderThickness = new Thickness(Stroke.Hairline), CornerRadius = Radius.Small,
            Padding = new Thickness(Space.Xs, Space.Xxs), FlowDirection = FlowDirection.LeftToRight,
            Child = Label(hint[0], TextSize.Caption)
        };
        keycap.Bind(Border.BackgroundProperty, keycap.GetResourceObservable("SurfaceBrush"));
        keycap.Bind(Border.BorderBrushProperty, keycap.GetResourceObservable("LineBrush"));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = Space.S, VerticalAlignment = VerticalAlignment.Center,
            Children = { keycap, Label(actionKey is null ? hint[1] : Strings.Get(actionKey), TextSize.Caption, "MutedBrush") }
        };
    }

    public static Border Feedback(string message)
    {
        var label = Label(message);
        label.TextWrapping = TextWrapping.Wrap;
        label.MaxLines = 2;
        var border = new Border
        {
            CornerRadius = Radius.Surface, BorderThickness = new Thickness(Stroke.Hairline),
            Padding = new Thickness(Space.Xl, Space.M), Child = label
        };
        PanelTheme.Apply(border);
        border.ActualThemeVariantChanged += (_, _) => PanelTheme.Apply(border);
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("WindowBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("LineBrush"));
        return border;
    }
}
