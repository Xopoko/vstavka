using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core;
using Vstavka.Core.Localization;
using System.Windows;

namespace Vstavka.Presentation;

public sealed class ClipCard : INotifyPropertyChanged
{
    public ClipItem Item { get; private set; }
    private string? _title, _preview, _typeLabel, _source, _time, _accessibleName, _domain, _colorText;
    private string[]? _fileNames;
    private FlowDirection? _contentFlowDirection;
    private Brush? _swatch;
    private WeakReference<ImageSource>? _image;
    private bool _imageUnavailable;
    private ClipKind _kind;
    private string _text, _sourceApp, _language, _typeBrushKey;
    private string[] _files;
    private byte[]? _imageBytes;
    private DateTimeOffset _localCapturedAt;
    private DateTime _today;
    private bool _pinned;

    public string Title => _title ??= GetTitle();
    public string Preview => _preview ??= Item.Preview ?? Item.Text ?? string.Empty;
    public string TypeLabel => _typeLabel ??= IsFiles
        ? $"{Item.TypeLabel} · {(Item.Files?.Length ?? 0).ToString(Strings.Culture)}"
        : Item.TypeLabel;
    public string Source => _source ??= string.IsNullOrWhiteSpace(Item.SourceApp) ? Strings.Get("Clipboard") : Item.SourceApp;
    public ImageSource? SourceIcon { get; private set; }
    public string Time => _time ??= _localCapturedAt.ToString(_localCapturedAt.Date == _today ? "t" : "m", Strings.Culture);
    public string AccessibleName => _accessibleName ??=
        $"{(IsFiles && Item.Files?.Length != 1 ? TypeLabel : $"{Item.TypeLabel}: {Title}")}. {Source}, {Time}{(Item.IsPinned ? ". " + Strings.Get("PinnedAccessible") : string.Empty)}";
    public string PinGlyph => Item.IsPinned ? "\uE840" : "\uE718";
    public string PinTooltip => Strings.Get(Item.IsPinned ? "Unpin" : "Pin");
    public Brush PinBrush => ThemeManager.GetBrush(Item.IsPinned ? "InkBrush" : "MutedBrush");
    public bool IsImage => Item.Kind == ClipKind.Image;
    public bool IsColor => Item.Kind == ClipKind.Color;
    public bool IsFiles => Item.Kind == ClipKind.Files;
    public bool IsLink => Item.Kind == ClipKind.Link;
    public bool IsText => !IsImage && !IsColor && !IsFiles && !IsLink;
    public string TypeGlyph => Item.Kind switch { ClipKind.Link => "\uE71B", ClipKind.Color => "\uE790", ClipKind.Image => "\uEB9F", ClipKind.Files => "\uE8B7", _ => "\uE8A5" };
    public string Domain => _domain ??= Uri.TryCreate(Item.Text, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) : Strings.Get("Link");
    public string ColorText => _colorText ??= (Item.Text ?? Title).Trim();
    public Brush Swatch => _swatch ??= GetSwatch();
    public string[] FileNames => _fileNames ??= (Item.Files ?? Array.Empty<string>()).Take(3).Select(Path.GetFileName).Select(x => x ?? string.Empty).ToArray();
    public FlowDirection ContentFlowDirection => _contentFlowDirection ??= DetectDirection(Preview);
    public ImageSource? Image
    {
        get
        {
            if (_imageUnavailable) return null;
            if (_image is not null && _image.TryGetTarget(out var cached)) return cached;
            var image = LoadImage(Item);
            if (image is null) _imageUnavailable = true;
            else _image = new WeakReference<ImageSource>(image);
            // Visible Image controls hold a strong reference. Scrolled-away thumbnails
            // remain reclaimable even when the history keeps hundreds of cards alive.
            return image;
        }
    }
    public Brush TypeBrush => ThemeManager.GetBrush(_typeBrushKey);
    public ClipCard(ClipItem item, ImageSource? sourceIcon = null)
    {
        Item = item;
        SourceIcon = sourceIcon;
        _kind = item.Kind;
        _text = item.Text;
        _sourceApp = item.SourceApp;
        _files = SnapshotFiles(item.Files);
        _imageBytes = item.ImageBytes;
        _pinned = item.IsPinned;
        _localCapturedAt = item.CapturedAt.ToLocalTime();
        _today = DateTime.Today;
        _language = Strings.Language;
        _typeBrushKey = item.Kind + "TypeBrush";
    }

    public void Refresh(ClipItem item, ImageSource? sourceIcon = null)
    {
        var kindChanged = _kind != item.Kind;
        var filesChanged = !FilesEqual(_files, item.Files);
        var contentChanged = kindChanged || _text != item.Text || filesChanged;
        var languageChanged = _language != Strings.Language;
        var localCapturedAt = item.CapturedAt.ToLocalTime();
        var today = DateTime.Today;
        var timeChanged = _localCapturedAt != localCapturedAt || _localCapturedAt.Offset != localCapturedAt.Offset || _today != today;
        var sourceChanged = _sourceApp != item.SourceApp;
        var sourceIconChanged = !ReferenceEquals(SourceIcon, sourceIcon);
        var pinChanged = _pinned != item.IsPinned;
        var imageChanged = kindChanged || !ReferenceEquals(_imageBytes, item.ImageBytes);
        var itemChanged = !ReferenceEquals(Item, item);
        Item = item;
        SourceIcon = sourceIcon;

        if (contentChanged || languageChanged)
        {
            _title = _preview = _typeLabel = _domain = _colorText = null;
            _fileNames = null;
            _contentFlowDirection = null;
        }
        if (contentChanged) _swatch = null;
        if (sourceChanged || languageChanged) _source = null;
        if (timeChanged || languageChanged) _time = null;
        if (imageChanged) { _image = null; _imageUnavailable = false; }

        _kind = item.Kind;
        _text = item.Text;
        _sourceApp = item.SourceApp;
        if (filesChanged) _files = SnapshotFiles(item.Files);
        _imageBytes = item.ImageBytes;
        _pinned = item.IsPinned;
        _localCapturedAt = localCapturedAt;
        _today = today;
        _language = Strings.Language;
        if (kindChanged) _typeBrushKey = item.Kind + "TypeBrush";

        if (itemChanged || contentChanged || languageChanged || timeChanged || sourceChanged || pinChanged || imageChanged)
        {
            _accessibleName = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
        else if (sourceIconChanged)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceIcon)));
    }

    private string GetTitle()
    {
        var title = Item.Title;
        return string.IsNullOrWhiteSpace(title) ? Strings.Get("Untitled") : title;
    }

    private Brush GetSwatch()
    {
        if (Item.Kind == ClipKind.Color)
        {
            try { var color = (Color)ColorConverter.ConvertFromString(ColorText); var brush = new SolidColorBrush(color); brush.Freeze(); return brush; } catch (FormatException) { } catch (NotSupportedException) { }
        }
        return ThemeManager.GetBrush("MediaBrush");
    }

    private static string[] SnapshotFiles(string[]? files) => files is { Length: > 0 } ? (string[])files.Clone() : Array.Empty<string>();

    private static bool FilesEqual(string[] snapshot, string[]? files)
    {
        if (snapshot.Length != (files?.Length ?? 0)) return false;
        for (var i = 0; i < snapshot.Length; i++) if (snapshot[i] != files![i]) return false;
        return true;
    }

    private static ImageSource? LoadImage(ClipItem item)
    {
        if (item.Kind == ClipKind.Image && item.ImageBytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(item.ImageBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 520; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException) { }
        }
        return null;
    }

    private static FlowDirection DetectDirection(string text)
    {
        foreach (char c in text)
        {
            // First strong letter chooses payload direction independently of the interface language.
            if (!char.IsLetter(c)) continue;
            return c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFC' ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
