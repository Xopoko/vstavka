using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Views;

namespace Vstavka.Presentation;

internal static partial class UiPreview
{
    internal static bool VerifyCollections()
    {
        var language = Strings.Language;
        Strings.SetLanguage("en");
        var history = new HistoryStore(Path.GetTempPath());
        var prompts = history.CreateCollection("Prompts", "CollectionBlue").Collection!;
        var work = history.CreateCollection("Work", "CollectionTeal").Collection!;
        var note = Add("Synthetic note");
        var pinned = Add("Synthetic pin", true);
        var prompt = Add("Synthetic prompt", true);
        var secondPrompt = Add("Another synthetic prompt");
        history.AssignCollection(prompt.Id, prompts.Id);
        history.AssignCollection(secondPrompt.Id, prompts.Id);
        var ribbon = new CollectionProbe(history)
        {
            Left = -24000, Top = -24000, Width = 1050, Topmost = false, ShowActivated = false
        };
        var cards = (ListBox)ribbon.FindName("Cards");
        var search = (TextBox)ribbon.FindName("SearchBox");
        var chips = (StackPanel)ribbon.FindName("Chips");
        var popup = (Popup)ribbon.FindName("CollectionPopup");
        var name = (TextBox)ribbon.FindName("CollectionNameBox");
        var plus = (Button)ribbon.FindName("NewCollectionButton");
        var submit = (Button)ribbon.FindName("CollectionSubmit");
        int pasteCalls = 0;
        ribbon.PasteRequested += (_, _) => pasteCalls++;

        try
        {
            ribbon.SetItems(history.Items);
            ribbon.Show();
            Layout();
            Expect(VisibleItems().Length == 2 && VisibleItems().All(item => item.CollectionId is null), "All excludes collection items.");
            SelectChip(ribbon, "pinned");
            Expect(VisibleItems().SequenceEqual(new[] { pinned }), "Pinned excludes collected pins.");
            SelectChip(ribbon, prompts.Id);
            Expect(VisibleItems().SequenceEqual(new[] { prompt, secondPrompt }), "The collection shows its pins first.");
            search.Text = "Another";
            Expect(VisibleItems().SequenceEqual(new[] { secondPrompt }), "Search is scoped to the active collection.");
            search.Text = "";
            SelectChip(ribbon, work.Id);
            Expect(VisibleItems().Length == 0 && ((TextBlock)ribbon.FindName("EmptyTitle")).Text == Strings.Get("EmptyCollection"), "An empty collection displays collection-specific guidance.");

            SelectChip(ribbon, "all");
            Click(plus);
            Layout();
            Expect(popup.IsOpen && ReferenceEquals(popup.PlacementTarget, plus), "Plus opens the inline editor below its button.");
            ribbon.RaiseDeactivated();
            Expect(ribbon.IsVisible && popup.IsOpen && !ribbon.IsDismissing, "Deactivation while editing leaves the ribbon and popup open.");
            SendKey(name, Key.Enter);
            Expect(((TextBlock)ribbon.FindName("CollectionError")).IsVisible && submit.IsEnabled, "An empty name shows an error and keeps Create enabled.");
            name.Text = "  Reading  ";
            SendKey(name, Key.Enter);
            Layout();
            var reading = history.Collections.Single(c => c.Name == "Reading");
            Expect(!popup.IsOpen && pasteCalls == 0 && Equals(Chip(reading.Id).Tag, "selected") && reading.Color == "CollectionGreen",
                "Enter on the name box creates and selects the collection with the first unused color and does not paste.");
            Click(plus);
            Layout();
            var swatches = ((Panel)ribbon.FindName("CollectionSwatches")).Children.OfType<Button>().ToArray();
            var selectedSwatch = swatches.Single(swatch => swatch.IsTabStop);
            var nextSwatch = swatches[(Array.IndexOf(swatches, selectedSwatch) + 1) % swatches.Length];
            SendKey(selectedSwatch, Key.Right);
            Expect(swatches.Length == 8 && swatches.All(swatch => AutomationProperties.GetName(swatch) == Strings.Get(swatch.Uid)) &&
                ReferenceEquals(swatches.Single(swatch => swatch.IsTabStop), nextSwatch), "Color swatches have localized names and respond to arrows.");
            SendKey(name, Key.Escape);
            Layout();
            Expect(!popup.IsOpen && ribbon.IsVisible && !ribbon.IsDismissing, "Escape dismisses only the popup.");

            SelectChip(ribbon, "all");
            var menu = OpenCardMenu(note);
            var assignment = Assignment(menu);
            Expect(Equals(assignment.Header, Strings.Get("AddToCollection")), "General history offers Add to collection.");
            assignment.IsSubmenuOpen = true;
            Layout();
            Expect(assignment.Template.FindName("PART_Popup", assignment) is Popup { IsOpen: true }, "Collection destinations open as a real WPF submenu.");
            Choose(menu, Option(assignment, prompts.Name));
            Expect(note.CollectionId == prompts.Id && !VisibleItems().Contains(note), "Adding moves the card out of All.");
            SelectChip(ribbon, prompts.Id);
            menu = OpenCardMenu(note);
            assignment = Assignment(menu);
            Expect(Equals(assignment.Header, Strings.Get("MoveToCollection")) && !assignment.Items.OfType<MenuItem>().Any(m => AutomationProperties.GetName(m) == prompts.Name),
                "Move offers other collections.");
            Choose(menu, Option(assignment, work.Name));
            SelectChip(ribbon, work.Id);
            menu = OpenCardMenu(note);
            Choose(menu, menu.Items.OfType<MenuItem>().Single(m => m.Uid == "collection-removal"));
            Expect(note.CollectionId is null && VisibleItems().Length == 0, "Removing the card leaves the collection empty.");

            SelectChip(ribbon, "all");
            menu = OpenCardMenu(note);
            Choose(menu, Assignment(menu).Items.OfType<MenuItem>().Last());
            Layout();
            Expect(popup.IsOpen, "New collection from a card opens the same editor.");
            name.Text = "From a card";
            SendKey(name, Key.Enter);
            Layout();
            var created = history.Collections.Single(c => c.Name == "From a card");
            Expect(VisibleItems().SequenceEqual(new[] { note }) && note.CollectionId == created.Id && pasteCalls == 0, "Creating from a card assigns it immediately.");

            menu = OpenMenu(Chip(created.Id));
            Choose(menu, (MenuItem)menu.Items[0]);
            Layout();
            Expect(name.Text == created.Name, "Rename starts with the current name.");
            name.Text = "Renamed collection";
            SendKey(name, Key.Enter);
            Layout();
            Expect(AutomationProperties.GetName(Chip(created.Id)) == "Renamed collection", "Rename updates the existing chip.");

            menu = OpenMenu(Chip(created.Id));
            var colors = (MenuItem)menu.Items[1];
            Choose(menu, Option(colors, Strings.Get("CollectionRose")));
            Expect(history.Collections.Single(c => c.Id == created.Id).Color == "CollectionRose", "The color submenu updates the chip color.");

            menu = OpenMenu(Chip(created.Id));
            Choose(menu, (MenuItem)menu.Items[2]);
            Layout();
            var message = (TextBlock)ribbon.FindName("CollectionDeleteMessage");
            Expect(popup.IsOpen && message.Text == Strings.Format("DeleteCollectionConfirm", "Renamed collection", 1) &&
                history.Items.Contains(note), "Delete shows the collection and item count before changing history.");
            Click(submit);
            Layout();
            Expect(history.Collections.All(c => c.Id != created.Id) && !history.Items.Contains(note) && Equals(Chip("all").Tag, "selected"),
                "Confirming deletes the collection and its items and selects All.");

            for (int i = 0; i < 7; i++) history.CreateCollection($"Collection {i}", "CollectionViolet");
            ribbon.SetItems(history.Items);
            VerifyScroll("en", rtl: false);
            VerifyScroll("ar", rtl: true);
            return true;
        }
        finally
        {
            popup.IsOpen = false;
            ribbon.AllowClose = true;
            ribbon.Close();
            Strings.SetLanguage(language);
        }

        ClipItem Add(string text, bool pinned = false)
        {
            var item = new ClipItem { Text = text, IsPinned = pinned };
            history.Add(item);
            return item;
        }

        static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        Button Chip(string id) => chips.Children.OfType<Button>().Single(b => b.Uid == id);
        ClipItem[] VisibleItems() => cards.Items.OfType<ClipCard>().Select(card => card.Item).ToArray();
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static void SendKey(UIElement target, Key key)
        {
            var inputSource = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("The popup has no WPF input source.");
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        void Layout()
        {
            ribbon.UpdateLayout();
            ribbon.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }
        ContextMenu OpenMenu(FrameworkElement anchor)
        {
            var menu = anchor.ContextMenu;
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
            Layout();
            return menu;
        }
        ContextMenu OpenCardMenu(ClipItem item)
        {
            var card = cards.Items.OfType<ClipCard>().Single(value => value.Item == item);
            cards.ScrollIntoView(card);
            Layout();
            return OpenMenu((ListBoxItem)cards.ItemContainerGenerator.ContainerFromItem(card));
        }
        static MenuItem Assignment(ContextMenu menu) => menu.Items.OfType<MenuItem>().Single(m => m.Uid == "collection-assignment");
        static MenuItem Option(MenuItem parent, string label) => parent.Items.OfType<MenuItem>().Single(m => AutomationProperties.GetName(m) == label);
        void Choose(ContextMenu menu, MenuItem item)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            Layout();
        }
        void VerifyScroll(string code, bool rtl)
        {
            Strings.SetLanguage(code);
            ribbon.UpdateLanguage();
            var scroller = (ScrollViewer)ribbon.FindName("CategoryScroller");
            scroller.ScrollToLeftEnd();
            Layout();
            var previous = (Button)ribbon.FindName("CategoryPrevious");
            var next = (Button)ribbon.FindName("CategoryNext");
            Expect(scroller.ScrollableWidth > 0 && previous.IsVisible && !previous.IsEnabled && next.IsEnabled &&
                Equals(next.Content, rtl ? "\uE76B" : "\uE76C"), "Scroll arrows reflect the start of the collection rail.");
            var last = Chip(history.Collections[^1].Id);
            var before = last.PointToScreen(new Point()).X;
            Click(next);
            Layout();
            var after = last.PointToScreen(new Point()).X;
            Expect(previous.IsEnabled && (rtl ? after > before : after < before), $"Next scrolls toward later collections in {code} ({before} → {after}).");
            Click(previous);
            Layout();
            Expect(!previous.IsEnabled && Math.Abs(last.PointToScreen(new Point()).X - before) < 1,
                "Previous returns to the start of the collection rail.");
        }
    }

    private sealed class CollectionProbe(HistoryStore history) : RibbonWindow(null, history)
    {
        internal void RaiseDeactivated() => OnDeactivated(EventArgs.Empty);
    }
}
