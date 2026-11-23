using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Presentation;
using Vstavka.Views;

namespace Vstavka.StoreScenes;

internal static class Program
{
    internal const string Accent = "blue";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Strings.SetLanguage("en");
            var window = new CaptureWindow();
            string? output = ValueAfter(args, "--output");
            if (args.Contains("--verify", StringComparer.Ordinal) || args.Contains("--render", StringComparer.Ordinal))
            {
                object report = args.Contains("--render", StringComparer.Ordinal)
                    ? window.RenderScenes(Path.GetFullPath(ValueAfter(args, "--render") ?? throw new ArgumentException("--render needs a directory.")))
                    : new { passed = true, kind = "offscreen-view-layout-only", scenes = window.VerifyScenes() };
                if (output is not null)
                    File.WriteAllText(Path.GetFullPath(output), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                window.Close();
                ThemeManager.Shutdown();
                app.Shutdown();
                return 0;
            }
            int scene = int.TryParse(ValueAfter(args, "--scene"), out int selected) ? selected : 1;
            window.SetScene(scene);
            app.Run(window);
            ThemeManager.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            string? output = ValueAfter(args, "--output");
            if (output is not null)
                File.WriteAllText(Path.GetFullPath(output), JsonSerializer.Serialize(new { passed = false, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }

    private static string? ValueAfter(string[] args, string name)
    {
        int position = Array.IndexOf(args, name);
        return position >= 0 && position + 1 < args.Length ? args[position + 1] : null;
    }
}

internal sealed class CaptureWindow : Window
{
    private readonly Grid _surface = new();
    private readonly Dictionary<string, byte[]> _sourceIcons = PreviewSourceIcons.Create();
    private Window? _viewOwner;
    private FrameworkElement? _view;

    internal CaptureWindow()
    {
        Title = "Vstavka Store capture";
        Width = 1560;
        Height = 900;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _surface;
        PreviewKeyDown += OnSceneKey;
        Closed += (_, _) => ReleaseScene();
    }

    private void OnSceneKey(object sender, KeyEventArgs args)
    {
        int scene = args.Key switch { Key.F1 => 1, Key.F2 => 2, Key.F3 => 3, Key.F4 => 4, _ => 0 };
        if (scene > 0) { SetScene(scene); args.Handled = true; }
        else if (args.Key == Key.Escape) { Close(); args.Handled = true; }
    }

    internal void SetScene(int scene)
    {
        if (scene is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(scene));
        ReleaseScene();
        Strings.SetLanguage("en");
        bool dark = scene == 2;
        ThemeManager.Apply(dark ? "dark" : "light", Program.Accent);
        var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#121217" : "#ECECEF"));
        background.Freeze();
        _surface.Background = background;

        if (scene == 4)
        {
            var settings = new SettingsWindow(new AppSettings { Language = "en", Theme = "light", Accent = Program.Accent, Volume = .45 });
            settings.AppearancePreviewRequested += (theme, accent) => ThemeManager.Apply(theme, accent);
            _view = DetachContent(settings);
            _view.Width = 660;
            _view.Height = 780;
            _view.HorizontalAlignment = HorizontalAlignment.Center;
            _view.VerticalAlignment = VerticalAlignment.Center;
            var save = (Button)settings.FindName("SaveButton");
            foreach (var button in ((Panel)save.Parent).Children.OfType<Button>())
            {
                button.Focusable = false;
                button.IsHitTestVisible = false;
                button.IsCancel = false;
                button.IsDefault = false;
            }
        }
        else
        {
            var history = new HistoryStore(Path.GetTempPath());
            foreach (var item in HistoryItems()) history.Add(item);
            var prompts = history.CreateCollection("Prompts", "CollectionViolet").Collection!;
            history.CreateCollection("Work", "CollectionTeal");
            foreach (var item in PromptItems())
            {
                history.Add(item);
                history.AssignCollection(item.Id, prompts.Id);
            }
            var ribbon = new RibbonWindow(source => _sourceIcons.GetValueOrDefault(source), history);
            ribbon.SetItems(history.Items);
            ribbon.SetStatus(Strings.Format("SavedStatus", history.Items.Count), false);
            if (scene == 3)
            {
                ((StackPanel)ribbon.FindName("Chips")).Children.OfType<Button>().Single(button => button.Uid == prompts.Id)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            _view = DetachContent(ribbon);
            _view.Height = 338;
            _view.Margin = new Thickness(14, 0, 14, 14);
            _view.HorizontalAlignment = HorizontalAlignment.Stretch;
            _view.VerticalAlignment = VerticalAlignment.Bottom;
        }
        _surface.Children.Add(_view);
        Keyboard.ClearFocus();
        Focus();
    }

    private FrameworkElement DetachContent(Window owner)
    {
        var content = (FrameworkElement)owner.Content;
        var resources = owner.Resources;
        owner.Resources = new ResourceDictionary();
        owner.Content = null;
        content.Resources.MergedDictionaries.Add(resources);
        content.Language = owner.Language;
        content.FlowDirection = owner.FlowDirection;
        TextElement.SetFontFamily(content, owner.FontFamily);
        content.UseLayoutRounding = owner.UseLayoutRounding;
        _viewOwner = owner;
        return content;
    }

    private void ReleaseScene()
    {
        _surface.Children.Clear();
        if (_viewOwner is RibbonWindow ribbon) ribbon.AllowClose = true;
        _viewOwner?.Close();
        _viewOwner = null;
        _view = null;
    }

    internal object[] VerifyScenes()
    {
        // A never-shown Window is collapsed and can reset its child's arrange pass.
        // Verify the same scene root independently; the native path keeps it attached.
        Content = null;
        var result = new List<object>();
        for (int scene = 1; scene <= 4; scene++)
        {
            SetScene(scene);
            _surface.Measure(new Size(1560, 900));
            _surface.Arrange(new Rect(0, 0, 1560, 900));
            _surface.UpdateLayout();
            double expectedWidth = scene == 4 ? 660 : 1532;
            double expectedHeight = scene == 4 ? 780 : 338;
            // Layout rounding at 125% DPI can produce 337.6 DIPs for a 338-DIP row.
            if (_view is null || Math.Abs(_view.ActualWidth - expectedWidth) > 1 || Math.Abs(_view.ActualHeight - expectedHeight) > 1)
                throw new InvalidOperationException($"Scene {scene} layout has unexpected dimensions: {_view?.ActualWidth} x {_view?.ActualHeight}.");
            if (IsVisible || _viewOwner?.IsVisible == true)
                throw new InvalidOperationException("Offscreen verification unexpectedly showed a window.");
            int? count = _viewOwner is RibbonWindow ribbon ? ((ListBox)ribbon.FindName("Cards")).Items.Count : null;
            int? expectedCount = scene switch { 3 => 3, 4 => null, _ => 6 };
            if (count != expectedCount) throw new InvalidOperationException($"Scene {scene} has an unexpected card count: {count}.");
            if (_viewOwner is RibbonWindow owner)
            {
                var collections = ((StackPanel)owner.FindName("Chips")).Children.OfType<Button>()
                    .Where(chip => chip.DataContext is ClipCollection).ToArray();
                if (collections.Length != 2 || !collections.All(chip => chip.Visibility == Visibility.Visible && chip.ActualWidth > 0) ||
                    (scene == 3 && !Equals(collections[0].Tag, "selected")))
                    throw new InvalidOperationException($"Scene {scene} has unexpected collection chips.");
            }
            result.Add(new { scene, widthDip = _view.ActualWidth, heightDip = _view.ActualHeight, visibleCards = count, ownerWindowsShown = false });
        }
        return result.ToArray();
    }

    internal object RenderScenes(string directory)
    {
        // Two device pixels per DIP keep every 1-DIP line crisp at 3120 x 1800,
        // within the Store desktop screenshot limits.
        const double scale = 2;
        string[] names = { "01-history-light.png", "02-history-dark.png", "03-prompts-collection.png", "04-settings.png" };
        Directory.CreateDirectory(directory);
        Content = null;
        var files = new List<object>();
        for (int scene = 1; scene <= 4; scene++)
        {
            SetScene(scene);
            _surface.Measure(new Size(Width, Height));
            _surface.Arrange(new Rect(0, 0, Width, Height));
            _surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(Width * scale), (int)(Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(_surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(directory, names[scene - 1]);
            using (var stream = File.Create(path)) encoder.Save(stream);
            byte[] bytes = File.ReadAllBytes(path);
            files.Add(new { scene, file = names[scene - 1], width = bitmap.PixelWidth, height = bitmap.PixelHeight, bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        }
        return new
        {
            schema = "vstavka.store-screenshots.v2",
            description = "Offscreen renders of the real RibbonWindow and SettingsWindow content with synthetic items; no clipboard, desktop or personal data.",
            appVersion = typeof(RibbonWindow).Assembly.GetName().Version?.ToString(),
            language = "en",
            accent = Program.Accent,
            scale,
            files
        };
    }

    private static ClipItem[] HistoryItems()
    {
        var time = new DateTimeOffset(DateTime.Today.AddHours(10.5));
        return new[]
        {
            new ClipItem { Id = "scene-note", Kind = ClipKind.Text, Text = "Project notes\n\nKeep the layout simple.\nUse one quiet accent color.\nReview the final screens together.", SourceApp = "Notes", CapturedAt = time, IsPinned = true },
            new ClipItem { Id = "scene-link", Kind = ClipKind.Link, Text = "https://example.org/design/ideas", SourceApp = "Browser", CapturedAt = time.AddMinutes(-1), IsPinned = true },
            new ClipItem { Id = "scene-image", Kind = ClipKind.Image, ImageBytes = Landscape(), SourceApp = "Photos", CapturedAt = time.AddMinutes(-2) },
            new ClipItem { Id = "scene-color", Kind = ClipKind.Color, Text = "#246AC5", SourceApp = "Design", CapturedAt = time.AddMinutes(-3) },
            new ClipItem { Id = "scene-files", Kind = ClipKind.Files, Files = new[] { @"C:\Samples\Project brief.pdf", @"C:\Samples\Color palette.png" }, SourceApp = "File Explorer", CapturedAt = time.AddMinutes(-4) },
            new ClipItem { Id = "scene-code", Kind = ClipKind.Text, Text = "const colors = {\n  accent: '#246AC5',\n  ink: '#1F1F23',\n  surface: '#F5F5F6'\n};", SourceApp = "Editor", CapturedAt = time.AddMinutes(-5) }
        };
    }

    private static ClipItem[] PromptItems()
    {
        var time = new DateTimeOffset(DateTime.Today.AddHours(10.5));
        return new[]
        {
            new ClipItem { Id = "prompt-summary", Text = "Summarize these notes\n\nUse three bullets. Keep decisions and open questions separate.", SourceApp = "Notes", CapturedAt = time, IsPinned = true },
            new ClipItem { Id = "prompt-reply", Text = "Draft a friendly reply\n\nConfirm the date and ask who will bring examples.", SourceApp = "Editor", CapturedAt = time.AddMinutes(-1) },
            new ClipItem { Id = "prompt-checklist", Text = "Make a checklist\n\nOrder the steps by what needs to happen first.", SourceApp = "Notes", CapturedAt = time.AddMinutes(-2) }
        };
    }

    private static byte[] Landscape()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(218, 224, 248)), null, new Rect(0, 0, 720, 420));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(249, 220, 174)), null, new Point(530, 110), 53, 53);
            DrawMountain(drawing, Color.FromRgb(153, 156, 205), new Point(-30, 300), new Point(225, 65), new Point(505, 320));
            DrawMountain(drawing, Color.FromRgb(117, 116, 172), new Point(220, 325), new Point(485, 130), new Point(760, 320));
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(117, 150, 188)), null, new Rect(0, 300, 720, 120));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(146, 176, 210)), null, new Point(352, 364), 270, 10);
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(163, 188, 220)), null, new Point(386, 397), 202, 5);
        }
        var bitmap = new RenderTargetBitmap(720, 420, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void DrawMountain(DrawingContext drawing, Color color, Point left, Point peak, Point right)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(left, isFilled: true, isClosed: true);
            context.LineTo(peak, isStroked: true, isSmoothJoin: false);
            context.LineTo(right, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        drawing.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }
}
