using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed class PanelWindow : Window
{
    private static readonly (string Label, ClipKind? Kind, bool Pinned)[] Filters =
    [
        ("All", null, false), ("Pinned", null, true), ("Text", ClipKind.Text, false),
        ("Links", ClipKind.Link, false), ("Images", ClipKind.Image, false), ("Files", ClipKind.Files, false)
    ];

    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly bool _animate;
    private readonly Border _drawer;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly List<(Button Button, ClipKind? Kind, bool Pinned, string? Collection)> _filters = [];
    private readonly ScrollViewer _chipScroller;
    private readonly Grid _categories;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly TextBox _search;
    private readonly ListBox _cards;
    private readonly StackPanel _empty;
    private readonly TextBlock _count;
    private readonly StackPanel _hints;
    private readonly Grid _status;
    private readonly Button _pause;
    private readonly ColumnDefinition _searchColumn = new(new GridLength(248));
    private IReadOnlyList<ClipCollection> _collections = [];
    private IReadOnlyList<ClipItem> _visible = [];
    private ContextMenu? _openMenu;
    private CancellationTokenSource? _motion;
    private int _activeFilter;
    private bool _dirty = true;
    private bool _attached;
    private bool _isShowing;
    private bool _dismissing;
    private bool _pasting;

    public bool IsOpen => IsVisible && !_dismissing;
    public event Action<string>? Feedback;

    public PanelWindow(IPlatformServices platform, DesktopHistory history, bool animate = true)
    {
        _platform = platform;
        _history = history;
        _animate = animate;

        Title = "Vstavka";
        Width = 1200;
        Height = 338;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        FlowDirection = Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        AutomationProperties.SetName(this, Strings.Get("History"));

        _chipScroller = new ScrollViewer
        {
            Content = _chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _previous = PanelVisuals.IconButton(Symbol.ChevronLeft, Strings.Get("PreviousCategories"), () => Scroll(_chipScroller, -160));
        _next = PanelVisuals.IconButton(Symbol.ChevronRight, Strings.Get("NextCategories"), () => Scroll(_chipScroller, 160));
        if (FlowDirection == FlowDirection.RightToLeft)
        {
            _previous.Content = PanelVisuals.Glyph(Symbol.ChevronRight, 17);
            _next.Content = PanelVisuals.Glyph(Symbol.ChevronLeft, 17);
        }
        _previous.IsVisible = _next.IsVisible = false;
        _categories = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 9, 0) };
        AddColumn(_categories, _previous, 0);
        AddColumn(_categories, _chipScroller, 1);
        AddColumn(_categories, _next, 2);
        _chipScroller.ScrollChanged += (_, _) => UpdateCategoryArrows();
        _categories.SizeChanged += (_, _) => UpdateCategoryArrows();
        _chipScroller.AddHandler(PointerWheelChangedEvent, (_, e) => ScrollWheel(_chipScroller, e), RoutingStrategies.Tunnel);

        _search = new TextBox
        {
            Name = "SearchBox", Classes = { "panel-search" }, PlaceholderText = Strings.Get("Search"),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
            InnerLeftContent = new Border { Margin = new Thickness(8, 0, 0, 0), Child = PanelVisuals.Glyph(Symbol.Search, 16, "MutedBrush") }
        };
        AutomationProperties.SetName(_search, Strings.Get("Search"));
        _search.TextChanged += (_, _) => ApplyFilter();
        _pause = PanelVisuals.IconButton(Symbol.Pause, Strings.Get("Pause"), history.TogglePause);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _pause,
                PanelVisuals.IconButton(Symbol.Settings, Strings.Get("Settings"), () => { }),
                PanelVisuals.IconButton(Symbol.Dismiss, Strings.Get("Close"), Dismiss)
            }
        };
        var toolbar = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), _searchColumn, new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(18, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center
        };
        AddColumn(toolbar, _categories, 0);
        AddColumn(toolbar, _search, 1);
        AddColumn(toolbar, actions, 2);

        _cards = new ListBox
        {
            Name = "Cards", Classes = { "cards" }, AutoScrollToSelectedItem = false,
            ItemTemplate = new FuncDataTemplate<ClipItem>((item, _) => item is null ? null : CreateCard(item))
        };
        AutomationProperties.SetName(_cards, Strings.Get("HistoryItems"));
        ScrollViewer.SetHorizontalScrollBarVisibility(_cards, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_cards, ScrollBarVisibility.Disabled);
        _cards.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_cards.Scroll is ScrollViewer viewer)
                ScrollWheel(viewer, e);
        }, RoutingStrategies.Tunnel);
        _cards.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is null &&
                source.FindAncestorOfType<ClipCardView>(includeSelf: true) is { } card)
                _ = PasteAsync(card.Item, plain: false);
        };
        _empty = new StackPanel
        {
            Name = "EmptyState", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12), Spacing = 6, MaxWidth = 520, IsHitTestVisible = false
        };
        var middle = new Grid { Margin = new Thickness(18, 0, 8, 0), Children = { _cards, _empty } };
        Grid.SetRow(middle, 1);

        _count = PanelVisuals.Label("", 12, "MutedBrush");
        _count.Name = "ItemCount";
        _hints = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center,
            Children = { PanelVisuals.Hint("ChooseHint"), PanelVisuals.Hint("PasteHint") }
        };
        _status = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 7 };
        var footer = new Grid
        {
            ColumnDefinitions = new("Auto,Auto,*"),
            ColumnSpacing = 18, Margin = new Thickness(20, 0, 18, 0)
        };
        AddColumn(footer, _count, 0);
        AddColumn(footer, _hints, 1);
        AddColumn(footer, _status, 2);
        Grid.SetRow(footer, 2);

        _drawer = new Border
        {
            CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(1), Margin = new Thickness(5),
            Child = new Grid { RowDefinitions = new("62,*,35"), Children = { toolbar, middle, footer } }
        };
        _drawer.Bind(Border.BackgroundProperty, _drawer.GetResourceObservable("WindowBrush"));
        _drawer.Bind(Border.BorderBrushProperty, _drawer.GetResourceObservable("LineBrush"));
        Content = new Grid { ClipToBounds = true, Children = { _drawer } };

        ActualThemeVariantChanged += (_, _) => PanelTheme.Apply(this);
        var settings = Application.Current?.PlatformSettings;
        if (settings is not null)
            settings.ColorValuesChanged += OnSystemColorsChanged;
        PanelTheme.Apply(this);
        SizeChanged += (_, e) =>
        {
            _searchColumn.Width = new GridLength(Math.Clamp(e.NewSize.Width * .23, 128, 248));
            _hints.IsVisible = e.NewSize.Width >= 890;
        };

        _history.Changed += OnHistoryChanged;
        Closed += (_, _) =>
        {
            StopMotion();
            _history.Changed -= OnHistoryChanged;
            if (settings is not null)
                settings.ColorValuesChanged -= OnSystemColorsChanged;
            if (_isShowing)
                NotifyHidden();
        };
        Activated += (_, _) => UpdateStatus();
        Opened += (_, _) => AttachNativePanel();
        Deactivated += (_, _) =>
        {
            if (_openMenu?.IsOpen != true)
                Dismiss();
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public void Open(bool positionOnScreen = true)
    {
        if (_pasting)
            return;
        if (IsOpen)
        {
            Activate();
            return;
        }

        StopMotion();
        _dismissing = false;
        _platform.Paste.CaptureTarget();
        if (positionOnScreen)
            PositionAtBottom();
        Refresh();
        AttachNativePanel();

        if (!_isShowing)
        {
            _platform.PanelWindow.Showing();
            _isShowing = true;
        }
        Show();
        Activate();
        SelectCard(Math.Max(0, _cards.SelectedIndex));
        if (_animate)
            _ = Animate(TimeSpan.FromMilliseconds(190), from: 0, to: 1, slide: true);
    }

    public void Dismiss() => _ = DismissAsync();

    private async Task DismissAsync()
    {
        if (!IsOpen)
            return;
        _dismissing = true;
        _openMenu?.Close();
        if (_animate && !await Animate(TimeSpan.FromMilliseconds(95), from: _drawer.Opacity, to: 0))
            return;

        Hide();
        NotifyHidden();
        _dismissing = false;
    }

    private void NotifyHidden()
    {
        _isShowing = false;
        _platform.PanelWindow.Hidden();
    }

    private void AttachNativePanel()
    {
        if (_attached || TryGetPlatformHandle() is not { Handle: not 0, HandleDescriptor: { } descriptor } handle)
            return;
        _platform.PanelWindow.Attach(handle.Handle, descriptor);
        _attached = true;
    }

    private async Task<bool> Animate(TimeSpan duration, double from, double to, bool slide = false)
    {
        StopMotion();
        using var cancellation = new CancellationTokenSource();
        _motion = cancellation;
        var start = new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } };
        var end = new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } };
        if (slide)
        {
            start.Setters.Add(new Setter(RenderTransformProperty, TransformOperations.Parse("translateY(16px)")));
            end.Setters.Add(new Setter(RenderTransformProperty, TransformOperations.Identity));
        }
        var animation = new Animation { Duration = duration, Easing = new CubicEaseOut(), Children = { start, end } };
        try
        {
            await animation.RunAsync(_drawer, cancellation.Token);
            return !cancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_motion, cancellation))
                _motion = null;
        }
    }

    private void StopMotion() => _motion?.Cancel();

    private void PositionAtBottom()
    {
        if (Screens.Primary is not { } screen)
            return;
        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        var margin = (int)Math.Round(12 * scale);
        Width = Math.Max(1, (work.Width - margin * 2) / scale);
        Height = Math.Clamp((work.Height - margin * 2) / scale, 1, 338);
        Position = new PixelPoint(work.X + margin, work.Bottom - margin - (int)Math.Round(Height * scale));
    }

    private void OnSystemColorsChanged(object? sender, Avalonia.Platform.PlatformColorValues e) =>
        Dispatcher.UIThread.Post(() => PanelTheme.Apply(this));

    private void OnHistoryChanged()
    {
        _dirty = true;
        if (IsOpen)
            Refresh();
    }

    private void Refresh()
    {
        if (_dirty)
        {
            if (_filters.Count == 0 || !_collections.SequenceEqual(_history.Store.Collections))
                BuildChips();
            ApplyFilter(rebuild: true);
            _dirty = false;
        }

        var pauseIcon = (FluentIcons.Avalonia.SymbolIcon)_pause.Content!;
        pauseIcon.Symbol = _history.Paused ? Symbol.Play : Symbol.Pause;
        PanelVisuals.NameButton(_pause, Strings.Get(_history.Paused ? "Resume" : "Pause"));
        UpdateStatus();
    }

    private void BuildChips()
    {
        var oldCollection = _filters.Count > 0 ? _filters[_activeFilter].Collection : null;
        _chips.Children.Clear();
        _filters.Clear();
        foreach (var (label, kind, pinned) in Filters)
            AddChip(label, Strings.Get(label), kind, pinned);
        _collections = _history.Store.Collections.ToArray();
        if (_collections.Count > 0)
        {
            var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
            divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("LineBrush"));
            _chips.Children.Add(divider);
        }
        foreach (var collection in _collections)
            AddChip("Collection", collection.Name, collection: collection);

        if (oldCollection is not null)
            _activeFilter = Math.Max(0, _filters.FindIndex(filter => filter.Collection == oldCollection));
        _filters[_activeFilter].Button.Classes.Add("active");
    }

    private void AddChip(string name, string label, ClipKind? kind = null, bool pinned = false, ClipCollection? collection = null)
    {
        var button = new Button { Name = name + "Chip", Classes = { "panel-button", "chip" } };
        button.Content = collection is null ? label : new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { PanelVisuals.Dot(collection.Color), new TextBlock { Text = label, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis } }
        };
        PanelVisuals.NameButton(button, label);
        var index = _filters.Count;
        button.Click += (_, _) =>
        {
            _filters[_activeFilter].Button.Classes.Remove("active");
            _activeFilter = index;
            button.Classes.Add("active");
            ApplyFilter(resetScroll: true);
        };
        _filters.Add((button, kind, pinned, collection?.Id));
        _chips.Children.Add(button);
    }

    private void ApplyFilter(bool rebuild = false, bool resetScroll = false)
    {
        if (_filters.Count == 0)
            return;

        var (_, kind, pinned, collection) = _filters[_activeFilter];
        var query = _search.Text?.Trim() ?? "";
        var store = _history.Store;
        var matches = store.Search(query, kind == ClipKind.Text ? null : kind, pinned, collection);
        if (kind == ClipKind.Text)
            matches = matches.Where(item => item.Kind is ClipKind.Text or ClipKind.Color).ToArray();

        var previous = (_cards.SelectedItem as ClipItem)?.Id;
        var focusCards = _cards.IsKeyboardFocusWithin;
        if (rebuild || !_visible.SequenceEqual(matches))
        {
            _visible = matches;
            _cards.ItemsSource = matches;
        }
        var index = Math.Max(0, matches.ToList().FindIndex(item => item.Id == previous));
        _cards.SelectedIndex = matches.Count > 0 ? index : -1;
        if (resetScroll && _cards.Scroll is ScrollViewer scroller)
            scroller.Offset = default;
        if (focusCards)
            SelectCard(index);

        var total = store.Items.Count(item => item.CollectionId == collection);
        _count.Text = total == matches.Count ? Strings.Format("ItemsCount", total) : Strings.Format("FilteredCount", matches.Count, total);
        _empty.IsVisible = matches.Count == 0;
        if (_empty.IsVisible)
        {
            var (icon, title, hint) = query.Length > 0 ? (Symbol.Search, "NoResults", "NoResultsHint")
                : collection is not null ? (Symbol.Folder, "EmptyCollection", null)
                : pinned ? (Symbol.Pin, "EmptyPinned", "EmptyPinnedHint")
                : total > 0 ? (Symbol.Clipboard, "EmptyCategory", "EmptyCategoryHint")
                : _history.Paused ? (Symbol.Pause, "Paused", "PausedHint")
                : (Symbol.Clipboard, "EmptyHistory", "EmptyHistoryHint");
            ShowEmpty(icon, title, hint);
        }
    }

    private void ShowEmpty(Symbol icon, string title, string? hint)
    {
        var heading = PanelVisuals.Label(Strings.Get(title), 20);
        heading.FontWeight = FontWeight.SemiBold;
        heading.TextAlignment = TextAlignment.Center;
        heading.TextWrapping = TextWrapping.Wrap;
        _empty.Children.Clear();
        _empty.Children.Add(PanelVisuals.Glyph(icon, 28, "MutedBrush"));
        _empty.Children.Add(heading);
        if (hint is not null)
        {
            var description = PanelVisuals.Label(Strings.Get(hint), 14, "MutedBrush");
            description.TextAlignment = TextAlignment.Center;
            description.TextWrapping = TextWrapping.Wrap;
            _empty.Children.Add(description);
        }
    }

    private void UpdateStatus()
    {
        var notice = _platform.Notices.FirstOrDefault();
        var missingPermission = _platform.Paste.Permission == PermissionState.Missing;
        var warning = notice is not null || missingPermission || _history.Warning is not null || _history.Paused;
        var text = notice is not null ? DesktopStrings.Notice(notice)
            : _history.Warning ?? (_history.Paused ? Strings.Get("Paused")
                : missingPermission ? DesktopStrings.Get("Permission")
                : Strings.Get(_platform is DemoPlatformServices ? "DemoStatus" : "LocalHistory"));
        var label = PanelVisuals.Label(text, 12, "MutedBrush");
        label.Name = "PlatformStatus";
        ToolTip.SetTip(label, text);
        _status.Children.Clear();
        _status.HorizontalAlignment = warning ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        AddColumn(_status, PanelVisuals.Dot(warning ? "WarningBrush" : "SuccessBrush"), 0);
        AddColumn(_status, label, 1);
        if (missingPermission)
        {
            var grant = new Button { Content = DesktopStrings.Get("Grant"), Classes = { "panel-button", "permission" }, VerticalAlignment = VerticalAlignment.Center };
            grant.Click += (_, _) => _platform.Paste.RequestPermission();
            AddColumn(_status, grant, 2);
        }
    }

    private void UpdateCategoryArrows()
    {
        var overflow = _chipScroller.Extent.Width > _categories.Bounds.Width + .5;
        _previous.IsVisible = _next.IsVisible = overflow;
        _previous.IsEnabled = _chipScroller.Offset.X > .5;
        _next.IsEnabled = _chipScroller.Offset.X < _chipScroller.ScrollBarMaximum.X - .5;
    }

    private void ScrollWheel(ScrollViewer viewer, PointerWheelEventArgs e)
    {
        if (e.Delta.X != 0 || e.Delta.Y == 0 || viewer.ScrollBarMaximum.X <= 0)
            return;
        Scroll(viewer, -Math.Sign(e.Delta.Y) * 110);
        e.Handled = true;
    }

    private static void Scroll(ScrollViewer viewer, double delta) =>
        viewer.Offset = new Vector(Math.Clamp(viewer.Offset.X + delta, 0, viewer.ScrollBarMaximum.X), 0);

    private void SelectCard(int index)
    {
        if (index < 0 || index >= _visible.Count)
        {
            _cards.Focus(NavigationMethod.Directional);
            return;
        }
        _cards.SelectedIndex = index;
        _cards.ScrollIntoView(index);
        _cards.UpdateLayout();
        if (_cards.ContainerFromIndex(index) is ListBoxItem container)
            container.Focus(NavigationMethod.Directional);
        else
            _cards.Focus(NavigationMethod.Directional);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_openMenu?.IsOpen == true)
            return;

        var plain = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var searchFocused = _search.IsKeyboardFocusWithin;
        var selected = _cards.SelectedItem as ClipItem;
        var focused = FocusManager?.GetFocusedElement();
        var index = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;

        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                break;
            case Key.F when control:
                _search.Focus(NavigationMethod.Tab);
                _search.SelectAll();
                break;
            case Key.Down when searchFocused:
                SelectCard(_cards.SelectedIndex);
                break;
            case Key.Left or Key.Right when !searchFocused && !_categories.IsKeyboardFocusWithin:
                var step = (e.Key == Key.Right ? 1 : -1) * (FlowDirection == FlowDirection.RightToLeft ? -1 : 1);
                SelectCard(Math.Clamp(_cards.SelectedIndex + step, 0, Math.Max(0, _visible.Count - 1)));
                break;
            case Key.Enter when focused is not Button && selected is not null:
                _ = PasteAsync(selected, plain);
                break;
            case Key.Delete when !searchFocused && selected is not null:
                Delete(selected);
                break;
            default:
                if (!control || index < 0 || index >= _visible.Count)
                    return;
                _ = PasteAsync(_visible[index], plain);
                break;
        }
        e.Handled = true;
    }

    private ClipCardView CreateCard(ClipItem item)
    {
        var card = new ClipCardView(item, _history.Store, () => TogglePin(item));
        var menu = new ContextMenu { FlowDirection = FlowDirection };
        menu.Items.Add(Option("Paste", () => _ = PasteAsync(item, false), new KeyGesture(Key.Enter)));
        menu.Items.Add(Option("PastePlain", () => _ = PasteAsync(item, true), new KeyGesture(Key.Enter, KeyModifiers.Shift)));
        menu.Items.Add(Option("Copy", () =>
        {
            if (TryWrite(item, plain: false))
            {
                Dismiss();
                Feedback?.Invoke(DesktopStrings.Get("ManualPaste"));
            }
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Option(item.IsPinned ? "Unpin" : "Pin", () => TogglePin(item)));
        menu.Items.Add(Option("Delete", () => Delete(item), new KeyGesture(Key.Delete)));
        menu.Opened += (_, _) => _openMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_openMenu, menu)) _openMenu = null; };
        card.ContextMenu = menu;
        return card;
    }

    private static MenuItem Option(string key, Action click, KeyGesture? gesture = null)
    {
        var option = new MenuItem { Header = Strings.Get(key), InputGesture = gesture };
        option.Click += (_, _) => click();
        return option;
    }

    private void TogglePin(ClipItem item)
    {
        _history.Store.TogglePin(item.Id);
        _history.HistoryChanged();
    }

    private void Delete(ClipItem item)
    {
        _history.Store.Remove(item.Id);
        _history.HistoryChanged();
    }

    private bool TryWrite(ClipItem item, bool plain)
    {
        try
        {
            _platform.ClipboardWriter.Write(item, plain);
            _platform.Clipboard.SuppressNextChange();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            Feedback?.Invoke(Strings.Get("ClipboardWriteFailed"));
            return false;
        }
    }

    private async Task PasteAsync(ClipItem item, bool plain)
    {
        if (_pasting || !TryWrite(item, plain))
            return;
        _pasting = true;
        try
        {
            await DismissAsync();
            if (!await _platform.Paste.PasteAsync())
                Feedback?.Invoke(DesktopStrings.Get("ManualPaste"));
        }
        finally
        {
            _pasting = false;
        }
    }

    private static void AddColumn(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
