using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Presentation;

namespace Vstavka.Views;

public partial class RibbonWindow
{
    private readonly HistoryStore? _history;
    private readonly Dictionary<string, Button> _collectionChips = new();
    private string? _collectionFilter;
    private string? _editCollectionId;
    private string? _pendingCollectionItemId;
    private string _collectionColor = ClipCollection.Colors[0];
    private FrameworkElement? _collectionPopupOpener;
    private ContextMenu? _openContextMenu;

    public event Action? CollectionsChanged;

    private void InitializeCollections()
    {
        CollectionDivider.Visibility = NewCollectionButton.Visibility = Visibility.Visible;
        foreach (var color in ClipCollection.Colors)
        {
            var dot = CollectionDot(color, 14);
            var ring = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Style = (Style)FindResource("FocusRing")
            };
            var content = new Grid();
            content.Children.Add(ring);
            content.Children.Add(dot);
            var swatch = new Button { Uid = color, Content = content, Style = (Style)FindResource("IconButton") };
            swatch.SetBinding(AutomationProperties.NameProperty, ColorName(color));
            swatch.SetBinding(ToolTipProperty, ColorName(color));
            swatch.Click += (_, _) => SelectCollectionColor(color);
            CollectionSwatches.Children.Add(swatch);
        }
        SyncCollectionChips();
    }

    private static System.Windows.Data.Binding ColorName(string color) => new($"[{color}]") { Source = Strings.Current };

    private void SyncCollectionChips()
    {
        if (_history is null) return;
        var current = _history.Collections.Select(collection => collection.Id).ToHashSet();
        foreach (var id in _collectionChips.Keys.Where(id => !current.Contains(id)).ToArray())
        {
            Chips.Children.Remove(_collectionChips[id]);
            _collectionChips.Remove(id);
        }

        foreach (var collection in _history.Collections)
        {
            if (!_collectionChips.TryGetValue(collection.Id, out var chip))
            {
                chip = new Button { Uid = collection.Id, Style = (Style)FindResource("ChipButton") };
                chip.Click += Filter_Click;
                var menu = new ContextMenu { Style = (Style)FindResource(typeof(ContextMenu)) };
                menu.Opened += CollectionMenu_Opened;
                menu.Closed += ContextMenu_Closed;
                chip.ContextMenu = menu;
                Chips.Children.Insert(Chips.Children.IndexOf(NewCollectionButton), chip);
                _collectionChips.Add(collection.Id, chip);
            }
            if (Equals(chip.DataContext, collection)) continue;
            chip.DataContext = collection;
            chip.Content = CollectionLabel(collection.Color, collection.Name, 132);
            chip.ToolTip = collection.Name;
            AutomationProperties.SetName(chip, collection.Name);
        }

        if (_collectionFilter is { } selected && !current.Contains(selected))
        {
            _collectionFilter = null;
            _filter = "all";
            AllChip.Tag = "selected";
        }
    }

    private static Ellipse CollectionDot(string color, double size)
    {
        var dot = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, color);
        return dot;
    }

    private static FrameworkElement CollectionLabel(string color, string name, double maxWidth = double.PositiveInfinity)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        // A transparent 12-DIP slot absorbs layout rounding at fractional DPI scales,
        // which otherwise clipped a pixel off the 8-DIP dot.
        var dot = CollectionDot(color, 8);
        dot.HorizontalAlignment = HorizontalAlignment.Center;
        layout.Children.Add(new Grid { Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Children = { dot } });
        var label = new TextBlock { Text = name, MaxWidth = maxWidth, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        layout.Children.Add(label);
        return layout;
    }

    private static MenuItem CollectionMenuOption(object header, Action onClick)
    {
        var option = new MenuItem { Header = header };
        option.Click += (_, _) => onClick();
        return option;
    }

    private void CollectionMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: Button { DataContext: ClipCollection collection } chip } menu)
            return;
        TrackMenu(menu);
        menu.Items.Clear();
        menu.Items.Add(CollectionMenuOption(Strings.Get("RenameCollection") + "…",
            () => OpenFromMenu(() => OpenCollectionEditor(chip, collection))));
        var colors = new MenuItem { Header = Strings.Get("Color") };
        foreach (var key in ClipCollection.Colors)
        {
            var name = Strings.Get(key);
            var option = CollectionMenuOption(CollectionLabel(key, name), () =>
            {
                if (_history!.SetCollectionColor(collection.Id, key)) RefreshCollections();
            });
            AutomationProperties.SetName(option, name);
            colors.Items.Add(option);
        }
        menu.Items.Add(colors);
        menu.Items.Add(CollectionMenuOption(Strings.Get("DeleteCollection") + "…",
            () => OpenFromMenu(() => OpenCollectionDelete(chip, collection))));
    }

    private void CardMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: FrameworkElement { DataContext: ClipCard card } anchor } menu)
            return;
        TrackMenu(menu);
        var assignment = menu.Items.OfType<MenuItem>().Single(option => option.Uid == "collection-assignment");
        var removal = menu.Items.OfType<MenuItem>().Single(option => option.Uid == "collection-removal");
        assignment.Visibility = _history is null ? Visibility.Collapsed : Visibility.Visible;
        removal.Visibility = _history is not null && card.Item.CollectionId is not null ? Visibility.Visible : Visibility.Collapsed;
        assignment.Items.Clear();
        if (_history is null) return;

        assignment.Header = Strings.Get(card.Item.CollectionId is null ? "AddToCollection" : "MoveToCollection");
        foreach (var collection in _history.Collections.Where(x => x.Id != card.Item.CollectionId))
        {
            var option = CollectionMenuOption(CollectionLabel(collection.Color, collection.Name),
                () => AssignCollection(card.Item, collection.Id));
            AutomationProperties.SetName(option, collection.Name);
            option.ToolTip = collection.Name;
            assignment.Items.Add(option);
        }
        assignment.Items.Add(CollectionMenuOption(Strings.Get("NewCollection") + "…",
            () => OpenFromMenu(() => OpenCollectionEditor(NewCollectionButton, itemId: card.Item.Id, opener: anchor))));
    }

    private void TrackMenu(ContextMenu menu)
    {
        menu.FlowDirection = FlowDirection;
        menu.Language = Language;
        _openContextMenu = menu;
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _openContextMenu)) _openContextMenu = null;
    }

    private void OpenFromMenu(Action open)
    {
        if (_openContextMenu is { } menu) menu.IsOpen = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, open);
    }

    private void RemoveFromCollection_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) AssignCollection(item, null);
    }

    private void AssignCollection(ClipItem item, string? id)
    {
        if (_history?.AssignCollection(item.Id, id) == true) RefreshCollections();
    }

    private void NewCollection_Click(object sender, RoutedEventArgs e) => OpenCollectionEditor(NewCollectionButton);

    internal void ConfigureCollectionEditor(ClipCollection? collection)
    {
        _editCollectionId = collection?.Id;
        CollectionEditor.Visibility = Visibility.Visible;
        CollectionDeleteMessage.Visibility = Visibility.Collapsed;
        CollectionNameBox.Text = collection?.Name ?? string.Empty;
        CollectionError.Visibility = Visibility.Collapsed;
        CollectionSubmit.Content = Strings.Get(collection is null ? "CreateCollection" : "Save");
        CollectionSubmit.Style = (Style)FindResource("PrimaryButton");
        SelectCollectionColor(collection?.Color ??
            ClipCollection.Colors.FirstOrDefault(key => _history!.Collections.All(x => x.Color != key)) ?? ClipCollection.Colors[0]);
    }

    private void OpenCollectionEditor(Button anchor, ClipCollection? collection = null, string? itemId = null, FrameworkElement? opener = null)
    {
        ConfigureCollectionEditor(collection);
        _pendingCollectionItemId = itemId;
        OpenCollectionPopup(anchor, opener ?? anchor);
    }

    private void OpenCollectionDelete(Button chip, ClipCollection collection)
    {
        _editCollectionId = collection.Id;
        _pendingCollectionItemId = null;
        CollectionEditor.Visibility = Visibility.Collapsed;
        CollectionDeleteMessage.Visibility = Visibility.Visible;
        CollectionDeleteMessage.Text = Strings.Format("DeleteCollectionConfirm", collection.Name,
            _history!.Items.Count(item => item.CollectionId == collection.Id));
        AutomationProperties.SetHelpText(CollectionCancel, CollectionDeleteMessage.Text);
        CollectionSubmit.Content = Strings.Get("Delete");
        CollectionSubmit.Style = (Style)FindResource("DangerFillButton");
        OpenCollectionPopup(chip, chip);
    }

    private void OpenCollectionPopup(Button anchor, FrameworkElement opener)
    {
        anchor.BringIntoView();
        UpdateLayout();
        _collectionPopupOpener = opener;
        CollectionPopup.PlacementTarget = anchor;
        CollectionPopup.IsOpen = true;
    }

    private void CollectionPopup_Opened(object? sender, EventArgs e)
    {
        if (CollectionEditor.Visibility == Visibility.Visible)
        {
            CollectionNameBox.Focus();
            CollectionNameBox.SelectAll();
        }
        else CollectionCancel.Focus();
    }

    private void CollectionPopup_Closed(object? sender, EventArgs e)
    {
        var opener = _collectionPopupOpener;
        var itemId = _pendingCollectionItemId;
        _collectionPopupOpener = null;
        _pendingCollectionItemId = null;
        CollectionPopup.PlacementTarget = null;
        CollectionCancel.ClearValue(AutomationProperties.HelpTextProperty);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsVisible || !IsActive || _dismissing || CollectionPopup.IsOpen) return;
            if (itemId is not null && _visible.FirstOrDefault(card => card.Item.Id == itemId) is { } card)
            {
                Cards.ScrollIntoView(card);
                Cards.UpdateLayout();
                (Cards.ItemContainerGenerator.ContainerFromItem(card) as ListBoxItem)?.Focus();
            }
            else if (itemId is null && opener?.IsVisible == true) opener.Focus();
            else AllChip.Focus();
        }));
    }

    private void SelectCollectionColor(string key)
    {
        _collectionColor = key;
        foreach (Button swatch in CollectionSwatches.Children)
        {
            var selected = swatch.Uid == key;
            ((Grid)swatch.Content).Children[0].Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            swatch.IsTabStop = selected;
        }
    }

    private void CollectionName_TextChanged(object sender, TextChangedEventArgs e)
    {
        CollectionNamePlaceholder.Visibility = CollectionNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CollectionError.Visibility = Visibility.Collapsed;
    }

    private void CollectionPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CollectionPopup.IsOpen = false;
            e.Handled = true;
            return;
        }
        var button = Ancestor<Button>(e.OriginalSource as DependencyObject);
        var swatchIndex = button is null ? -1 : CollectionSwatches.Children.IndexOf(button);
        if (e.Key == Key.Enter && (button is null || swatchIndex >= 0))
        {
            e.Handled = true;
            SubmitCollectionPopup();
            return;
        }
        if (swatchIndex < 0 || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        int delta = e.Key is Key.Right or Key.Down ? 1 : -1;
        if (FlowDirection == FlowDirection.RightToLeft && e.Key is Key.Left or Key.Right) delta = -delta;
        int count = CollectionSwatches.Children.Count;
        var next = (Button)CollectionSwatches.Children[(swatchIndex + delta + count) % count];
        SelectCollectionColor(next.Uid);
        next.Focus();
        e.Handled = true;
    }

    private void CollectionCancel_Click(object sender, RoutedEventArgs e) => CollectionPopup.IsOpen = false;
    private void CollectionSubmit_Click(object sender, RoutedEventArgs e) => SubmitCollectionPopup();

    private void SubmitCollectionPopup()
    {
        if (_history is null) return;
        if (CollectionEditor.Visibility != Visibility.Visible)
        {
            CollectionPopup.IsOpen = false;
            if (_history.DeleteCollection(_editCollectionId!))
            {
                SelectChip(AllChip);
                RefreshCollections();
            }
            return;
        }

        if (_editCollectionId is { } id)
        {
            var error = _history.RenameCollection(id, CollectionNameBox.Text);
            if (error != CollectionEditError.None) { ShowCollectionError(error); return; }
            _history.SetCollectionColor(id, _collectionColor);
            CollectionPopup.IsOpen = false;
            RefreshCollections();
            return;
        }

        var result = _history.CreateCollection(CollectionNameBox.Text, _collectionColor);
        if (result.Collection is not { } created) { ShowCollectionError(result.Error); return; }
        if (_pendingCollectionItemId is { } itemId) _history.AssignCollection(itemId, created.Id);
        CollectionPopup.IsOpen = false;
        RefreshCollections();
        SelectChip(_collectionChips[created.Id]);
    }

    private void ShowCollectionError(CollectionEditError error)
    {
        CollectionError.Text = error switch
        {
            CollectionEditError.EmptyName => Strings.Get("CollectionNameRequired"),
            CollectionEditError.NameTooLong => Strings.Format("CollectionNameTooLong", HistoryStore.MaxCollectionNameLength),
            CollectionEditError.Limit => Strings.Format("CollectionLimit", HistoryStore.MaxCollections),
            _ => Strings.Get("CollectionUpdateFailed")
        };
        CollectionError.Visibility = Visibility.Visible;
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(CollectionError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void RefreshCollections()
    {
        SetItems(_history!.Items);
        CollectionsChanged?.Invoke();
    }
}
