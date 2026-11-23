using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Shell;
using Vstavka.Views;

namespace Vstavka.Presentation;

/// <summary>Offscreen rendering and checks using synthetic items, without accessing the clipboard.</summary>
internal static partial class UiPreview
{
    internal static void Render(string path, ClipItem[] items)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        Strings.SetLanguage("en");
        ThemeManager.Apply("light", "windows");
        var icons = PreviewSourceIcons.Create();
        var history = new HistoryStore(Path.GetTempPath());
        foreach (var item in items) history.Add(item);
        var prompts = history.CreateCollection("Prompts", "CollectionViolet").Collection!;
        var work = history.CreateCollection("Work", "CollectionTeal").Collection!;
        var promptTexts = new[]
        {
            "Summarize these notes in three bullets.\n\nKeep decisions and open questions separate.",
            "Draft a friendly reply.\n\nConfirm the date and ask who will bring examples.",
            "Turn this outline into a checklist ordered by what needs to happen first."
        };
        var time = DateTimeOffset.Now.AddDays(-1);
        for (int index = 0; index < promptTexts.Length; index++)
        {
            var item = new ClipItem
            {
                Text = promptTexts[index], SourceApp = "Notes",
                CapturedAt = time.AddMinutes(-index), IsPinned = index == 0
            };
            history.Add(item);
            history.AssignCollection(item.Id, prompts.Id);
        }
        var ribbon = new RibbonWindow(GetSourceIcon, history);
        ribbon.SetItems(history.Items);
        ribbon.SetStatus(Strings.Get("DemoStatus"), false);
        Render((FrameworkElement)ribbon.Content, path, 1620, 338);
        SelectChip(ribbon, prompts.Id);
        RenderCompanion(ribbon, "preview-collection-prompts.png", 1200, 338);
        SelectChip(ribbon, work.Id);
        RenderCompanion(ribbon, "preview-collection-empty.png", 1200, 338);
        RenderCollectionEditor(ribbon, "preview-collection-create.png");
        SelectChip(ribbon, "all");
        RenderCompanion(ribbon, "preview-ribbon-compact.png", 1000, 338);
        ribbon.SetStatus(Strings.Get("Paused"), true);
        RenderCompanion(ribbon, "preview-ribbon-paused.png", 1000, 338);
        ribbon.SetStatus(Strings.Get("DemoStatus"), false);
        ribbon.SetItems(Array.Empty<ClipItem>());
        RenderCompanion(ribbon, "preview-empty.png", 1100, 338);
        ribbon.SetItems(history.Items);
        ((TextBox)ribbon.FindName("SearchBox")).Text = "no matching item 912887";
        RenderCompanion(ribbon, "preview-search-empty.png", 1100, 338);
        var settings = new SettingsWindow(new AppSettings());
        RenderCompanion(settings, "preview-settings.png", 650, 739);
        ((ScrollViewer)settings.FindName("SettingsScroll")).ScrollToBottom();
        RenderCompanion(settings, "preview-settings-privacy.png", 650, 739);
        ((Button)settings.FindName("ClearHistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((ScrollViewer)settings.FindName("SettingsScroll")).ScrollToBottom();
        RenderCompanion(settings, "preview-settings-confirm-clear.png", 650, 739);
        RenderWelcome("preview-welcome.png");
        ribbon.AllowClose = true;
        ribbon.Close();
        settings.Close();

        RenderVariant("en", "dark", "windows", 1200);
        RenderVariant("de", "light", "teal", 640);
        RenderWelcome("preview-welcome-de.png");
        RenderVariant("ar", "dark", "blue", 1000);
        RenderWelcome("preview-welcome-ar.png");
        RenderVariant("ja", "light", "rose", 1000);
        RenderVariant("zh-Hant", "light", "blue", 1000);
        RenderVariant("cs", "dark", "teal", 640);
        RenderVariant("hu", "light", "amber", 640);
        RenderVariant("uk", "dark", "violet", 1000);

        ThemeManager.SetPreviewHighContrast(true);
        try
        {
            RenderVariant("en", "light", "windows", 1620, "hc");
        }
        finally
        {
            ThemeManager.SetPreviewHighContrast(false);
        }

        void RenderCompanion(Window window, string fileName, int width, int height)
        {
            var companion = Path.Combine(directory, fileName);
            if (!string.Equals(path, companion, StringComparison.OrdinalIgnoreCase))
                Render((FrameworkElement)window.Content, companion, width, height);
        }

        void RenderWelcome(string fileName)
        {
            var welcome = new WelcomeWindow(new AppSettings().Hotkey);
            var content = (FrameworkElement)welcome.Content;
            int width = (int)welcome.Width;
            content.Measure(new Size(width, double.PositiveInfinity));
            int height = (int)Math.Ceiling(Math.Min(content.DesiredSize.Height, welcome.MaxHeight));
            RenderCompanion(welcome, fileName, width, height);
            welcome.Close();
        }

        void RenderVariant(string language, string theme, string accent, int width, string? appearance = null)
        {
            appearance ??= theme;
            Strings.SetLanguage(language);
            ThemeManager.Apply(theme, accent);
            var localizedRibbon = new RibbonWindow(GetSourceIcon, history);
            localizedRibbon.SetItems(history.Items);
            localizedRibbon.SetStatus(Strings.Get("DemoStatus"), false);
            RenderCompanion(localizedRibbon, $"preview-ribbon-{language}-{appearance}.png", width, 338);
            if (language is "en" or "ar")
            {
                SelectChip(localizedRibbon, prompts.Id);
                RenderCompanion(localizedRibbon, $"preview-collection-{language}-{appearance}.png", width, 338);
                RenderCollectionEditor(localizedRibbon, $"preview-collection-create-{language}-{appearance}.png");
            }
            var localizedSettings = new SettingsWindow(new AppSettings { Language = language, Theme = theme, Accent = accent });
            RenderCompanion(localizedSettings, $"preview-settings-{language}-{appearance}.png", 650, 739);
            localizedRibbon.AllowClose = true;
            localizedRibbon.Close();
            localizedSettings.Close();
        }

        void RenderCollectionEditor(RibbonWindow owner, string fileName)
        {
            owner.ConfigureCollectionEditor(null);
            var popup = (Popup)owner.FindName("CollectionPopup");
            var surface = (FrameworkElement)popup.Child;
            popup.Child = null;
            try
            {
                surface.FlowDirection = owner.FlowDirection;
                surface.Language = owner.Language;
                surface.Measure(new Size(surface.Width, double.PositiveInfinity));
                Render(surface, Path.Combine(directory, fileName), (int)surface.Width, (int)Math.Ceiling(surface.DesiredSize.Height));
            }
            finally { popup.Child = surface; }
        }

        ReadOnlyMemory<byte> GetSourceIcon(string source) => icons.GetValueOrDefault(source);
    }

    private static void SelectChip(RibbonWindow ribbon, string id) =>
        ((StackPanel)ribbon.FindName("Chips")).Children.OfType<Button>().Single(button => button.Uid == id)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static bool VerifyInteractions()
    {
        var note = new ClipItem { Id = "ui-note", Kind = ClipKind.Text, Text = "Лавандовый план", SourceApp = "Заметки", IsPinned = true };
        var link = new ClipItem { Id = "ui-link", Kind = ClipKind.Link, Text = "https://example.org/notes", SourceApp = "Edge" };
        var file = new ClipItem { Id = "ui-file", Kind = ClipKind.Files, Files = new[] { @"C:\Samples\Идеи.txt" }, SourceApp = "Проводник" };
        var ribbon = new RibbonWindow();
        ribbon.SetItems(new[] { file, link, note });
        var cards = (ListBox)ribbon.FindName("Cards");
        var search = (TextBox)ribbon.FindName("SearchBox");
        var chips = (StackPanel)ribbon.FindName("Chips");
        var pause = (Button)ribbon.FindName("PauseButton");
        ribbon.SetStatus(Strings.Get("Paused"), true);
        if (!ReferenceEquals(pause.Background, ThemeManager.GetBrush("SelectionBrush")) ||
            !ReferenceEquals(pause.Foreground, ThemeManager.GetBrush("SelectionForegroundBrush"))) return false;
        var root = (FrameworkElement)ribbon.Content;
        Arrange(root, 1000, 338);
        ribbon.SetStatus(Strings.Get("DemoStatus"), false);
        Arrange(root, 1100, 338);
        var statusLabel = (TextBlock)ribbon.FindName("StatusLabel");
        if (pause.Tag is not null || LayoutInformation.GetLayoutClip(statusLabel) is not null) return false;
        if (cards.Items.Count != 3 || cards.Items[0] is not ClipCard { Item.Id: "ui-note" }) return false;
        search.Text = "ЛАВАНДОВЫЙ";
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != note.Id) return false;
        search.Text = "Идеи.txt";
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != file.Id) return false;
        search.Text = "  план\tЗАМЕТКИ  ";
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != note.Id) return false;
        search.Text = Strings.Get("Link") + " example";
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != link.Id) return false;
        search.Text = "Идеи Проводник";
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != file.Id) return false;
        search.Text = string.Empty;
        chips.Children.OfType<Button>().Single(x => x.Uid == "link").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != link.Id) return false;
        chips.Children.OfType<Button>().Single(x => x.Uid == "pinned").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (cards.Items.Count != 1 || ((ClipCard)cards.Items[0]).Item.Id != note.Id) return false;
        search.Text = "несуществующее";
        if (cards.Items.Count != 0 || ((FrameworkElement)ribbon.FindName("EmptyState")).Visibility != Visibility.Visible) return false;
        var original = new AppSettings { Volume = 0.37, ExcludedApps = new[] { "PrivateApp" }, Language = "de", Theme = "dark", Accent = "teal" };
        var settings = new SettingsWindow(original);
        ((Slider)settings.FindName("VolumeSlider")).Value = 0.72;
        ((TextBox)settings.FindName("ExcludedAppsBox")).Text = "DifferentApp";
        (string Theme, string Accent)? preview = null;
        settings.AppearancePreviewRequested += (theme, accent) => preview = (theme, accent);
        var themes = (ComboBox)settings.FindName("ThemeCombo");
        themes.SelectedItem = themes.Items.OfType<ComboBoxItem>().Single(x => (string)x.Tag == "light");
        if (preview != ("light", "teal") || settings.Result is not null ||
            original.Theme != "dark" || original.Volume != 0.37 || original.ExcludedApps[0] != "PrivateApp") return false;

        int clearCalls = 0;
        settings.ClearHistoryRequested += () => ++clearCalls == 2;
        var clear = (Button)settings.FindName("ClearHistoryButton");
        var confirm = (Button)settings.FindName("ConfirmClearButton");
        var confirmation = (FrameworkElement)settings.FindName("ClearConfirmation");
        var status = (TextBlock)settings.FindName("ClearStatus");
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (clearCalls != 0 || confirmation.Visibility != Visibility.Visible) return false;
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (clearCalls != 1 || status.Visibility != Visibility.Visible || status.Text != Strings.Get("SaveFailed")) return false;
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return clearCalls == 2 && status.Visibility == Visibility.Visible && status.Text == Strings.Get("HistoryCleared");
    }

    internal static bool VerifyCardRefresh()
    {
        string language = Strings.Language;
        var ribbon = new RibbonWindow();
        try
        {
            Strings.SetLanguage("en");
            var items = Enumerable.Range(0, 100).Select(i => new ClipItem { Id = "refresh-" + i, Text = "Sample " + i, CapturedAt = DateTimeOffset.UnixEpoch.AddMinutes(i) }).ToList();
            ribbon.SetItems(items);
            var cards = (ListBox)ribbon.FindName("Cards");
            var search = (TextBox)ribbon.FindName("SearchBox");
            Arrange((FrameworkElement)ribbon.Content, 1200, 338);
            var source = cards.ItemsSource;
            var first = (ClipCard)cards.Items[0];
            var container = cards.ItemContainerGenerator.ContainerFromIndex(0);
            cards.SelectedIndex = 2;
            var selected = cards.SelectedItem;
            ribbon.SetItems(items);
            Arrange((FrameworkElement)ribbon.Content, 1200, 338);
            if (!ReferenceEquals(source, cards.ItemsSource) || !ReferenceEquals(first, cards.Items[0]) ||
                container is null || !ReferenceEquals(container, cards.ItemContainerGenerator.ContainerFromIndex(0)) ||
                !ReferenceEquals(selected, cards.SelectedItem)) return false;
            // Reordering, mutable metadata, replacement objects and deleted items must
            // update reused cards without losing selection or leaving stale payloads.
            items[0].IsPinned = true;
            ribbon.SetItems(items);
            if (((ClipCard)cards.Items[0]).Item.Id != items[0].Id || !ReferenceEquals(selected, cards.SelectedItem)) return false;
            items[^1] = new ClipItem { Id = first.Item.Id, Text = "Changed payload", CapturedAt = DateTimeOffset.UnixEpoch.AddMinutes(200) };
            ribbon.SetItems(items);
            if (first.Preview != "Changed payload") return false;
            search.Text = "Changed payload";
            if (cards.Items.Count != 1 || !ReferenceEquals(first, cards.Items[0])) return false;
            items.RemoveAt(items.Count - 1); ribbon.SetItems(items);
            if (cards.Items.Count != 0) return false;
            search.Text = "";
            var card = (ClipCard)cards.Items[0]; var english = card.TypeLabel;
            Strings.SetLanguage("uk"); ribbon.SetItems(items);
            if (card.TypeLabel != card.Item.TypeLabel || card.TypeLabel == english) return false;
            search.Text = "Sample 1";
            var expected = items.Count(x => x.Text.Contains("Sample", StringComparison.OrdinalIgnoreCase) && x.Text.Contains('1'));
            if (cards.Items.Count != expected) return false;
            search.Text = "Sample 2";
            if (cards.Items.Count != items.Count(x => x.Text.Contains('2')) ||
                cards.Items.Cast<ClipCard>().Any(x => !x.Item.Text.Contains("Sample", StringComparison.OrdinalIgnoreCase) || !x.Item.Text.Contains('2'))) return false;
            return true;
        }
        finally { Strings.SetLanguage(language); ribbon.AllowClose = true; ribbon.Close(); }
    }

    internal static bool VerifyAppearanceAndLayout()
    {
        string language = Strings.Language, theme = ThemeManager.CurrentTheme, accent = ThemeManager.CurrentAccent;
        try
        {
            ThemeManager.Apply("light", "violet");
            var stableBrush = (SolidColorBrush)ThemeManager.GetBrush("WindowBrush");
            if (stableBrush.CanFreeze || stableBrush.IsFrozen) return false;
            Color light = stableBrush.Color;
            foreach (var palette in new[] { "light", "dark" })
            {
                ThemeManager.Apply(palette, "teal");
                if (!ReferenceEquals(stableBrush, ThemeManager.GetBrush("WindowBrush"))) return false;
                if (!SystemParameters.HighContrast && ThemeManager.IsDark != (palette == "dark")) return false;
                if (!SystemParameters.HighContrast && palette == "dark" && stableBrush.Color == light) return false;
                foreach (var (foreground, background) in new[]
                {
                    ("MutedBrush", "WindowBrush"), ("MutedBrush", "SurfaceBrush"),
                    ("SelectionForegroundBrush", "SelectionBrush"), ("SelectionTextBrush", "SelectionBrush"),
                    ("OnDangerBrush", "DangerFillBrush")
                })
                {
                    if (!SystemParameters.HighContrast && ThemeManager.Contrast(BrushColor(foreground), BrushColor(background)) < 4.5)
                        throw new InvalidOperationException($"Low contrast in {palette}: {foreground} on {background}.");
                }
                if (!SystemParameters.HighContrast && ClipCollection.Colors.Any(color =>
                    ThemeManager.Contrast(BrushColor(color), BrushColor("SurfaceBrush")) < 3 ||
                    ThemeManager.Contrast(BrushColor(color), BrushColor("WindowBrush")) < 3))
                    throw new InvalidOperationException($"Low contrast for a collection color in {palette}.");
            }
            var text = new ClipItem { Text = "payload stays unchanged" };
            var history = new HistoryStore(Path.GetTempPath());
            history.Add(text);
            history.CreateCollection("Prompts", "CollectionViolet");
            history.CreateCollection("Work", "CollectionTeal");
            var rtlText = new ClipItem { Text = "مرحبا بالعالم" };
            if (new ClipCard(text).ContentFlowDirection != FlowDirection.LeftToRight || new ClipCard(rtlText).ContentFlowDirection != FlowDirection.RightToLeft) return false;
            foreach (var option in Strings.AvailableLanguages)
            {
                Strings.SetLanguage(option.Code);
                var ribbon = new RibbonWindow(null, history);
                ribbon.SetItems(history.Items);
                if (ribbon.FlowDirection != (Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)) return false;
                foreach (int width in new[] { 560, 640, 1000, 1600 })
                {
                    var root = (FrameworkElement)ribbon.Content;
                    Arrange(root, width, 338);
                    var categories = (FrameworkElement)ribbon.FindName("CategoriesRail");
                    var search = (FrameworkElement)ribbon.FindName("SearchContainer");
                    var actions = (FrameworkElement)ribbon.FindName("ToolbarActions");
                    var cr = Bounds(categories, root); var sr = Bounds(search, root); var ar = Bounds(actions, root);
                    if (search.ActualWidth < 220 || categories.ActualWidth < 60 || cr.IntersectsWith(sr) || sr.IntersectsWith(ar) || cr.IntersectsWith(ar)) return false;
                    if (Math.Abs(cr.Top + cr.Height / 2 - (sr.Top + sr.Height / 2)) > 3) return false;
                    if (ar.Left < 0 || ar.Right > width || sr.Left < 0 || sr.Right > width) return false;
                }
                if (text.Text != "payload stays unchanged") return false;
                ribbon.AllowClose = true; ribbon.Close();
            }
            VerifyRenderedControls(SystemParameters.HighContrast);
            ThemeManager.SetPreviewHighContrast(true);
            VerifyRenderedControls(highContrast: true);
            return true;
        }
        finally
        {
            ThemeManager.SetPreviewHighContrast(false);
            Strings.SetLanguage(language);
            ThemeManager.Apply(theme, accent);
        }
    }

    internal static bool VerifySourceIcons()
    {
        var icons = PreviewSourceIcons.Create();
        var store = new HistoryStore(Path.GetTempPath());
        store.Add(new ClipItem { Text = "Source with an icon", SourceApp = "Notes" });
        store.Add(new ClipItem { Text = "Same source in another card", SourceApp = "NOTES" });
        store.Add(new ClipItem { Text = "Source without an icon", SourceApp = "Fixture" });
        var ribbon = new RibbonWindow(store.GetSourceIcon);
        try
        {
            var root = (FrameworkElement)ribbon.Content;
            var cards = (ListBox)ribbon.FindName("Cards");
            ribbon.SetItems(store.Items);
            Arrange(root, 1100, 338);
            var retained = cards.Items.Cast<ClipCard>().Single(card => card.Source == "Notes");
            VerifyIcons(expectIcons: false);
            if (!store.SetSourceIcon("notes", icons["Notes"]))
                throw new InvalidOperationException("The synthetic source icon was rejected.");
            ribbon.SetItems(store.Items);
            Arrange(root, 1100, 338);
            if (!cards.Items.Contains(retained) || retained.SourceIcon is not BitmapSource { IsFrozen: true, PixelWidth: 32, PixelHeight: 32 } ||
                !ReferenceEquals(retained.SourceIcon, cards.Items.Cast<ClipCard>().Single(card => card.Source == "NOTES").SourceIcon))
                throw new InvalidOperationException("Existing cards must share the newly loaded, frozen icon for their source.");
            VerifyIcons(expectIcons: true);
            ribbon.FlowDirection = FlowDirection.RightToLeft;
            Arrange(root, 1100, 338);
            VerifyIcons(expectIcons: true);
            return true;

            void VerifyIcons(bool expectIcons)
            {
                var containers = Descendants<ListBoxItem>(root).ToArray();
                if (containers.Length != 3) throw new InvalidOperationException($"Expected 3 realized cards, found {containers.Length}.");
                foreach (var container in containers)
                {
                    var card = (ClipCard)container.Content;
                    var image = Descendants<DecorativeImage>(container).Single(element => element.Name == "SourceIconImage");
                    var fallback = Descendants<DecorativeGlyph>(container).Single(element => element.Name == "SourceIconFallback");
                    var label = Descendants<TextBlock>(container).Single(element => element.Name == "CardSourceLabel");
                    var hasIcon = expectIcons && card.Source != "Fixture";
                    if ((image.Visibility == Visibility.Visible) != hasIcon || (fallback.Visibility == Visibility.Visible) == hasIcon)
                        throw new InvalidOperationException($"Incorrect icon or fallback visibility for {card.Source}.");
                    var iconBounds = Bounds(hasIcon ? image : fallback, root);
                    var labelBounds = Bounds(label, root);
                    // Coordinates relative to the layout root are logical even when it mirrors visually.
                    if (iconBounds.Right > labelBounds.Left || Math.Abs(iconBounds.Top + iconBounds.Height / 2 - labelBounds.Top - labelBounds.Height / 2) > 1)
                        throw new InvalidOperationException($"Icon alignment for {card.Source} in {root.FlowDirection}: {iconBounds}, {labelBounds}.");
                }
            }
        }
        finally { ribbon.AllowClose = true; ribbon.Close(); }
    }

    private static void VerifyRenderedControls(bool highContrast)
    {
        Strings.SetLanguage("en"); ThemeManager.Apply("dark", "violet");
        var ribbon = new RibbonWindow();
        ribbon.SetItems(new[]
        {
            new ClipItem { Kind = ClipKind.Text, Text = "Readable dark text" },
            new ClipItem { Kind = ClipKind.Color, Text = "#A78BFA" },
            new ClipItem { Kind = ClipKind.Files, Files = new[] { @"C:\Samples\Readable.txt" } }
        });
        Arrange((FrameworkElement)ribbon.Content, 1100, 338);
        var root = (FrameworkElement)ribbon.Content;
        var labels = Descendants<TextBlock>(root).ToArray();
        foreach (string name in new[] { "PayloadText", "ColorValueLabel", "FileNameLabel" })
        {
            var matches = labels.Where(x => x.Name == name).ToArray();
            if (matches.Length == 0 || matches.Any(x => x.Foreground is not SolidColorBrush brush || brush.Color != BrushColor("InkBrush")))
                throw new InvalidOperationException($"Card text is not using the readable foreground: {name}.");
        }
        if (!labels.Any(x => x.Name == "CardTypeLabel" && x.Text == "Files · 1" && x.Foreground is SolidColorBrush brush && brush.Color == BrushColor("MutedBrush")))
            throw new InvalidOperationException("The files label must include the file count.");
        var selected = Descendants<ListBoxItem>(root).Single(item => item.IsSelected);
        var ring = (Border)selected.Template.FindName("SelectionRing", selected);
        if (selected.Width != 250 || selected.Height != 214 || ring.RenderSize != selected.RenderSize || ring.IsHitTestVisible || ring.Visibility != Visibility.Visible)
            throw new InvalidOperationException("The selection ring must cover the card without affecting its layout.");
        if (highContrast)
        {
            var chip = (Button)ribbon.FindName("AllChip");
            if (((SolidColorBrush)chip.Background).Color != SystemColors.HighlightColor ||
                ((SolidColorBrush)chip.Foreground).Color != SystemColors.HighlightTextColor ||
                BrushColor("SurfaceBrush") != SystemColors.WindowColor || BrushColor("InkBrush") != SystemColors.WindowTextColor ||
                BrushColor("FocusBrush") != SystemColors.HighlightColor || BrushColor("HoverBrush") != SystemColors.WindowColor)
                throw new InvalidOperationException("High contrast must pair the Windows highlight with its foreground.");
            if (ClipCollection.Colors.Any(color => BrushColor(color) != SystemColors.WindowTextColor))
                throw new InvalidOperationException("High contrast collection dots must use the Windows text color.");
        }
        ribbon.AllowClose = true; ribbon.Close();
        var settings = new SettingsWindow(new AppSettings { Language = "en", Theme = "dark" });
        var settingsRoot = (FrameworkElement)settings.Content;
        Arrange(settingsRoot, 650, 739);
        var languages = (ComboBox)settings.FindName("LanguageCombo");
        var presenter = languages.Template.FindName("SelectionPresenter", languages) as ContentPresenter;
        var selectedLabels = presenter == null ? Array.Empty<TextBlock>() : Descendants<TextBlock>(presenter).ToArray();
        if (selectedLabels.Length == 0 || !selectedLabels.Any(x => x.Text == "English") || selectedLabels.Any(x => x.Text.Contains("LanguageOption", StringComparison.Ordinal)))
            throw new InvalidOperationException("The selected language must render its native name, English, instead of the record representation.");
        foreach (string name in new[] { "AccentCombo", "HotkeyCombo" })
        {
            var combo = (ComboBox)settings.FindName(name);
            var row = (FrameworkElement)VisualTreeHelper.GetParent(combo);
            var section = (FrameworkElement)VisualTreeHelper.GetParent(row);
            var comboBounds = Bounds(combo, section);
            if (combo.ActualWidth != 260 || comboBounds.Bottom > (LayoutInformation.GetLayoutClip(section)?.Bounds.Bottom ?? section.ActualHeight) + .01)
                throw new InvalidOperationException($"The entire settings dropdown must fit in its section: {name}.");
        }
        var excluded = (TextBox)settings.FindName("ExcludedAppsBox");
        if (excluded.GetFirstVisibleLineIndex() != 0 || excluded.GetLastVisibleLineIndex() != 2)
            throw new InvalidOperationException("Excluded apps must show three complete lines.");
        settings.Close();
    }

    private static Color BrushColor(string key) => ((SolidColorBrush)ThemeManager.GetBrush(key)).Color;

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement parent) => element.TransformToAncestor(parent).TransformBounds(new Rect(new Point(), element.RenderSize));

    private static void Render(FrameworkElement root, string path, int width, int height)
    {
        Arrange(root, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Arrange(FrameworkElement root, int width, int height)
    {
        root.ClearValue(FrameworkElement.WidthProperty); root.ClearValue(FrameworkElement.HeightProperty);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
    }

}
