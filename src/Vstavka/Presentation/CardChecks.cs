using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Presentation;

/// <summary>Synthetic checks for retained card state; no window, clipboard, or history access.</summary>
internal static class CardChecks
{
    internal static bool VerifyRetainedCards()
    {
        var originalLanguage = Strings.Language;
        var originalTheme = ThemeManager.CurrentTheme;
        var originalAccent = ThemeManager.CurrentAccent;
        try
        {
            Strings.SetLanguage("en");
            ThemeManager.Apply("light", "violet");
            var capturedAt = new DateTimeOffset(2001, 6, 15, 12, 30, 0, TimeSpan.Zero);
            var item = new ClipItem { Text = "first\nsecond", CapturedAt = capturedAt, SourceApp = "Fixture" };
            var card = new ClipCard(item);
            ReadBindings(card);
            int notifications = 0;
            card.PropertyChanged += (_, _) => notifications++;
            card.Refresh(item);
            Require(notifications == 0, "Unchanged refresh must not invalidate bindings.");

            item.IsPinned = true;
            card.Refresh(item);
            Require(card.PinTooltip == Strings.Get("Unpin") && card.AccessibleName.Contains(Strings.Get("PinnedAccessible")), "Pin refresh failed.");
            item.IsPinned = false;
            card.Refresh(item);
            Require(card.PinTooltip == Strings.Get("Pin") && !card.AccessibleName.Contains(Strings.Get("PinnedAccessible")), "Unpin refresh failed.");

            item.Text = "שלום";
            item.SourceApp = "Changed source";
            item.CapturedAt = capturedAt.AddDays(-3);
            card.Refresh(item);
            Require(card.Title == item.Text && card.Preview == item.Text && card.ContentFlowDirection == FlowDirection.RightToLeft && card.Source == item.SourceApp, "Mutable text, source, or direction refresh failed.");
            Require(card.Time == item.CapturedAt.ToLocalTime().ToString("m", Strings.Culture), "Capture date refresh failed.");

            var replacement = new ClipItem { Id = item.Id, Kind = ClipKind.Files, Files = ["C:\\Fixture\\first.txt", "C:\\Fixture\\second.pdf"] };
            card.Refresh(replacement);
            ReadBindings(card);
            replacement.Files[0] = "C:\\Fixture\\changed.txt";
            card.Refresh(replacement);
            Require(ReferenceEquals(card.Item, replacement) && card.FileNames[0] == "changed.txt" && card.Preview.Contains("changed.txt"), "Replacement identity or file-array mutation refresh failed.");
            foreach (var locale in Strings.AvailableLanguages)
            {
                Strings.SetLanguage(locale.Code);
                card.Refresh(replacement);
                Require(card.Title == Strings.Format("FilesCount", 2) && card.TypeLabel == $"{Strings.Get("Files")} · {2.ToString(Strings.Culture)}" &&
                    card.Source == Strings.Get("Clipboard"), "Locale refresh failed: " + locale.Code);
            }

            Strings.SetLanguage("en");
            var color = new ClipItem { Kind = ClipKind.Color, Text = "#ff0000" };
            card.Refresh(color);
            Require(((SolidColorBrush)card.Swatch).Color == Colors.Red, "Color swatch failed.");
            color.Text = "#0000ff";
            card.Refresh(color);
            Require(((SolidColorBrush)card.Swatch).Color == Colors.Blue, "Mutable color refresh failed.");

            var imageBytes = CreatePng();
            var imageItem = new ClipItem { Kind = ClipKind.Image, ImageBytes = imageBytes };
            var imageCard = new ClipCard(imageItem);
            var originalImage = imageCard.Image;
            imageCard.Refresh(imageItem);
            Require(ReferenceEquals(originalImage, imageCard.Image) && originalImage is { IsFrozen: true }, "Unchanged visible image must remain cached and frozen.");
            imageItem.ImageBytes = (byte[])imageBytes.Clone();
            imageCard.Refresh(imageItem);
            Require(!ReferenceEquals(originalImage, imageCard.Image) && imageCard.Image is { IsFrozen: true }, "Replaced image bytes must invalidate the thumbnail.");
            imageItem.ImageBytes = [0, 1, 2, 3];
            imageCard.Refresh(imageItem);
            Require(imageCard.Image is null, "Invalid image must produce an empty preview.");
            imageItem.ImageBytes = imageBytes;
            imageCard.Refresh(imageItem);
            Require(imageCard.Image is { IsFrozen: true }, "A valid replacement must recover from an invalid preview.");

            var themedBrush = card.TypeBrush;
            var lightColor = ((SolidColorBrush)themedBrush).Color;
            ThemeManager.Apply("dark", "violet");
            Require(ReferenceEquals(themedBrush, card.TypeBrush) && !themedBrush.IsFrozen &&
                (SystemParameters.HighContrast || ((SolidColorBrush)themedBrush).Color != lightColor), "Retained cards must preserve live theme brush identity and colors.");
            return true;
        }
        finally
        {
            Strings.SetLanguage(originalLanguage);
            ThemeManager.Apply(originalTheme, originalAccent);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ReadBindings(ClipCard card)
    {
        _ = card.Title;
        _ = card.Preview;
        _ = card.TypeLabel;
        _ = card.Source;
        _ = card.Time;
        _ = card.AccessibleName;
        _ = card.Domain;
        _ = card.ColorText;
        _ = card.FileNames;
        _ = card.ContentFlowDirection;
    }

    private static byte[] CreatePng()
    {
        const int width = 16, height = 8;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(x * 12);
                pixels[offset + 1] = (byte)(y * 24);
                pixels[offset + 2] = 120;
                pixels[offset + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
