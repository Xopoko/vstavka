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
    private readonly Image _sourceImage = new() { Width = 16, Height = 16 };
    private readonly SymbolIcon _sourceFallback = PanelVisuals.Glyph(Symbol.AppGeneric, 15, "MutedBrush");
    private readonly Image _previewImage = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _imageFallback = PanelVisuals.Label(Strings.Get("ImagePreview"), 12, "MutedBrush");
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
        var typeLabel = PanelVisuals.Label(item.TypeLabel, 12, "MutedBrush");
        typeLabel.FontWeight = FontWeight.SemiBold;
        var pinLabel = Strings.Get(item.IsPinned ? "Unpin" : "Pin");
        var pin = PanelVisuals.IconButton(Symbol.Pin, pinLabel, togglePin);
        pin.Content = PanelVisuals.Glyph(Symbol.Pin, 15, item.IsPinned ? "AccentBrush" : null, item.IsPinned);
        pin.HorizontalAlignment = HorizontalAlignment.Right;

        var header = new Grid { ColumnDefinitions = new("21,*,36"), Margin = new Thickness(13, 0, 3, 0) };
        var glyph = PanelVisuals.Glyph(typeIcon, 14, item.Kind + "TypeBrush");
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
        var margin = new Thickness(13, 9, 13, 7);
        switch (Item.Kind)
        {
            case ClipKind.Link:
                var title = PanelVisuals.Label(Item.Title, 18);
                title.FontWeight = FontWeight.SemiBold;
                title.FlowDirection = FlowDirection.LeftToRight;
                return new StackPanel
                {
                    Margin = margin, Spacing = 7, ClipToBounds = true,
                    Children = { title, PreviewText(Item.Preview, 4, FlowDirection.LeftToRight, "MutedBrush") }
                };
            case ClipKind.Image:
                _imageFallback.HorizontalAlignment = HorizontalAlignment.Center;
                AutomationProperties.SetName(_previewImage, Strings.Get("ImagePreview"));
                var image = new Border
                {
                    Margin = new Thickness(8, 5, 8, 7), CornerRadius = new CornerRadius(7), ClipToBounds = true,
                    Child = new Grid { Children = { _previewImage, _imageFallback } }
                };
                image.Bind(Border.BackgroundProperty, image.GetResourceObservable("MediaBrush"));
                return image;
            case ClipKind.Color:
                var swatch = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
                var hex = Item.Text.Trim();
                if (Color.TryParse(hex, out var color))
                    swatch.Background = new SolidColorBrush(color);
                else
                    swatch.Bind(Border.BackgroundProperty, swatch.GetResourceObservable("MediaBrush"));
                swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable("LineBrush"));
                var value = PanelVisuals.Label(hex);
                value.FontFamily = FontFamily.Parse("Cascadia Mono, Menlo, DejaVu Sans Mono, monospace");
                value.FlowDirection = FlowDirection.LeftToRight;
                Grid.SetRow(value, 1);
                return new Grid { Margin = new Thickness(10, 5, 10, 7), RowDefinitions = new("*,29"), Children = { swatch, value } };
            case ClipKind.Files:
                var names = new StackPanel { Margin = margin, Spacing = 5, ClipToBounds = true };
                foreach (var path in Item.Files.Take(5))
                {
                    var row = new Grid { ColumnDefinitions = new("21,*") };
                    var fileGlyph = PanelVisuals.Glyph(Symbol.Document, 14, "MutedBrush");
                    fileGlyph.HorizontalAlignment = HorizontalAlignment.Left;
                    row.Children.Add(fileGlyph);
                    var name = path.TrimEnd('/', '\\').Split(['/', '\\']).LastOrDefault();
                    var label = PanelVisuals.Label(string.IsNullOrEmpty(name) ? path : name);
                    label.FlowDirection = Direction(label.Text ?? "");
                    AddColumn(row, label, 1);
                    names.Children.Add(row);
                }

                if (Item.Files.Length > 5)
                    names.Children.Add(PanelVisuals.Label(Strings.Format("FilesCount", Item.Files.Length), 12, "MutedBrush"));
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
        var details = new Grid { ColumnDefinitions = new("16,6,*,7,Auto") };
        details.Children.Add(new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { _sourceFallback, _sourceImage } });
        AddColumn(details, PanelVisuals.Label(source, 12, "MutedBrush"), 2);
        AddColumn(details, PanelVisuals.Label(time, 12, "MutedBrush"), 4);
        var footer = new Border
        {
            Margin = new Thickness(10, 0), Padding = new Thickness(2, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = details
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("LineBrush"));
        return footer;
    }

    private static TextBlock PreviewText(string text, int maxLines, FlowDirection direction, string brush)
    {
        var label = PanelVisuals.Label(text, 14, brush);
        label.VerticalAlignment = VerticalAlignment.Top;
        label.TextWrapping = TextWrapping.Wrap;
        label.LineHeight = 20;
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
