using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vstavka.Desktop;
using Path = Avalonia.Controls.Shapes.Path;

/// <summary>
/// The picture that opens the README: a drawn macOS desktop with the rendered panel at the bottom of the screen.
/// </summary>
internal static class MacDesktop
{
    public const double Width = 1280;
    public const double Height = 800;

    /// <summary>The gap PanelWindow keeps between the panel and the edges of the screen.</summary>
    public const double PanelMargin = 12;

    private const string AppleLogo =
        "M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04-2.04.027-3.91 1.183-4.961 3.014-2.117 3.675-.546 9.103 1.519 12.09 " +
        "1.013 1.454 2.208 3.09 3.792 3.039 1.52-.065 2.09-.987 3.935-.987 1.831 0 2.35.987 3.96.948 1.637-.026 2.676-1.48 " +
        "3.676-2.948 1.156-1.688 1.636-3.325 1.662-3.415-.039-.013-3.182-1.221-3.22-4.857-.026-3.04 2.48-4.494 2.597-4.559" +
        "-1.429-2.09-3.623-2.324-4.39-2.376-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83-1.207.052" +
        "-2.662.805-3.532 1.818-.78.896-1.454 2.338-1.273 3.714 1.338.104 2.715-.688 3.559-1.701";

    /// <param name="panelPng">The panel rendered at twice its size.</param>
    public static Window Create(byte[] panelPng, bool dark)
    {
        var desktop = Wallpaper(dark);
        // The macOS interface font, where a Mac has it installed under this name; other systems use their default font.
        TextElement.SetFontFamily(desktop, FontFamily.Parse("SF Pro Text"));
        Place(desktop, MenuBar(dark), 0, 0);
        Place(desktop, AppWindow(dark, "Trail map.png", active: false, 448,
            Slab(447, 252, 0, 9.5, new ImageBrush(Decode(DemoSamples.ExampleImage(scale: 3))) { Stretch = Stretch.UniformToFill })), 628, 118);
        Place(desktop, AppWindow(dark, "Saturday plan.txt", active: true, 548, Note(dark)), 176, 62);

        // The panel's surface sits 5 units inside its window and has 23-unit corners; the shadow follows it.
        var panel = Decode(panelPng);
        var width = panel.PixelSize.Width / 2.0;
        var height = panel.PixelSize.Height / 2.0;
        var top = Height - PanelMargin - height;
        Place(desktop, new Border
        {
            Width = width - 10, Height = height - 10, CornerRadius = new CornerRadius(23),
            BoxShadow = BoxShadows.Parse(dark ? "0 16 48 0 #99000000" : "0 16 48 0 #59000000")
        }, PanelMargin + 5, top + 5);
        Place(desktop, new Image { Source = panel, Width = width, Height = height }, PanelMargin, top);

        return new Window
        {
            Width = Width, Height = Height, Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = desktop }
        };
    }

    private static Canvas Wallpaper(bool dark)
    {
        var wallpaper = new Canvas
        {
            Width = Width, Height = Height,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = dark
                    ? [new(Color.Parse("#0C1230"), 0), new(Color.Parse("#231650"), .55), new(Color.Parse("#3B1552"), 1)]
                    : [new(Color.Parse("#DCE8FB"), 0), new(Color.Parse("#E6DEF8"), .55), new(Color.Parse("#F7E1EC"), 1)]
            }
        };
        (string Color, double X, double Y, double Width, double Height)[] glows = dark
            ?
            [
                ("#A63F6FE0", 900, 40, 1300, 760), ("#B37A4BEA", 300, 640, 1400, 900),
                ("#99D0489A", 1240, 700, 1000, 800), ("#5924B6B0", 60, 120, 700, 560)
            ]
            :
            [
                ("#CC9DC4FA", 900, 40, 1300, 760), ("#CCB59CF5", 300, 640, 1400, 900),
                ("#CCF6B3D0", 1240, 700, 1000, 800), ("#99FBDDBB", 60, 120, 700, 560)
            ];
        foreach (var glow in glows)
        {
            var color = Color.Parse(glow.Color);
            Place(wallpaper, new Avalonia.Controls.Shapes.Ellipse
            {
                Width = glow.Width, Height = glow.Height,
                Fill = new RadialGradientBrush { GradientStops = [new(color, 0), new(new Color(0, color.R, color.G, color.B), 1)] }
            }, glow.X - glow.Width / 2, glow.Y - glow.Height / 2);
        }
        return wallpaper;
    }

    private static Border MenuBar(bool dark)
    {
        var ink = Ink(dark, 1);
        var faint = Ink(dark, .45);
        var menus = Row(19, new Path { Data = Geometry.Parse(AppleLogo), Fill = ink, Stretch = Stretch.Uniform, Height = 15.5, Margin = new Thickness(0, -1.5, 1, 0) },
            Label("TextEdit", ink, FontWeight.Bold));
        foreach (var menu in new[] { "File", "Edit", "Format", "View", "Window", "Help" })
            menus.Children.Add(Label(menu, ink));

        using var icon = AssetLoader.Open(new Uri("avares://Vstavka.Desktop/Assets/Vstavka.png"));
        var battery = Row(1,
            new Border
            {
                Width = 22.5, Height = 11.5, CornerRadius = new CornerRadius(3.4), BorderBrush = faint, BorderThickness = new Thickness(1),
                Padding = new Thickness(1), Child = new Border { Background = ink, CornerRadius = new CornerRadius(1.8) }
            },
            new Border { Width = 1.4, Height = 4, Background = faint, CornerRadius = new CornerRadius(0, 1, 1, 0) });
        var status = Row(15, new Image { Source = new Bitmap(icon), Width = 17, Height = 17 }, battery,
            Stroked(16, 12, "M0.8,4 A10.2,10.2 0 0 1 15.2,4 M3.1,6.3 A6.9,6.9 0 0 1 12.9,6.3 M5.4,8.7 A3.6,3.6 0 0 1 10.6,8.7 M7.9,11 L8.1,11", ink, 1.8),
            Stroked(14, 14, "M10.4,5.8 A4.6,4.6 0 1 1 1.2,5.8 A4.6,4.6 0 1 1 10.4,5.8 M9.2,9.2 L13,13", ink, 1.6),
            new Path
            {
                Width = 16, Height = 13, Fill = ink,
                Data = Geometry.Parse(
                    "M2.9,0 H13.1 A2.9,2.9 0 0 1 13.1,5.8 H2.9 A2.9,2.9 0 0 1 2.9,0 Z M3.1,1.2 A1.7,1.7 0 1 0 3.1,4.6 A1.7,1.7 0 1 0 3.1,1.2 Z " +
                    "M2.9,7.2 H13.1 A2.9,2.9 0 0 1 13.1,13 H2.9 A2.9,2.9 0 0 1 2.9,7.2 Z M12.9,8.4 A1.7,1.7 0 1 0 12.9,11.8 A1.7,1.7 0 1 0 12.9,8.4 Z")
            },
            Label(DateTime.Now.ToString("ddd MMM d   h:mm tt", CultureInfo.InvariantCulture), ink));

        menus.Margin = new Thickness(20, 0, 0, 0);
        status.Margin = new Thickness(0, 0, 16, 0);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        return new Border
        {
            Width = Width, Height = 26, Background = Brush(dark ? "#4D0A0A14" : "#73FFFFFF"),
            Child = new Panel { Children = { menus, status } }
        };
    }

    private static Panel AppWindow(bool dark, string title, bool active, double width, Control content)
    {
        string[] lights = active ? ["#FF5F57", "#FEBC2E", "#28C840"] : dark ? ["#5C5C61", "#5C5C61", "#5C5C61"] : ["#D2D2D6", "#D2D2D6", "#D2D2D6"];
        var buttons = Row(8);
        buttons.Margin = new Thickness(10, 0);
        foreach (var light in lights)
            buttons.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 12, Height = 12, Fill = Brush(light) });

        var name = Label(title, Ink(dark, active ? .85 : .45), FontWeight.SemiBold);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        var bar = new Panel
        {
            Children = { Slab(width - 1, 28, 9.5, 0, Brush(dark ? active ? "#3A3A3D" : "#2E2E31" : active ? "#ECECEF" : "#F5F5F7")), buttons, name }
        };
        var corners = new CornerRadius(10);
        return new Panel
        {
            Width = width,
            Children =
            {
                // The shadow has its own Border: in a 2x bitmap, a Border with a BoxShadow draws its children at twice the scale.
                new Border { CornerRadius = corners, BoxShadow = BoxShadows.Parse(active ? "0 22 60 0 #73000000" : "0 10 30 0 #40000000") },
                new Border
                {
                    CornerRadius = corners, Background = Brush(dark ? "#1E1E20" : "#FFFFFF"),
                    BorderBrush = Brush(dark ? "#33FFFFFF" : "#33000000"), BorderThickness = new Thickness(.5),
                    Child = new StackPanel { Children = { bar, new Border { Height = .5, Background = Brush(dark ? "#99000000" : "#1F000000") }, content } }
                }
            }
        };
    }

    /// <summary>A rectangle with its own corner radius at the top and at the bottom.</summary>
    private static Path Slab(double width, double height, double top, double bottom, IBrush fill)
    {
        var shape = new StreamGeometry();
        using (var figure = shape.Open())
        {
            figure.BeginFigure(new Point(0, top), isFilled: true);
            figure.ArcTo(new Point(top, 0), new Size(top, top), 0, false, SweepDirection.Clockwise);
            figure.LineTo(new Point(width - top, 0));
            figure.ArcTo(new Point(width, top), new Size(top, top), 0, false, SweepDirection.Clockwise);
            figure.LineTo(new Point(width, height - bottom));
            figure.ArcTo(new Point(width - bottom, height), new Size(bottom, bottom), 0, false, SweepDirection.Clockwise);
            figure.LineTo(new Point(bottom, height));
            figure.ArcTo(new Point(0, height - bottom), new Size(bottom, bottom), 0, false, SweepDirection.Clockwise);
            figure.EndFigure(isClosed: true);
        }
        return new Path { Data = shape, Fill = fill };
    }

    private static StackPanel Note(bool dark)
    {
        var ink = Ink(dark, .88);
        TextBlock Line(string text, FontWeight weight = FontWeight.Normal) => new()
        {
            Text = text, FontSize = 14, LineHeight = 21, FontWeight = weight, Foreground = ink, TextWrapping = TextWrapping.Wrap
        };
        return new StackPanel
        {
            Height = 402, Margin = new Thickness(18, 14, 22, 0),
            Children =
            {
                Line("Saturday field trip", FontWeight.Bold),
                Line(" "),
                Line("North path is open again. Take the short loop past the river and stop at the old bridge."),
                Line(" "),
                Line("Note for everyone:"),
                new Border { Width = 1.5, Height = 18, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Background = Brush(dark ? "#0A84FF" : "#007AFF") }
            }
        };
    }

    private static StackPanel Row(double spacing, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
        {
            child.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }
        return row;
    }

    private static TextBlock Label(string text, IBrush ink, FontWeight weight = FontWeight.Medium) =>
        new() { Text = text, FontSize = 13, FontWeight = weight, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };

    private static Path Stroked(double width, double height, string data, IBrush ink, double thickness) => new()
    {
        Width = width, Height = height, Data = Geometry.Parse(data), Stroke = ink, StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
    };

    private static Bitmap Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }

    private static SolidColorBrush Ink(bool dark, double opacity) => new(dark ? Colors.White : Colors.Black, opacity);

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static void Place(Canvas canvas, Control child, double left, double top)
    {
        Canvas.SetLeft(child, left);
        Canvas.SetTop(child, top);
        canvas.Children.Add(child);
    }
}
