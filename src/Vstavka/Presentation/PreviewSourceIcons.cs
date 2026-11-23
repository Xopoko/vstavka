using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vstavka.Presentation;

internal static class PreviewSourceIcons
{
    internal static Dictionary<string, byte[]> Create()
    {
        var notes = Draw(Color.FromRgb(91, 75, 184), "M 9,9 H 23 V 12 H 9 Z M 9,15 H 23 V 18 H 9 Z M 9,21 H 19 V 24 H 9 Z");
        var browser = Draw(Color.FromRgb(27, 112, 147), "M 16,7 L 25,16 16,25 7,16 Z M 16,13 L 13,16 16,19 19,16 Z");
        var photos = Draw(Color.FromRgb(173, 79, 76), "M 7,23 L 14,11 18,18 21,14 26,23 Z");
        var editor = Draw(Color.FromRgb(37, 113, 89), "M 15,9 L 8,16 15,23 17,21 12,16 17,11 Z M 22,15 H 25 V 22 H 18 V 19 H 22 Z");
        return new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Notes"] = notes,
            ["Microsoft Edge"] = browser,
            ["Browser"] = browser,
            ["Photos"] = photos,
            ["Visual Studio Code"] = editor,
            ["Editor"] = editor
        };
    }

    private static byte[] Draw(Color color, string shape)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(1, 1, 30, 30), 7, 7);
            drawing.DrawGeometry(Brushes.White, null, Geometry.Parse(shape));
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
