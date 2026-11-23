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
        foreach (var app in samples.Select(sample => sample.SourceApp).Distinct())
            store.SetSourceIcon(app, SourceIcon(app));
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

    /// <summary>
    /// A small app icon for the sample sources, drawn on a 24-unit grid into 32 pixels, the largest icon the
    /// history keeps, so the 16-pixel card icon stays sharp on high-DPI screens.
    /// </summary>
    internal static byte[] SourceIcon(string app)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(128, 128));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            var tile = app switch
            {
                "Browser" => "#2F7BD8",
                "Color picker" => "#F1EEF8",
                "Photos" => "#3E8E68",
                "Files" => "#4A86E8",
                "Code" => "#252A31",
                _ => "#E7C273"
            };
            drawing.DrawRectangle(Fill(tile), null, new Rect(1, 1, 22, 22), 5, 5);
            switch (app)
            {
                case "Browser":
                    var globe = Stroke("#FFFFFF");
                    drawing.DrawEllipse(null, globe, new Point(12, 12), 6.5, 6.5);
                    drawing.DrawEllipse(null, globe, new Point(12, 12), 2.8, 6.5);
                    drawing.DrawLine(globe, new Point(5.5, 12), new Point(18.5, 12));
                    break;
                case "Color picker":
                    drawing.DrawEllipse(Fill("#E5484D"), null, new Point(9, 9), 3.2, 3.2);
                    drawing.DrawEllipse(Fill("#F5A524"), null, new Point(15, 9), 3.2, 3.2);
                    drawing.DrawEllipse(Fill("#30A46C"), null, new Point(9, 15), 3.2, 3.2);
                    drawing.DrawEllipse(Fill("#3E63DD"), null, new Point(15, 15), 3.2, 3.2);
                    break;
                case "Photos":
                    drawing.DrawEllipse(Fill("#FFD866"), null, new Point(16.5, 8.5), 2.3, 2.3);
                    drawing.DrawGeometry(Fill("#FFFFFF"), null, Geometry.Parse("M4.5,18 L10,10.5 13.5,15 16,12.5 19.5,18 Z"));
                    break;
                case "Files":
                    drawing.DrawGeometry(Fill("#FFFFFF"), null, Geometry.Parse("M5.5,7.5 H10 L11.5,9 H18.5 V17 H5.5 Z"));
                    break;
                case "Code":
                    drawing.DrawGeometry(null, Stroke("#8CC8FF"), Geometry.Parse("M9.5,8.5 L6,12 9.5,15.5 M14.5,8.5 L18,12 14.5,15.5"));
                    break;
                default:
                    var ink = Stroke("#654B2C");
                    for (var y = 8; y <= 16; y += 4)
                        drawing.DrawLine(ink, new Point(6, y), new Point(18, y));
                    break;
            }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static SolidColorBrush Fill(string color) => new(Color.Parse(color));

    private static Pen Stroke(string color) =>
        new(Fill(color), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
}
