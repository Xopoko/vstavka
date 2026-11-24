using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;
using Vstavka.Platform.MacOS;

namespace Vstavka.Desktop;

internal sealed partial class PanelWindow : Window
{
    private static readonly (string Label, ClipKind? Kind, bool Pinned)[] Filters =
    [
        ("All", null, false), ("Pinned", null, true), ("Text", ClipKind.Text, false),
        ("Links", ClipKind.Link, false), ("Images", ClipKind.Image, false), ("Files", ClipKind.Files, false)
    ];
    private static readonly TransformOperations Lowered = TransformOperations.Parse("translateY(8px)");

    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly bool? _animate;
    private readonly Border _drawer;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = Space.Xs };
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
    private readonly DoubleTransition _fade = new() { Property = OpacityProperty };
    private readonly TransformOperationsTransition _rise = new() { Property = RenderTransformProperty };
    private readonly Transitions _drawerMotion;
    private CancellationTokenSource? _motion;
    private CancellationTokenSource? _reflow;
    private long _reflowedAt;
    private int _activeFilter;
    private bool _dirty = true;
    private bool _attached;
    private bool _isShowing;
    private bool _dismissing;
    private bool _pasting;

    private bool ClipboardOnly => _platform is MacPlatformServices { IsAppStoreBuild: true };
    private bool Animated => _animate ?? DesktopMotion.Enabled;
    public bool IsOpen => IsVisible && !_dismissing;
    public event Action<string>? Feedback;
    public event Action? SettingsRequested;
    public Func<OnboardingWindow?>? PracticeTarget { get; set; }

    /// <param name="animate">Null follows the system setting for animations; tests fix it either way.</param>
    public PanelWindow(IPlatformServices platform, DesktopHistory history, bool? animate = null)
    {
        _platform = platform;
        _history = history;
        _animate = animate;
        _drawerMotion = [_fade, _rise];

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
            _previous.Content = PanelVisuals.Glyph(Symbol.ChevronRight, IconSize.Control);
            _next.Content = PanelVisuals.Glyph(Symbol.ChevronLeft, IconSize.Control);
        }
        _previous.IsVisible = _next.IsVisible = false;
        _categories = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, Space.M, 0) };
        AddColumn(_categories, _previous, 0);
        AddColumn(_categories, _chipScroller, 1);
        AddColumn(_categories, _next, 2);
        _chipScroller.ScrollChanged += (_, _) => UpdateCategoryArrows();
        _categories.SizeChanged += (_, _) => UpdateCategoryArrows();
        _chipScroller.AddHandler(PointerWheelChangedEvent, (_, e) => ScrollWheel(_chipScroller, e), RoutingStrategies.Tunnel);

        _search = new TextBox
        {
            Name = "SearchBox", Classes = { "panel-search" }, PlaceholderText = Strings.Get("Search"),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = TextSize.Body,
            Margin = new Thickness(0, 0, Space.M, 0),
            InnerLeftContent = new Border
            {
                Margin = new Thickness(Space.M, 0, 0, 0), Child = PanelVisuals.Glyph(Symbol.Search, IconSize.Control, "MutedBrush")
            }
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
                PanelVisuals.IconButton(Symbol.Settings, Strings.Get("Settings"), () => _ = OpenSettingsAsync()),
                PanelVisuals.IconButton(Symbol.Dismiss, Strings.Get("Close"), Dismiss)
            }
        };
        var toolbar = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), _searchColumn, new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(Space.Xl, 0, Space.L, 0), VerticalAlignment = VerticalAlignment.Center
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
        // A recycled card would carry its slide over to another item.
        _cards.ContainerClearing += (_, _) => _reflow?.Cancel();
        _cards.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_cards.Scroll is ScrollViewer viewer)
                ScrollWheel(viewer, e);
        }, RoutingStrategies.Tunnel);
        _cards.ContextRequested += (sender, e) =>
        {
            // A keyboard context request starts on the list item, above the card visual.
            if (!e.TryGetPosition(_cards, out _) && _cards.SelectedIndex >= 0 &&
                _cards.ContainerFromIndex(_cards.SelectedIndex)?.FindDescendantOfType<ClipCardView>() is { ContextMenu: { } menu } card)
            {
                menu.Open(card);
                e.Handled = true;
            }
        };
        _cards.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is null &&
                source.FindAncestorOfType<ClipCardView>(includeSelf: true) is { } card)
                _ = PasteAsync(card.Item, plain: false);
        };
        _empty = new StackPanel
        {
            Name = "EmptyState", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(Space.L), Spacing = Space.S, MaxWidth = 520, IsHitTestVisible = false
        };
        var middle = new Grid { Margin = new Thickness(Space.Xl, 0, Space.M, 0), Children = { _cards, _empty } };
        Grid.SetRow(middle, 1);

        _count = PanelVisuals.Label("", TextSize.Caption, "MutedBrush");
        _count.Name = "ItemCount";
        _hints = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = Space.Xl, VerticalAlignment = VerticalAlignment.Center,
            Children = { PanelVisuals.Hint("ChooseHint"), PanelVisuals.Hint("PasteHint", ClipboardOnly ? "Copy" : null) }
        };
        _status = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = Space.S };
        var footer = new Grid
        {
            ColumnDefinitions = new("Auto,Auto,*"),
            ColumnSpacing = Space.Xl, Margin = new Thickness(Space.Xl, 0)
        };
        AddColumn(footer, _count, 0);
        AddColumn(footer, _hints, 1);
        AddColumn(footer, _status, 2);
        Grid.SetRow(footer, 2);

        _drawer = new Border
        {
            CornerRadius = Radius.Panel, BorderThickness = new Thickness(Stroke.Hairline), Margin = new Thickness(Space.Xs),
            Child = new Grid { RowDefinitions = new("62,*,35"), Children = { toolbar, middle, footer } }
        };
        _drawer.Bind(Border.BackgroundProperty, _drawer.GetResourceObservable("WindowBrush"));
        _drawer.Bind(Border.BorderBrushProperty, _drawer.GetResourceObservable("LineBrush"));
        _collectionOverlay.Bind(Panel.BackgroundProperty, _collectionOverlay.GetResourceObservable("ScrimBrush"));
        Content = new Grid { ClipToBounds = true, Children = { _drawer, _collectionOverlay } };

        ActualThemeVariantChanged += (_, _) => ApplyAppearance();
        var settings = Application.Current?.PlatformSettings;
        if (settings is not null)
            settings.ColorValuesChanged += OnSystemColorsChanged;
        ApplyAppearance();
        SizeChanged += (_, e) =>
        {
            _searchColumn.Width = new GridLength(Math.Clamp(e.NewSize.Width * .23, 128, 248));
            _hints.IsVisible = e.NewSize.Width >= 890;
        };

        _history.Changed += OnHistoryChanged;
        Closed += (_, _) =>
        {
            StopMotion();
            _reflow?.Cancel();
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
            if (_openMenu?.IsOpen != true && _collectionColorChooser?.IsDropDownOpen != true)
                Dismiss();
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public void Open(bool positionOnScreen = true, ClipItem? practiceItem = null)
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
        if (practiceItem is not null)
        {
            _filters[_activeFilter].Button.Classes.Remove("active");
            _activeFilter = practiceItem.CollectionId is null
                ? 0 : Math.Max(0, _filters.FindIndex(filter => filter.Collection == practiceItem.CollectionId));
            _filters[_activeFilter].Button.Classes.Add("active");
            _search.Clear();
            ApplyFilter(resetScroll: true);
        }
        AttachNativePanel();

        // The panel rises from the screen edge it is docked to. Reopened in the middle of a dismissal, it turns
        // around from where it is; otherwise its first frame is laid out before the window is on screen.
        var animated = Animated;
        if (!IsVisible)
            MoveDrawer(shown: !animated, animated: false);
        if (!_isShowing)
        {
            _platform.PanelWindow.Showing();
            _isShowing = true;
        }
        Show();
        Activate();
        var practiceIndex = practiceItem is null ? -1 : _visible.ToList().FindIndex(item => item.Id == practiceItem.Id);
        SelectCard(practiceIndex >= 0 ? practiceIndex : Math.Max(0, _cards.SelectedIndex));
        MoveDrawer(shown: true, animated);
    }

    public void Dismiss() => _ = DismissAsync(fade: true);

    private async Task OpenSettingsAsync()
    {
        await DismissAsync(fade: false);
        SettingsRequested?.Invoke();
    }

    /// <param name="fade">
    /// False when the panel only stands between the user and what they asked for, such as a paste: it is gone at once.
    /// </param>
    private async Task DismissAsync(bool fade)
    {
        if (!IsOpen)
            return;
        _dismissing = true;
        _openMenu?.Close();
        CloseCollectionDialog(restoreFocus: false);
        if (Animated)
        {
            StopMotion();
            using var cancellation = new CancellationTokenSource();
            _motion = cancellation;
            MoveDrawer(shown: false, animated: fade);
            try
            {
                if (fade)
                    await Task.Delay(DesktopMotion.Exit, cancellation.Token);
                // A hidden window comes back showing its last frame, so the empty one must reach the screen first.
                var presented = new TaskCompletionSource();
                RequestAnimationFrame(_ => RequestAnimationFrame(_ => presented.TrySetResult()));
                await presented.Task;
                if (cancellation.IsCancellationRequested)
                    return;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(_motion, cancellation))
                    _motion = null;
            }
        }

        Hide();
        NotifyHidden();
        _dismissing = false;
    }

    private void MoveDrawer(bool shown, bool animated)
    {
        // The transitions stay attached while they run, so a reversal starts from the drawer's current place.
        _fade.Duration = _rise.Duration = shown ? DesktopMotion.Enter : DesktopMotion.Exit;
        _fade.Easing = _rise.Easing = shown ? DesktopMotion.Decelerate : DesktopMotion.Linear;
        // Turned around before it has moved, a transition would start from its old target and flash the drawer.
        var opacity = shown ? 1 : 0;
        _drawer.Transitions = animated && _drawer.Opacity != opacity ? _drawerMotion : null;
        _drawer.Opacity = opacity;
        _drawer.RenderTransform = shown ? TransformOperations.Identity : Lowered;
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
        Dispatcher.UIThread.Post(ApplyAppearance);

    public void ApplyAppearance() => PanelTheme.Apply(this, _history.Store.Settings.Accent);

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
        var divider = new Border
        {
            Width = Stroke.Hairline, Height = Space.Xl, Margin = new Thickness(Space.Xs, 0), VerticalAlignment = VerticalAlignment.Center
        };
        divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("LineBrush"));
        _chips.Children.Add(divider);
        foreach (var collection in _collections)
            AddChip("Collection", collection.Name, collection: collection);
        _chips.Children.Add(NewCollectionButton());

        if (oldCollection is not null)
            _activeFilter = Math.Max(0, _filters.FindIndex(filter => filter.Collection == oldCollection));
        _filters[_activeFilter].Button.Classes.Add("active");
    }

    private void AddChip(string name, string label, ClipKind? kind = null, bool pinned = false, ClipCollection? collection = null)
    {
        var button = new Button { Name = name + "Chip", Classes = { "panel-button", "chip" } };
        button.Content = collection is null ? label : new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = Space.S,
            Children = { PanelVisuals.Dot(collection.Color), new TextBlock { Text = label, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis } }
        };
        PanelVisuals.NameButton(button, label);
        var index = _filters.Count;
        button.Click += (_, _) => SelectFilter(index);
        if (collection is not null)
            button.ContextMenu = CreateCollectionMenu(collection, button);
        _filters.Add((button, kind, pinned, collection?.Id));
        _chips.Children.Add(button);
    }

    private void SelectFilter(int index)
    {
        _filters[_activeFilter].Button.Classes.Remove("active");
        _activeFilter = index;
        _filters[_activeFilter].Button.Classes.Add("active");
        ApplyFilter(resetScroll: true);
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
        var slot = _cards.SelectedIndex;
        var focusCards = _cards.IsKeyboardFocusWithin;
        var before = _visible;
        var scroller = _cards.Scroll as ScrollViewer;
        var offset = scroller?.Offset.X ?? 0;
        if (rebuild || !_visible.SequenceEqual(matches))
        {
            _visible = matches;
            _cards.ItemsSource = matches;
        }
        var index = matches.ToList().FindIndex(item => item.Id == previous);
        // When the selected card leaves the row, the one that takes its place is selected.
        if (index < 0)
            index = rebuild ? Math.Clamp(slot, 0, Math.Max(0, matches.Count - 1)) : 0;
        _cards.SelectedIndex = matches.Count > 0 ? index : -1;
        if (resetScroll && scroller is not null)
            scroller.Offset = default;
        if (focusCards)
            SelectCard(index);
        if (rebuild && IsOpen && scroller is not null)
            Reflow(before, scroller, offset);

        var total = store.Items.Count(item => item.CollectionId == collection);
        _count.Text = total == matches.Count ? Strings.Format("ItemsCount", total) : Strings.Format("FilteredCount", matches.Count, total);
        _empty.IsVisible = matches.Count == 0;
        if (_empty.IsVisible)
        {
            var (icon, title, hint) = query.Length > 0 ? (Symbol.Search, "NoResults", "NoResultsHint")
                : collection is not null ? (Symbol.Folder, "EmptyCollection", "EmptyCollectionHint")
                : pinned ? (Symbol.Pin, "EmptyPinned", "EmptyPinnedHint")
                : total > 0 ? (Symbol.Clipboard, "EmptyCategory", "EmptyCategoryHint")
                : _history.Paused ? (Symbol.Pause, "Paused", "PausedHint")
                : (Symbol.Clipboard, "EmptyHistory", "EmptyHistoryHint");
            ShowEmpty(icon, title, hint);
        }
    }

    /// <summary>
    /// Slides the cards that changed place into their new slots after an item was removed, pinned or moved, so
    /// the row is seen closing up instead of being swapped for another one.
    /// </summary>
    private void Reflow(IReadOnlyList<ClipItem> before, ScrollViewer scroller, double offsetBefore)
    {
        _reflow?.Cancel();
        _reflow?.Dispose();
        _reflow = null;
        if (!Animated || before.SequenceEqual(_visible))
            return;
        // A held Delete key changes the row faster than a slide can be read: only a change that stands alone moves.
        var now = Environment.TickCount64;
        var alone = now - _reflowedAt > DesktopMotion.Travel.TotalMilliseconds;
        _reflowedAt = now;
        if (!alone)
            return;

        _cards.UpdateLayout();
        var reflow = _reflow = new CancellationTokenSource();
        var scrolled = scroller.Offset.X - offsetBefore;
        var slots = before.ToList();
        foreach (var card in _cards.GetRealizedContainers())
        {
            var index = _cards.IndexFromContainer(card);
            var was = index < 0 ? -1 : slots.IndexOf(_visible[index]);
            if (was < 0)
                continue;
            var shift = (was - index) * (card.Bounds.Width + card.Margin.Left + card.Margin.Right) + scrolled;
            // From farther than the edge of the row a card would only arrive faster, not from anywhere visible.
            shift = Math.Clamp(shift, -scroller.Viewport.Width, scroller.Viewport.Width);
            if (Math.Abs(shift) >= 1)
                _ = Slide(card, shift, above: Math.Abs(was - index) > 1);
        }

        async Task Slide(Control card, double shift, bool above)
        {
            // A card that jumps over its neighbours passes in front of them.
            card.ZIndex = above ? 1 : 0;
            await DesktopMotion.Arrive(card, FormattableString.Invariant($"translateX({shift:F1}px)"), DesktopMotion.Travel, reflow.Token);
            card.ZIndex = 0;
        }
    }

    private void ShowEmpty(Symbol icon, string title, string? hint)
    {
        var heading = PanelVisuals.Label(Strings.Get(title), TextSize.Subtitle);
        heading.FontWeight = FontWeight.SemiBold;
        heading.TextAlignment = TextAlignment.Center;
        heading.TextWrapping = TextWrapping.Wrap;
        _empty.Children.Clear();
        _empty.Children.Add(PanelVisuals.Glyph(icon, IconSize.Hero, "MutedBrush"));
        _empty.Children.Add(heading);
        if (hint is not null)
        {
            var description = PanelVisuals.Label(Strings.Get(hint), TextSize.Body, "MutedBrush");
            description.TextAlignment = TextAlignment.Center;
            description.TextWrapping = TextWrapping.Wrap;
            _empty.Children.Add(description);
        }
    }

    private void UpdateStatus()
    {
        var notice = _platform.Notices.FirstOrDefault() ?? (_history.Warning is null ? _history.CaptureNotice : null);
        var warning = notice is not null || _history.ShortcutRegistrationFailed || _history.Warning is not null || _history.Paused;
        if (_history.ShortcutRegistrationFailed)
            notice = new PlatformNotice("shortcut-unavailable", Strings.Get("HotkeyBusy"), () => _ = OpenSettingsAsync());
        var text = notice?.Code switch
        {
            "adapter-not-wired" => Strings.Get("PlatformUnavailable"),
            "accessibility-permission" => Strings.Format("AutomaticPasteOff", SetupVisuals.ShortcutInText(_platform.Name == "macOS" ? "⌘V" : "Ctrl+V")),
            "pasteboard-access-approval" => Strings.Get("ClipboardApprovalNotice"),
            "pasteboard-access-denied" => Strings.Get("PasteboardAccessDenied"),
            "keychain-unavailable" => Strings.Get("KeychainUnavailable"),
            "gnome-extension-missing" => Strings.Get("GnomeHelperNotice"),
            "gnome-helper-restart" => Strings.Get("GnomeRestartNotice"),
            "gnome-extension-not-allowed" => notice.Message,
            "clipboard-item-too-large" => Strings.Get("ItemTooLarge"),
            "clipboard-too-many-files" => Strings.Get("TooManyFiles"),
            "clipboard-busy" => Strings.Get("ClipboardBusy"),
            "clipboard-read-failed" => Strings.Get("UnsupportedFormat"),
            _ => notice?.Message ?? _history.Warning ?? Strings.Get(_history.Paused ? "Paused"
                : _platform is DemoPlatformServices ? "DemoStatus" : "LocalHistory")
        };
        var label = PanelVisuals.Label(text, TextSize.Caption, "MutedBrush");
        label.Name = "PlatformStatus";
        ToolTip.SetTip(label, _history.Warning is { } storageWarning && text != storageWarning
            ? text + Environment.NewLine + storageWarning
            : text);
        _status.Children.Clear();
        _status.HorizontalAlignment = warning ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        AddColumn(_status, PanelVisuals.Dot(warning ? "WarningBrush" : "SuccessBrush"), 0);
        AddColumn(_status, label, 1);
        if (notice?.Resolve is { } resolve)
        {
            var action = new Button
            {
                Content = Strings.Get(notice.Code switch
                {
                    "accessibility-permission" => "TurnOn",
                    "pasteboard-access-approval" => "Allow",
                    "pasteboard-access-denied" => "OpenSettings",
                    "gnome-extension-missing" => "Install",
                    "gnome-helper-restart" => "SignOut",
                    "shortcut-unavailable" => "Settings",
                    _ => "GrantAccess"
                }),
                Classes = { "panel-button", "permission" },
                VerticalAlignment = VerticalAlignment.Center
            };
            action.Click += async (_, _) =>
            {
                if (notice.Code == "gnome-helper-restart")
                {
                    action.IsEnabled = false;
                    try { await _history.SignOutAsync(resolve); }
                    finally { action.IsEnabled = true; }
                }
                else
                    resolve();
                DispatcherTimer.RunOnce(() =>
                {
                    if (IsOpen)
                        UpdateStatus();
                }, TimeSpan.FromMilliseconds(400));
            };
            AddColumn(_status, action, 2);
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
        if (_openMenu?.IsOpen == true || HandleCollectionDialogKey(e))
            return;

        var plain = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Command on macOS, Ctrl elsewhere, for search (F) and quick paste (1–9).
        var command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
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
            case Key.F when command:
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
            case Key.Delete or Key.Back when (e.Key == Key.Delete || OperatingSystem.IsMacOS()) && !searchFocused && selected is not null:
                Delete(selected);
                break;
            default:
                if (!command || index < 0 || index >= _visible.Count)
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
        menu.Items.Add(Option(ClipboardOnly ? "Copy" : "Paste", () => _ = PasteAsync(item, false), new KeyGesture(Key.Enter)));
        menu.Items.Add(Option(ClipboardOnly ? "CopyPlain" : "PastePlain", () => _ = PasteAsync(item, true), new KeyGesture(Key.Enter, KeyModifiers.Shift)));
        if (!ClipboardOnly)
            menu.Items.Add(Option("Copy", () =>
            {
                if (TryWrite(item, plain: false))
                {
                    Dismiss();
                    Feedback?.Invoke(Strings.Get("ManualPaste"));
                }
            }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Option(item.IsPinned ? "Unpin" : "Pin", () => TogglePin(item)));
        AddCardCollectionOptions(menu, item, card);
        menu.Items.Add(Option("Delete", () => Delete(item), new KeyGesture(Key.Delete)));
        TrackMenu(menu);
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
        if (_history.WriteToClipboard(item, plain) is not { } error)
            return true;
        Feedback?.Invoke(error);
        return false;
    }

    private async Task PasteAsync(ClipItem item, bool plain)
    {
        if (_pasting || !TryWrite(item, plain))
            return;
        var practice = PracticeTarget?.Invoke();
        if (practice is null)
            _history.CountStoreReviewUse();
        _pasting = true;
        try
        {
            await DismissAsync(fade: false);
            if (practice is not null)
            {
                practice.ReceivePracticePaste(item);
                return;
            }
            var result = await _platform.Paste.PasteAsync();
            if (result == PasteResult.ShortcutSent)
            {
                var settings = _history.Store.Settings;
                if (settings.PasteSoundEnabled)
                    _platform.ClipboardSounds?.Play(paste: true, settings.Volume);
            }
            else
                Feedback?.Invoke(Strings.Get(result == PasteResult.ClipboardChanged ? "PasteStateChanged" : "ManualPaste"));
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
