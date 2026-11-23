using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Desktop;

internal sealed partial class PanelWindow
{
    private readonly Grid _collectionOverlay = new()
    {
        Name = "CollectionDialog",
        IsVisible = false,
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
    };
    private Control? _collectionDialogOpener;
    private ComboBox? _collectionColorChooser;

    private Button NewCollectionButton()
    {
        Button? button = null;
        button = PanelVisuals.IconButton(Symbol.Add, Strings.Get("NewCollection"),
            () => ShowCollectionEditor(button!));
        button.Name = "NewCollectionButton";
        return button;
    }

    private ContextMenu CreateCollectionMenu(ClipCollection collection, Button chip)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection };
        menu.Items.Add(Option("RenameCollection", () => AfterMenu(() => ShowCollectionEditor(chip, collection))));
        var colors = new MenuItem { Header = Strings.Get("Color") };
        foreach (var color in ClipCollection.Colors)
            colors.Items.Add(CollectionOption(Strings.Get(color), color, () => AfterMenu(() =>
            {
                if (_history.Store.SetCollectionColor(collection.Id, color))
                    RefreshCollectionEdit(collection.Id, select: false);
            })));
        menu.Items.Add(colors);
        menu.Items.Add(Option("DeleteCollection", () => AfterMenu(() => ConfirmCollectionDelete(chip, collection))));
        TrackMenu(menu);
        return menu;
    }

    private void AddCardCollectionOptions(ContextMenu menu, ClipItem item, ClipCardView card)
    {
        var assignment = new MenuItem
        {
            Header = Strings.Get(item.CollectionId is null ? "AddToCollection" : "MoveToCollection"),
            Name = "AssignCollection"
        };
        foreach (var collection in _history.Store.Collections.Where(collection => collection.Id != item.CollectionId))
            assignment.Items.Add(CollectionOption(collection.Name, collection.Color,
                () => AfterMenu(() => AssignCollection(item, collection.Id))));
        assignment.Items.Add(Option("NewCollection", () => AfterMenu(() => ShowCollectionEditor(card, itemId: item.Id))));
        menu.Items.Add(assignment);
        if (item.CollectionId is not null)
        {
            var remove = Option("RemoveFromCollection", () => AfterMenu(() => AssignCollection(item, null)));
            remove.Name = "RemoveFromCollection";
            menu.Items.Add(remove);
        }
    }

    private static MenuItem CollectionOption(string label, string color, Action click)
    {
        var option = new MenuItem
        {
            Header = SetupVisuals.Horizontal(7, PanelVisuals.Dot(color), new TextBlock { Text = label })
        };
        AutomationProperties.SetName(option, label);
        option.Click += (_, _) => click();
        return option;
    }

    private void TrackMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => _openMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_openMenu, menu)) _openMenu = null; };
    }

    private void AfterMenu(Action action)
    {
        _openMenu?.Close();
        Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen)
                action();
        }, DispatcherPriority.Input);
    }

    private void AssignCollection(ClipItem item, string? collectionId)
    {
        if (!_history.Store.AssignCollection(item.Id, collectionId))
            return;
        _history.HistoryChanged();
        SelectCard(_cards.SelectedIndex);
    }

    private void ShowCollectionEditor(Control opener, ClipCollection? collection = null, string? itemId = null)
    {
        var name = new TextBox
        {
            Name = "CollectionName", Classes = { "panel-search", "setup-input" },
            Text = collection?.Name, PlaceholderText = Strings.Get("CollectionName")
        };
        AutomationProperties.SetName(name, Strings.Get("CollectionName"));
        var colors = ClipCollection.Colors;
        var selectedColor = collection?.Color ?? colors.FirstOrDefault(color =>
            _history.Store.Collections.All(current => current.Color != color)) ?? colors[0];
        var colorChooser = new ComboBox
        {
            Name = "CollectionColor", Classes = { "setup-select" }, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(colorChooser, Strings.Get("Color"));
        foreach (var color in colors)
        {
            var label = Strings.Get(color);
            var swatch = new ComboBoxItem
            {
                Content = SetupVisuals.Horizontal(7, PanelVisuals.Dot(color, 9), new TextBlock { Text = label })
            };
            AutomationProperties.SetName(swatch, label);
            colorChooser.Items.Add(swatch);
        }
        colorChooser.SelectedIndex = colors.ToList().IndexOf(selectedColor);
        var errorLabel = SetupVisuals.Text("", 12, "DangerBrush");
        errorLabel.Name = "CollectionError";
        errorLabel.IsVisible = false;
        AutomationProperties.SetLiveSetting(errorLabel, AutomationLiveSetting.Polite);
        name.TextChanged += (_, _) => errorLabel.IsVisible = false;

        void Save()
        {
            var color = colors[Math.Max(0, colorChooser.SelectedIndex)];
            var store = _history.Store;
            if (collection is not null)
            {
                var error = store.RenameCollection(collection.Id, name.Text);
                if (error == CollectionEditError.None)
                {
                    store.SetCollectionColor(collection.Id, color);
                    RefreshCollectionEdit(collection.Id, select: false);
                    return;
                }
                ShowCollectionError(errorLabel, error);
                return;
            }

            var result = store.CreateCollection(name.Text, color);
            if (result.Collection is not { } created)
            {
                ShowCollectionError(errorLabel, result.Error);
                return;
            }
            if (itemId is not null)
                store.AssignCollection(itemId, created.Id);
            RefreshCollectionEdit(created.Id, select: true);
        }

        name.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            Save();
        };
        var cancel = SetupVisuals.Button("Cancel", () => CloseCollectionDialog(), "ghost", "CancelCollectionEdit");
        var save = SetupVisuals.Button(collection is null ? "CreateCollection" : "Save", Save, "primary", "SaveCollection");
        var body = new StackPanel { Spacing = 8, Children = { name, colorChooser, errorLabel, DialogActions(cancel, save) } };
        ShowCollectionDialog(opener, collection is null ? "NewCollection" : "RenameCollection", body, name);
        _collectionColorChooser = colorChooser;
        name.SelectAll();
    }

    private void ConfirmCollectionDelete(Button chip, ClipCollection collection)
    {
        var count = _history.Store.Items.Count(item => item.CollectionId == collection.Id);
        var message = SetupVisuals.Text(Strings.Format("DeleteCollectionConfirm", collection.Name, count));
        var cancel = SetupVisuals.Button("Cancel", () => CloseCollectionDialog(), "ghost", "CancelCollectionEdit");
        var delete = SetupVisuals.Button("Delete", () =>
        {
            if (_history.Store.DeleteCollection(collection.Id))
                RefreshCollectionEdit(null, select: true);
            else
                CloseCollectionDialog();
        }, "danger-filled", "ConfirmCollectionDelete");
        var body = new StackPanel { Spacing = 10, Children = { message, DialogActions(cancel, delete) } };
        ShowCollectionDialog(chip, "DeleteCollection", body, cancel);
    }

    private static StackPanel DialogActions(Button cancel, Button submit)
    {
        var actions = SetupVisuals.Horizontal(8, cancel, submit);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        return actions;
    }

    private void ShowCollectionDialog(Control opener, string titleKey, Control body, Control focus)
    {
        var title = SetupVisuals.Text(Strings.Get(titleKey), 17, strong: true);
        var contents = new StackPanel { Spacing = 12, Children = { title, body } };
        var surface = SetupVisuals.Surface(contents, background: "WindowBrush");
        surface.Padding = new Thickness(16);
        surface.Margin = new Thickness(16);
        surface.Width = 330;
        surface.MaxWidth = Math.Max(1, Bounds.Width - 32);
        surface.HorizontalAlignment = HorizontalAlignment.Center;
        surface.VerticalAlignment = VerticalAlignment.Center;
        KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);
        _collectionDialogOpener = opener;
        _collectionOverlay.Children.Add(surface);
        _collectionOverlay.IsVisible = true;
        _drawer.IsEnabled = false;
        UpdateLayout();
        focus.Focus(NavigationMethod.Tab);
    }

    private bool HandleCollectionDialogKey(KeyEventArgs e)
    {
        if (!_collectionOverlay.IsVisible)
            return false;
        if (e.Key == Key.Escape && _collectionColorChooser?.IsDropDownOpen != true)
        {
            CloseCollectionDialog();
            e.Handled = true;
        }
        return true;
    }

    private void CloseCollectionDialog(bool restoreFocus = true)
    {
        if (!_collectionOverlay.IsVisible)
            return;
        var opener = _collectionDialogOpener;
        _collectionDialogOpener = null;
        _collectionColorChooser = null;
        _collectionOverlay.IsVisible = false;
        _collectionOverlay.Children.Clear();
        _drawer.IsEnabled = true;
        if (restoreFocus && opener?.Focus(NavigationMethod.Tab) != true)
            _filters[_activeFilter].Button.Focus(NavigationMethod.Tab);
    }

    private void RefreshCollectionEdit(string? selectedCollection, bool select)
    {
        CloseCollectionDialog(restoreFocus: false);
        _history.HistoryChanged();
        var index = selectedCollection is null ? 0 : _filters.FindIndex(filter => filter.Collection == selectedCollection);
        if (select)
            SelectFilter(Math.Max(0, index));
        var chip = _filters[Math.Max(0, index)].Button;
        chip.BringIntoView();
        chip.Focus(NavigationMethod.Tab);
    }

    private static void ShowCollectionError(TextBlock label, CollectionEditError error)
    {
        label.Text = error switch
        {
            CollectionEditError.EmptyName => Strings.Get("CollectionNameRequired"),
            CollectionEditError.NameTooLong => Strings.Format("CollectionNameTooLong", HistoryStore.MaxCollectionNameLength),
            CollectionEditError.Limit => Strings.Format("CollectionLimit", HistoryStore.MaxCollections),
            _ => Strings.Get("CollectionUpdateFailed")
        };
        label.IsVisible = true;
    }
}
