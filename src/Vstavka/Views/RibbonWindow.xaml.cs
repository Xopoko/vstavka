using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Presentation;
using System.Windows.Markup;

namespace Vstavka.Views;

public partial class RibbonWindow : Window
{
    private List<ClipItem> _items = new();
    private readonly ObservableCollection<ClipCard> _visible = new();
    private readonly Dictionary<string, ClipCard> _cards = new();
    private readonly Func<string, ReadOnlyMemory<byte>>? _getSourceIcon;
    private readonly Dictionary<string, (ReadOnlyMemory<byte> Png, ImageSource? Image)> _sourceIcons = new(StringComparer.OrdinalIgnoreCase);
    private int _animationGeneration;
    private DispatcherOperation? _opening;
    private string _filter = "all";
    private bool _paused;
    private bool _dismissing;
    internal bool IsDismissing => _dismissing;
    public bool AllowClose { get; set; }
    public event Action<ClipItem, bool>? PasteRequested;
    public event Action<ClipItem>? CopyRequested;
    public event Action<ClipItem>? PinRequested;
    public event Action<ClipItem>? DeleteRequested;
    public event Action? SettingsRequested;
    public event Action? PauseRequested;

    public RibbonWindow() : this(null) { }

    public RibbonWindow(Func<string, ReadOnlyMemory<byte>>? getSourceIcon, HistoryStore? history = null)
    {
        _getSourceIcon = getSourceIcon;
        _history = history;
        ThemeManager.EnsureInitialized();
        InitializeComponent();
        UpdateLanguage();
        InitializeCardMenu();
        if (_history is not null) InitializeCollections();
        KeyboardHints.Children.Add(CreateKeyboardHint(Strings.Get("ChooseHint")));
        KeyboardHints.Children.Add(CreateKeyboardHint(Strings.Get("PasteHint")));
        ApplyFilter();
    }

    internal void UpdateLanguage()
    {
        FlowDirection = Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Language = XmlLanguage.GetLanguage(Strings.Culture.IetfLanguageTag);
        CategoryPrevious.Content = Strings.IsRightToLeft ? "\uE76C" : "\uE76B";
        CategoryNext.Content = Strings.IsRightToLeft ? "\uE76B" : "\uE76C";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; DismissDrawer(); }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ++_animationGeneration; _opening?.Abort(); _opening = null;
        DrawerOffset.BeginAnimation(TranslateTransform.YProperty, null);
        AnimatedSurface.BeginAnimation(UIElement.OpacityProperty, null);
        base.OnClosed(e);
    }

    public void SetItems(IEnumerable<ClipItem> items)
    {
        SyncCollectionChips();
        _items = items.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CapturedAt).ToList();
        var retained = new HashSet<string>();
        foreach (var item in _items)
        {
            retained.Add(item.Id);
            var icon = GetSourceIcon(item.SourceApp);
            if (_cards.TryGetValue(item.Id, out var card)) card.Refresh(item, icon);
            else _cards.Add(item.Id, new ClipCard(item, icon));
        }
        foreach (var id in _cards.Keys.Where(id => !retained.Contains(id)).ToArray()) _cards.Remove(id);
        foreach (var source in _sourceIcons.Keys)
            if (_getSourceIcon!(source).IsEmpty) _sourceIcons.Remove(source);
        ApplyFilter();
    }

    private ImageSource? GetSourceIcon(string source)
    {
        if (_getSourceIcon is null || string.IsNullOrWhiteSpace(source)) return null;
        var png = _getSourceIcon(source);
        if (png.IsEmpty) return null;
        if (_sourceIcons.TryGetValue(source, out var cached) && cached.Png.Equals(png)) return cached.Image;
        ImageSource? icon = null;
        try
        {
            using var stream = new MemoryStream(png.ToArray(), writable: false);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            icon = frame;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException) { }
        _sourceIcons[source] = (png, icon);
        return icon;
    }

    public void SetStatus(string status, bool paused)
    {
        bool pausedChanged = _paused != paused;
        _paused = paused;
        if (StatusLabel.Text != status)
        {
            StatusLabel.Text = status;
            StatusPanel.InvalidateMeasure();
        }
        StatusDot.Fill = ThemeManager.GetBrush(paused ? "WarningBrush" : "SuccessBrush");
        PauseButton.Content = paused ? "\uE768" : "\uE769";
        PauseButton.Tag = paused ? "selected" : null;
        PauseButton.ToolTip = Strings.Get(paused ? "Resume" : "Pause");
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, (string)PauseButton.ToolTip);
        if (pausedChanged && _items.Count == 0) ApplyFilter();
    }

    public void OpenDrawer()
    {
        if (IsVisible && !_dismissing) { Activate(); FocusSearch(); return; }
        double startOffset = IsVisible ? DrawerOffset.Y : 54;
        double startOpacity = IsVisible ? AnimatedSurface.Opacity : 0;
        int generation = ++_animationGeneration;
        _opening?.Abort();
        _dismissing = false;
        DrawerOffset.BeginAnimation(TranslateTransform.YProperty, null);
        AnimatedSurface.BeginAnimation(UIElement.OpacityProperty, null);
        var area = SystemParameters.WorkArea;
        Width = Math.Min(area.Width, Math.Max(560, area.Width - 28)); Height = Math.Min(338, area.Height - 24);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 9;
        bool animate = SystemParameters.ClientAreaAnimation;
        DrawerOffset.Y = animate ? startOffset : 0;
        AnimatedSurface.Opacity = animate ? startOpacity : 1;
        if (!IsVisible) Show();
        Activate();
        // Loaded runs after the initial layout/render work. The first visible frame must
        // already have its start transform; moving the HWND races its layered surface.
        _opening = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _opening = null;
            if (generation != _animationGeneration || !IsVisible || _dismissing) return;
            FocusSearch();
            if (!animate) return;
            DrawerOffset.Y = 0; AnimatedSurface.Opacity = 1;
            var slide = new DoubleAnimation(startOffset, 0, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            DrawerOffset.BeginAnimation(TranslateTransform.YProperty, slide);
            AnimatedSurface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(startOpacity, 1, TimeSpan.FromMilliseconds(170)) { FillBehavior = FillBehavior.Stop });
        }));
    }

    private void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); if (Cards.SelectedIndex < 0 && _visible.Count > 0) Cards.SelectedIndex = 0; }

    public void DismissDrawer()
    {
        if (!IsVisible || _dismissing) return;
        CollectionPopup.IsOpen = false;
        int generation = ++_animationGeneration;
        _opening?.Abort(); _opening = null;
        _dismissing = true;
        if (!SystemParameters.ClientAreaAnimation || AnimatedSurface.Opacity == 0) { FinishDismiss(); return; }
        var fade = new DoubleAnimation(AnimatedSurface.Opacity, 0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) => { if (_dismissing && generation == _animationGeneration) FinishDismiss(); };
        AnimatedSurface.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void FinishDismiss()
    {
        Hide();
        DrawerOffset.BeginAnimation(TranslateTransform.YProperty, null); DrawerOffset.Y = 0;
        AnimatedSurface.BeginAnimation(UIElement.OpacityProperty, null); AnimatedSurface.Opacity = 1;
        _dismissing = false;
    }

    private void ApplyFilter()
    {
        if (Cards == null) return;
        var selected = (Cards.SelectedItem as ClipCard)?.Item.Id;
        string query = SearchBox.Text.Trim();
        var scope = _items.Where(x => x.CollectionId == _collectionFilter);
        var source = scope.Where(x => _filter switch { "pinned" => x.IsPinned, "text" => x.Kind is ClipKind.Text or ClipKind.Color, "link" => x.Kind == ClipKind.Link, "image" => x.Kind == ClipKind.Image, "files" => x.Kind == ClipKind.Files, _ => true });
        if (query.Length > 0) source = source.Where(new ClipSearch(query).Matches);
        var next = source.Select(item => _cards[item.Id]).ToList();
        // Keep realized containers and thumbnails when reopening unchanged history.
        // Replace only changed slots for large searches; a single pin/add/delete uses
        // Move/Insert/Remove so neighboring cards keep their focus and visual state.
        int differences = Math.Abs(next.Count - _visible.Count);
        for (int i = 0; i < Math.Min(next.Count, _visible.Count); i++) if (!ReferenceEquals(next[i], _visible[i])) differences++;
        var nextSet = differences > 0 ? next.ToHashSet() : null;
        bool bulkReplacement = differences > 32 && _visible.Count(x => nextSet!.Contains(x)) < Math.Max(next.Count, _visible.Count) - 32;
        if (bulkReplacement)
        {
            for (int i = 0; i < Math.Min(next.Count, _visible.Count); i++) if (!ReferenceEquals(next[i], _visible[i])) _visible[i] = next[i];
            while (_visible.Count > next.Count) _visible.RemoveAt(_visible.Count - 1);
            for (int i = _visible.Count; i < next.Count; i++) _visible.Add(next[i]);
        }
        else if (differences > 0)
        {
            for (int i = _visible.Count - 1; i >= 0; i--) if (!nextSet!.Contains(_visible[i])) _visible.RemoveAt(i);
            for (int i = 0; i < next.Count; i++)
            {
                if (i < _visible.Count && ReferenceEquals(_visible[i], next[i])) continue;
                int previous = _visible.IndexOf(next[i]);
                if (previous >= 0) _visible.Move(previous, i); else _visible.Insert(i, next[i]);
            }
        }
        if (!ReferenceEquals(Cards.ItemsSource, _visible)) Cards.ItemsSource = _visible;
        var selectedCard = _visible.FirstOrDefault(x => Equals(x.Item.Id, selected));
        Cards.SelectedItem = selectedCard ?? _visible.FirstOrDefault();
        int scopeCount = scope.Count();
        CountLabel.Text = _visible.Count == scopeCount ? Strings.Format("ItemsCount", _visible.Count) : Strings.Format("FilteredCount", _visible.Count, scopeCount);
        EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (query.Length > 0) SetEmpty("\uE721", "NoResults", "NoResultsHint");
        else if (_collectionFilter is not null) SetEmpty("\uE8B7", "EmptyCollection", "EmptyCollectionHint");
        else if (_filter == "pinned") SetEmpty("\uE718", "EmptyPinned", "EmptyPinnedHint");
        else if (_items.Count > 0) SetEmpty("\uE8C8", "EmptyCategory", "EmptyCategoryHint");
        else if (_paused) SetEmpty("\uE769", "Paused", "PausedHint");
        else SetEmpty("\uE8C8", "EmptyHistory", "EmptyHistoryHint");
    }

    private void SetEmpty(string glyph, string title, string hint) { EmptyGlyph.Text = glyph; EmptyTitle.Text = Strings.Get(title); EmptyDescription.Text = Strings.Get(hint); }

    private static StackPanel CreateKeyboardHint(string text)
    {
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 17, 0) };
        int separator = text.IndexOf("  ", StringComparison.Ordinal);
        if (separator >= 0)
        {
            var keycap = new Border
            {
                FlowDirection = FlowDirection.LeftToRight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = text[..separator], FontSize = 12, FontWeight = FontWeights.SemiBold }
            };
            keycap.SetResourceReference(Border.BackgroundProperty, "KeycapBrush");
            keycap.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            var keySpacing = new Border { Child = keycap, Margin = new Thickness(0, 0, 5, 0) };
            hint.Children.Add(keySpacing);
        }
        var label = new TextBlock
        {
            Text = separator >= 0 ? text[(separator + 2)..] : text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        hint.Children.Add(label);
        return hint;
    }

    private void Layout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (KeyboardHints != null) KeyboardHints.Visibility = e.NewSize.Width < 930 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void CategoryPrevious_Click(object sender, RoutedEventArgs e) => CategoryScroller.ScrollToHorizontalOffset(CategoryScroller.HorizontalOffset - 170);
    private void CategoryNext_Click(object sender, RoutedEventArgs e) => CategoryScroller.ScrollToHorizontalOffset(CategoryScroller.HorizontalOffset + 170);
    private void Categories_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        CategoryScroller.ScrollToHorizontalOffset(CategoryScroller.HorizontalOffset - Math.Sign(e.Delta) * 110); e.Handled = true;
    }
    private void Categories_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (CategoryPrevious == null || CategoryNext == null) return;
        // Keep both arrows allocated once the full rail exceeds its available width; avoid visibility oscillation.
        bool overflow = Chips.ActualWidth > CategoriesRail.ActualWidth + .5;
        CategoryPrevious.Visibility = CategoryNext.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        CategoryPrevious.IsEnabled = CategoryScroller.HorizontalOffset > .5;
        CategoryNext.IsEnabled = CategoryScroller.HorizontalOffset < CategoryScroller.ScrollableWidth - .5;
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) SelectChip(button);
    }

    private void SelectChip(Button button)
    {
        _collectionFilter = (button.DataContext as ClipCollection)?.Id;
        _filter = _collectionFilter is null ? button.Uid : "all";
        foreach (var child in Chips.Children.OfType<Button>()) child.Tag = child == button ? "selected" : null;
        ApplyFilter();
        button.BringIntoView();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (CollectionPopup.IsOpen || _openContextMenu?.IsOpen == true) return;
        if (e.Key == Key.Escape) { DismissDrawer(); e.Handled = true; return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
            int index = key >= Key.D1 && key <= Key.D9 ? key - Key.D1 : key >= Key.NumPad1 && key <= Key.NumPad9 ? key - Key.NumPad1 : -1;
            if (index >= 0 && index < _visible.Count) { PasteRequested?.Invoke(_visible[index].Item, (Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; return; }
        }
        if (key == Key.Enter && Keyboard.FocusedElement is Button) return;
        if (key == Key.Enter && Cards.SelectedItem is ClipCard selected) { PasteRequested?.Invoke(selected.Item, (Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; return; }
        if (key == Key.Down && SearchBox.IsKeyboardFocusWithin) { FocusSelected(); e.Handled = true; return; }
        if (key is Key.Left or Key.Right && !SearchBox.IsKeyboardFocusWithin && !CategoriesRail.IsKeyboardFocusWithin)
        {
            int delta = (key == Key.Right ? 1 : -1) * (FlowDirection == FlowDirection.RightToLeft ? -1 : 1);
            int next = Math.Clamp(Cards.SelectedIndex + delta, 0, Math.Max(0, _visible.Count - 1));
            if (_visible.Count > 0) { Cards.SelectedIndex = next; FocusSelected(); }
            e.Handled = true; return;
        }
        if (key == Key.Delete && !SearchBox.IsKeyboardFocusWithin && Cards.SelectedItem is ClipCard clip) { DeleteRequested?.Invoke(clip.Item); e.Handled = true; }
    }

    private void FocusSelected()
    {
        if (Cards.SelectedItem is not ClipCard selected) return;
        Cards.ScrollIntoView(selected); Cards.UpdateLayout();
        (Cards.ItemContainerGenerator.ContainerFromItem(selected) as ListBoxItem)?.Focus();
    }
    private void Cards_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && Ancestor<Button>(source) != null) return;
        if (Cards.SelectedItem is ClipCard selected && e.OriginalSource is DependencyObject target && Ancestor<ListBoxItem>(target) != null) PasteRequested?.Invoke(selected.Item, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }
    private void Cards_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = Descendant<ScrollViewer>(Cards);
        if (scroller != null) { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - Math.Sign(e.Delta)); e.Handled = true; }
    }
    private static T? Ancestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null) { if (current is T found) return found; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current); } return null;
    }
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) return found; var nested = Descendant<T>(child); if (nested != null) return nested; } return null;
    }
    private static ClipItem? ItemFrom(object sender) => (sender as FrameworkElement)?.DataContext is ClipCard card ? card.Item : null;
    private void InitializeCardMenu()
    {
        var menu = (ContextMenu)FindResource("CardMenu");
        menu.Opened += CardMenu_Opened;
        menu.Closed += ContextMenu_Closed;
        var options = menu.Items.OfType<MenuItem>().ToDictionary(item => item.Uid);
        options["paste"].Click += PasteMenu_Click;
        options["paste-plain"].Click += PlainPasteMenu_Click;
        options["copy"].Click += CopyMenu_Click;
        options["pin"].Click += PinMenu_Click;
        options["delete"].Click += DeleteMenu_Click;
        options["collection-removal"].Click += RemoveFromCollection_Click;
    }
    private void Pin_Click(object sender, RoutedEventArgs e) { if (ItemFrom(sender) is { } item) PinRequested?.Invoke(item); e.Handled = true; }
    private void PasteMenu_Click(object sender, RoutedEventArgs e) { if (ItemFrom(sender) is { } item) PasteRequested?.Invoke(item, false); }
    private void PlainPasteMenu_Click(object sender, RoutedEventArgs e) { if (ItemFrom(sender) is { } item) PasteRequested?.Invoke(item, true); }
    private void CopyMenu_Click(object sender, RoutedEventArgs e) { if (ItemFrom(sender) is { } item) CopyRequested?.Invoke(item); }
    private void PinMenu_Click(object sender, RoutedEventArgs e) { if (ItemFrom(sender) is { } item) PinRequested?.Invoke(item); }
    private void DeleteMenu_Click(object sender, RoutedEventArgs e) { if (ItemFrom(sender) is { } item) DeleteRequested?.Invoke(item); }
    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => DismissDrawer();
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible && !CollectionPopup.IsOpen && _openContextMenu?.IsOpen != true) DismissDrawer();
    }
}
