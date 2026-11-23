using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vstavka.Core;

namespace Vstavka.Desktop;

internal static class DemoSamples
{
    public static void Seed(HistoryStore store)
    {
        var now = DateTimeOffset.Now;
        var samples = new[]
        {
            new ClipItem { Kind = ClipKind.Files, Files = ["Field notes.pdf", "Trail map.png", "Reading list.txt"], SourceApp = "Files" },
            new ClipItem { Kind = ClipKind.Image, ImageBytes = ExampleImage(), SourceApp = "Photos" },
            new ClipItem { Kind = ClipKind.Color, Text = "#246AC5", SourceApp = "Color picker" },
            new ClipItem { Kind = ClipKind.Link, Text = "https://example.org/field-notes/early-spring", SourceApp = "Browser" },
            new ClipItem { Text = "Saturday field notes\nNorth path is open again. Take the short loop past the river and stop at the old bridge.", SourceApp = "Notes", IsPinned = true }
        };
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i].CapturedAt = now.AddMinutes((i - samples.Length) * 7);
            store.Add(samples[i]);
        }

        var collection = store.CreateCollection("Field notes", "CollectionTeal").Collection!;
        var saved = new ClipItem { Text = "Pack a field notebook and a spare pencil.", SourceApp = "Notes" };
        store.Add(saved);
        store.AssignCollection(saved.Id, collection.Id);
        store.SetSourceIcon("Notes", SourceIcon());
    }

    internal static byte[] ExampleImage()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 180));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            drawing.FillRectangle(new SolidColorBrush(Color.Parse("#CADDDB")), new Rect(0, 0, 320, 180));
            drawing.DrawEllipse(new SolidColorBrush(Color.Parse("#F9ECD0")), null, new Point(244, 55), 27, 27);
            drawing.DrawGeometry(new SolidColorBrush(Color.Parse("#729A92")), null,
                Geometry.Parse("M0,112 L69,64 127,112 188,79 290,121 320,103 320,180 0,180 Z"));
            drawing.DrawGeometry(new SolidColorBrush(Color.Parse("#436D62")), null,
                Geometry.Parse("M0,146 L49,126 110,153 175,118 238,151 320,125 320,180 0,180 Z"));
            drawing.DrawGeometry(new SolidColorBrush(Color.Parse("#E2D3AE")), null,
                Geometry.Parse("M63,180 L124,157 152,157 112,180 Z"));
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static byte[] SourceIcon()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(24, 24));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.Parse("#E7C273")), null, new Rect(1, 1, 22, 22), 5, 5);
            var ink = new Pen(new SolidColorBrush(Color.Parse("#654B2C")), 1.5);
            for (var y = 8; y <= 16; y += 4)
                drawing.DrawLine(ink, new Point(6, y), new Point(18, y));
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
