using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Desktop;

internal sealed class ClipCardView : UserControl
{
    private readonly HistoryStore _store;
    private readonly Image _sourceImage = new() { Width = IconSize.Control, Height = IconSize.Control };
    private readonly SymbolIcon _sourceFallback = PanelVisuals.Glyph(Symbol.AppGeneric, IconSize.Control, "MutedBrush");
    private readonly Image _previewImage = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _imageFallback = PanelVisuals.Label(Strings.Get("ImagePreview"), TextSize.Caption, "MutedBrush");
    private Bitmap? _sourceBitmap;
    private Bitmap? _previewBitmap;

    public ClipItem Item { get; }

    public ClipCardView(ClipItem item, HistoryStore store, Action togglePin)
    {
        Item = item;
        _store = store;

        var typeIcon = item.Kind switch
        {
            ClipKind.Link => Symbol.Link,
            ClipKind.Image => Symbol.Image,
            ClipKind.Color => Symbol.Color,
            ClipKind.Files => Symbol.Folder,
            _ => Symbol.TextAlignLeft
        };
        var typeLabel = PanelVisuals.Label(item.TypeLabel, TextSize.Caption, "MutedBrush");
        typeLabel.FontWeight = FontWeight.SemiBold;
        var pinLabel = Strings.Get(item.IsPinned ? "Unpin" : "Pin");
        var pin = PanelVisuals.IconButton(Symbol.Pin, pinLabel, togglePin);
        pin.Content = PanelVisuals.Glyph(Symbol.Pin, IconSize.Control, item.IsPinned ? "AccentBrush" : null, item.IsPinned);
        pin.HorizontalAlignment = HorizontalAlignment.Right;

        var header = new Grid { ColumnDefinitions = new("20,*,36"), Margin = new Thickness(Space.L, 0, Space.Xs, 0) };
        var glyph = PanelVisuals.Glyph(typeIcon, IconSize.Inline, item.Kind + "TypeBrush");
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        header.Children.Add(glyph);
        AddColumn(header, typeLabel, 1);
        AddColumn(header, pin, 2);

        var body = CreateBody();
        Grid.SetRow(body, 1);
        var footer = CreateFooter();
        Grid.SetRow(footer, 2);
        Content = new Grid { RowDefinitions = new("39,*,35"), Children = { header, body, footer } };
        AutomationProperties.SetName(this, item.Title);
        AttachedToVisualTree += (_, _) => LoadBitmaps();
        DetachedFromVisualTree += (_, _) => ReleaseBitmaps();
    }

    private Control CreateBody()
    {
        var margin = new Thickness(Space.L, Space.M);
        switch (Item.Kind)
        {
            case ClipKind.Link:
                var title = PanelVisuals.Label(Item.Title, TextSize.Subtitle);
                title.FontWeight = FontWeight.SemiBold;
                title.FlowDirection = FlowDirection.LeftToRight;
                return new StackPanel
                {
                    Margin = margin, Spacing = Space.M, ClipToBounds = true,
                    Children = { title, PreviewText(Item.Preview, 4, FlowDirection.LeftToRight, "MutedBrush") }
                };
            case ClipKind.Image:
                _imageFallback.HorizontalAlignment = HorizontalAlignment.Center;
                AutomationProperties.SetName(_previewImage, Strings.Get("ImagePreview"));
                var image = new Border
                {
                    Margin = new Thickness(Space.M, Space.Xs, Space.M, Space.M), CornerRadius = Radius.Control, ClipToBounds = true,
                    Child = new Grid { Children = { _previewImage, _imageFallback } }
                };
                image.Bind(Border.BackgroundProperty, image.GetResourceObservable("MediaBrush"));
                return image;
            case ClipKind.Color:
                var swatch = new Border { CornerRadius = Radius.Control, BorderThickness = new Thickness(Stroke.Hairline) };
                var hex = Item.Text.Trim();
                if (Color.TryParse(hex, out var color))
                    swatch.Background = new SolidColorBrush(color);
                else
                    swatch.Bind(Border.BackgroundProperty, swatch.GetResourceObservable("MediaBrush"));
                swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable("LineBrush"));
                var value = PanelVisuals.Label(hex);
                value.FontFamily = FontFamily.Parse("Cascadia Mono, Menlo, DejaVu Sans Mono, monospace");
                value.FlowDirection = FlowDirection.LeftToRight;
                // The value lines up with the text of the other cards, not with the swatch above it.
                value.Margin = new Thickness(Space.Xs, 0);
                Grid.SetRow(value, 1);
                return new Grid
                {
                    Margin = new Thickness(Space.M, Space.Xs, Space.M, Space.M), RowDefinitions = new("*,28"), Children = { swatch, value }
                };
            case ClipKind.Files:
                var names = new StackPanel { Margin = margin, Spacing = Space.Xs, ClipToBounds = true };
                foreach (var path in Item.Files.Take(5))
                {
                    var row = new Grid { ColumnDefinitions = new("20,*") };
                    var fileGlyph = PanelVisuals.Glyph(Symbol.Document, IconSize.Inline, "MutedBrush");
                    fileGlyph.HorizontalAlignment = HorizontalAlignment.Left;
                    row.Children.Add(fileGlyph);
                    var name = path.TrimEnd('/', '\\').Split(['/', '\\']).LastOrDefault();
                    var label = PanelVisuals.Label(string.IsNullOrEmpty(name) ? path : name);
                    label.FlowDirection = Direction(label.Text ?? "");
                    AddColumn(row, label, 1);
                    names.Children.Add(row);
                }

                if (Item.Files.Length > 5)
                    names.Children.Add(PanelVisuals.Label(Strings.Format("FilesCount", Item.Files.Length), TextSize.Caption, "MutedBrush"));
                return names;
            default:
                var text = PreviewText(Item.Preview, 5, Direction(Item.Preview), "InkBrush");
                text.Margin = margin;
                return text;
        }
    }

    private Border CreateFooter()
    {
        var source = string.IsNullOrWhiteSpace(Item.SourceApp) ? Strings.Get("Clipboard") : Item.SourceApp;
        var captured = Item.CapturedAt.ToLocalTime();
        var time = captured.Date == DateTime.Today ? captured.ToString("t", Strings.Culture) : captured.ToString("d", Strings.Culture);
        var details = new Grid { ColumnDefinitions = new("16,6,*,8,Auto") };
        details.Children.Add(new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { _sourceFallback, _sourceImage } });
        AddColumn(details, PanelVisuals.Label(source, TextSize.Caption, "MutedBrush"), 2);
        AddColumn(details, PanelVisuals.Label(time, TextSize.Caption, "MutedBrush"), 4);
        var footer = new Border
        {
            Margin = new Thickness(Space.M, 0), Padding = new Thickness(Space.Xs, 0),
            BorderThickness = new Thickness(0, Stroke.Hairline, 0, 0),
            Child = details
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("LineBrush"));
        return footer;
    }

    private static TextBlock PreviewText(string text, int maxLines, FlowDirection direction, string brush)
    {
        var label = PanelVisuals.Label(text, TextSize.Body, brush);
        label.VerticalAlignment = VerticalAlignment.Top;
        label.TextWrapping = TextWrapping.Wrap;
        label.LineHeight = TextSize.LineHeight(TextSize.Body);
        label.MaxLines = maxLines;
        label.FlowDirection = direction;
        return label;
    }

    private static void AddColumn(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private void LoadBitmaps()
    {
        _sourceBitmap = Decode(_store.GetSourceIcon(Item.SourceApp), width: null);
        _sourceImage.Source = _sourceBitmap;
        _sourceFallback.IsVisible = _sourceBitmap is null;
        if (Item.ImageBytes is { Length: > 0 } bytes && Item.Kind == ClipKind.Image)
            _previewBitmap = Decode(bytes, width: 520);
        _previewImage.Source = _previewBitmap;
        _imageFallback.IsVisible = _previewBitmap is null;
    }

    private void ReleaseBitmaps()
    {
        _sourceImage.Source = _previewImage.Source = null;
        _sourceBitmap?.Dispose();
        _previewBitmap?.Dispose();
        _sourceBitmap = _previewBitmap = null;
    }

    private static Bitmap? Decode(ReadOnlyMemory<byte> png, int? width)
    {
        if (png.IsEmpty)
            return null;
        try
        {
            using var stream = new MemoryStream(png.ToArray(), writable: false);
            return width is int pixels ? Bitmap.DecodeToWidth(stream, pixels) : new Bitmap(stream);
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static FlowDirection Direction(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (System.Text.Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter))
                continue;
            return rune.Value is >= 0x0590 and <= 0x08FF or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFC
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }
}
