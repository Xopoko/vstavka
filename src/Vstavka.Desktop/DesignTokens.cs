using Avalonia;

namespace Vstavka.Desktop;

// The app's design tokens. They come in three layers:
//   1. Scales: the steps in this file and the colour pairs in PanelTheme.
//   2. Roles: the names in this file and the brushes PanelTheme publishes (InkBrush, SurfaceBrush, ...).
//   3. Components: PanelStyles.axaml and the builders in PanelVisuals and SetupVisuals, which use roles only.
// A view picks a role. It does not write a size, a radius or a colour of its own; where a window is laid out
// by hand, only the placement of its content is. DESIGN.md describes every role and component.

/// <summary>Text sizes. Text is regular or semibold, nothing else.</summary>
internal static class TextSize
{
    /// <summary>Labels, status and anything secondary.</summary>
    public const double Caption = 12;
    /// <summary>The default.</summary>
    public const double Body = 14;
    /// <summary>The title of a section, a dialog or an empty state, and a card's headline.</summary>
    public const double Subtitle = 18;
    /// <summary>The large keycaps in the guide.</summary>
    public const double Keycap = 22;
    /// <summary>The title of a guide step.</summary>
    public const double Title = 24;
    /// <summary>The title of the step where the guide ends.</summary>
    public const double Display = 28;

    /// <summary>The line height that wrapped text of a size uses; titles that stay on one line keep the font's own.</summary>
    public static double LineHeight(double size) => size switch
    {
        Caption => 16,
        Body => 20,
        Title => 30,
        Display => 34,
        _ => double.NaN
    };
}

/// <summary>Icon sizes.</summary>
internal static class IconSize
{
    /// <summary>Beside caption text.</summary>
    public const double Inline = 14;
    /// <summary>In buttons, fields and beside body text.</summary>
    public const double Control = 16;
    /// <summary>Alone in a tile.</summary>
    public const double Feature = 20;
    /// <summary>An empty state.</summary>
    public const double Hero = 28;
}

/// <summary>
/// Distances between and inside things, on a 4-point grid with 2 and 6 for dense rows. The margins that place
/// content in a hand-laid window are layout, not spacing, and stay where the window is built.
/// </summary>
internal static class Space
{
    /// <summary>A label and the line under it.</summary>
    public const double Xxs = 2;
    /// <summary>Parts of one object, such as the keys of a shortcut.</summary>
    public const double Xs = 4;
    /// <summary>An icon and its label.</summary>
    public const double S = 6;
    /// <summary>Neighbouring controls and the rows of a form.</summary>
    public const double M = 8;
    /// <summary>The inside of a surface.</summary>
    public const double L = 12;
    /// <summary>Groups, and the edge of the panel.</summary>
    public const double Xl = 16;
    /// <summary>Sections.</summary>
    public const double Xxl = 24;
    /// <summary>The edge of a window.</summary>
    public const double Xxxl = 32;
}

/// <summary>Corner radii. Each level holds the one below: the panel holds surfaces, a surface holds controls.</summary>
internal static class Radius
{
    /// <summary>Keycaps, tags and thumbnails.</summary>
    public static readonly CornerRadius Small = new(4);
    /// <summary>Buttons, fields, tiles and media inside a card.</summary>
    public static readonly CornerRadius Control = new(8);
    /// <summary>Cards, grouped content, dialogs and messages.</summary>
    public static readonly CornerRadius Surface = new(12);
    /// <summary>The panel itself.</summary>
    public static readonly CornerRadius Panel = new(24);
    /// <summary>A filter chip: the pill of a control of the default height.</summary>
    public static readonly CornerRadius Chip = Pill(ControlSize.Default);

    /// <summary>Fully rounded ends for something of the given height: dots, switches, chips.</summary>
    public static CornerRadius Pill(double height) => new(height / 2);
}

/// <summary>Heights of interactive controls.</summary>
internal static class ControlSize
{
    /// <summary>Buttons, chips, fields and icon buttons.</summary>
    public const double Default = 36;
    /// <summary>An action inside a row of text, and the switch.</summary>
    public const double Compact = 26;
}

/// <summary>Line widths.</summary>
internal static class Stroke
{
    /// <summary>Borders and dividers.</summary>
    public const double Hairline = 1;
    /// <summary>Focus and selection rings.</summary>
    public const double Focus = 2;

    /// <summary>A hairline on every side, for styles.</summary>
    public static readonly Thickness Border = new(Hairline);
    /// <summary>A ring on every side, for styles.</summary>
    public static readonly Thickness Ring = new(Focus);
}
