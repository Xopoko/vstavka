using System.Runtime.InteropServices;
using System.Text;
using Vstavka.Core;
using static Vstavka.Platform.Linux.X11Native;

namespace Vstavka.Platform.Linux;

internal sealed partial class X11Desktop
{
    private const int TargetLimit = 512 * 4;
    private const int ChunkSize = 64 * 1024;
    private const int TransferTimeoutMs = 3000;
    private Incoming? _incoming;
    private Dictionary<nuint, byte[]> _owned = [];
    private readonly Dictionary<(nuint Window, nuint Atom), Outgoing> _outgoing = [];
    private nuint _ownedTime;

    private sealed class Incoming(nuint window, nuint owner, string? app)
    {
        public nuint Window { get; } = window;
        public nuint Owner { get; } = owner;
        public string? App { get; } = app;
        public nuint Target { get; set; }
        public int Limit { get; set; }
        public long Deadline { get; set; }
        public (nuint Atom, string Name)[] Formats { get; set; } = [];
        public string? Mime { get; set; }
        public string? Hint { get; set; }
        public MemoryStream? Stream { get; set; }
    }

    private sealed class Outgoing(nuint target, byte[] data)
    {
        public nuint Target { get; } = target;
        public byte[] Data { get; } = data;
        public int Offset { get; set; }
        public long Deadline { get; set; } = Environment.TickCount64 + TransferTimeoutMs;
    }

    private sealed record Property(uint Atom, byte Format, byte[] Data);

    private void HandleClipboardEvent(Event xevent)
    {
        if (xevent.Header.Type == _fixesEvent)
        {
            var owner = xevent.Owner.Owner;
            if (owner != XGetSelectionOwner(_display, Atom("CLIPBOARD")))
                return;
            if (owner == _window)
                _ownedTime = xevent.Owner.SelectionTime;
            if (_watching)
                BeginRead(owner);
            return;
        }
        switch (xevent.Header.Type)
        {
            case SelectionNotify:
                SelectionReceived(xevent.Notify);
                break;
            case SelectionRequest:
                Respond(xevent.Request);
                break;
            case PropertyNotify:
                PropertyChanged(xevent.Property);
                break;
        }
    }

    private void BeginRead(nuint owner)
    {
        FinishRead();
        if (owner == 0 || owner == _window)
            return;
        var window = CreateHiddenWindow();
        XSelectInput(_display, window, (nint)PropertyChangeMask);
        _incoming = new(window, owner, FocusedApp());
        ConvertSelection(_incoming, Atom("TARGETS"), TargetLimit);
    }

    private void ConvertSelection(Incoming transfer, nuint target, int limit)
    {
        transfer.Target = target;
        transfer.Limit = limit;
        transfer.Deadline = Environment.TickCount64 + TransferTimeoutMs;
        XConvertSelection(_display, Atom("CLIPBOARD"), target, Atom("_VSTAVKA_SELECTION"), transfer.Window, 0);
        XFlush(_display);
    }

    private void SelectionReceived(NotifyEvent notice)
    {
        var transfer = _incoming;
        if (transfer is null || transfer.Window != notice.Header.Window ||
            notice.Selection != Atom("CLIPBOARD") || notice.Target != transfer.Target)
            return;
        if (notice.Property != Atom("_VSTAVKA_SELECTION"))
        {
            FinishRead();
            return;
        }
        var property = ReadProperty(transfer.Window, notice.Property, transfer.Limit, delete: true);
        if (property?.Atom == Atom("INCR") && transfer.Mime is not null && property is { Format: 32, Data.Length: 4 } &&
            BitConverter.ToUInt32(property.Data) <= transfer.Limit)
            transfer.Stream = new MemoryStream();
        else
            ProcessProperty(transfer, property);
    }

    private void ProcessProperty(Incoming transfer, Property? property)
    {
        if (XGetSelectionOwner(_display, Atom("CLIPBOARD")) != transfer.Owner)
        {
            FinishRead();
            return;
        }
        if (transfer.Target == Atom("TARGETS") && property is { Format: 32 } && property.Atom == Atom("ATOM"))
        {
            transfer.Formats = DecodeFormats(property.Data);
            var names = transfer.Formats.Select(pair => pair.Name).ToArray();
            if (!LinuxClipboard.HasPrivateMarker(names))
            {
                var hint = transfer.Formats.FirstOrDefault(pair =>
                    pair.Name.Equals(LinuxClipboard.PasswordHint, StringComparison.OrdinalIgnoreCase));
                if (hint.Atom != 0)
                {
                    ConvertSelection(transfer, hint.Atom, LinuxClipboard.HintBytes);
                    return;
                }
                if (RequestPayload(transfer))
                    return;
            }
        }
        else if (transfer.Mime is null && property is { Format: 8 })
        {
            transfer.Hint = Encoding.ASCII.GetString(property.Data).TrimEnd('\0');
            if (RequestPayload(transfer))
                return;
        }
        else if (transfer.Mime is { } mime && property is { Format: 8, Data.Length: > 0 })
        {
            ClipboardChanged?.Invoke(new(mime, property.Data, Name: transfer.App,
                Formats: transfer.Formats.Select(pair => pair.Name).ToArray(), PasswordHint: transfer.Hint));
        }
        FinishRead();
    }

    private bool RequestPayload(Incoming transfer)
    {
        if (LinuxClipboard.IsPrivate(transfer.Formats.Select(pair => pair.Name).ToArray(), transfer.Hint))
            return false;
        foreach (var mime in LinuxClipboard.PayloadTypes)
        {
            var offered = transfer.Formats.FirstOrDefault(pair => pair.Name.Equals(mime, StringComparison.OrdinalIgnoreCase));
            if (offered.Atom == 0)
                continue;
            transfer.Mime = mime;
            ConvertSelection(transfer, offered.Atom, mime == LinuxClipboard.Image ? HistoryStore.MaxImageBytes : LinuxClipboard.TextBytes);
            return true;
        }
        return false;
    }

    private void PropertyChanged(PropertyEvent e)
    {
        if (e.State == PropertyDelete && _outgoing.TryGetValue((e.Header.Window, e.Atom), out var send))
        {
            SendChunk(e.Header.Window, e.Atom, send);
            return;
        }
        if (e.State != PropertyNewValue || _incoming is not { Stream: { } stream } transfer ||
            transfer.Window != e.Header.Window || e.Atom != Atom("_VSTAVKA_SELECTION"))
            return;
        var remaining = transfer.Limit - (int)stream.Length;
        var chunk = ReadProperty(transfer.Window, e.Atom, remaining, delete: true);
        if (chunk is not { Format: 8 })
        {
            FinishRead();
            return;
        }
        if (chunk.Data.Length == 0)
        {
            ProcessProperty(transfer, chunk with { Data = stream.ToArray() });
            return;
        }
        stream.Write(chunk.Data);
        transfer.Deadline = Environment.TickCount64 + TransferTimeoutMs;
    }

    private void FinishRead()
    {
        if (_incoming is not { } transfer)
            return;
        _incoming = null;
        transfer.Stream?.Dispose();
        XDestroyWindow(_display, transfer.Window);
        XFlush(_display);
    }

    private bool OwnClipboard(string mime, byte[] data)
    {
        if (data.Length > (mime == LinuxClipboard.Image ? HistoryStore.MaxImageBytes : LinuxClipboard.TextBytes))
            return false;
        var formats = new Dictionary<nuint, byte[]> { [Atom(mime)] = data };
        if (mime == LinuxClipboard.Text)
        {
            formats.Add(Atom("UTF8_STRING"), data);
            formats.Add(Atom("text/plain"), data);
            formats.Add(Atom("STRING"), Encoding.Latin1.GetBytes(Encoding.UTF8.GetString(data)));
        }
        var clipboard = Atom("CLIPBOARD");
        XSetSelectionOwner(_display, clipboard, _window, 0);
        if (XGetSelectionOwner(_display, clipboard) != _window)
            return false;
        _owned = formats;
        _ownedTime = 0;
        return true;
    }

    private void Respond(RequestEvent request)
    {
        var property = request.Property != 0 ? request.Property : request.Target;
        var success = request.Selection == Atom("CLIPBOARD") &&
            XGetSelectionOwner(_display, request.Selection) == _window;
        if (success)
        {
            if (request.Target == Atom("TARGETS"))
            {
                var targets = _owned.Keys.Append(Atom("TARGETS"));
                if (_ownedTime != 0)
                    targets = targets.Append(Atom("TIMESTAMP"));
                success = WriteProperty(request.Requestor, property, Atom("ATOM"), 32,
                    targets.SelectMany(value => BitConverter.GetBytes((uint)value)).ToArray());
            }
            else if (request.Target == Atom("TIMESTAMP") && _ownedTime != 0)
                success = WriteProperty(request.Requestor, property, Atom("INTEGER"), 32, BitConverter.GetBytes((uint)_ownedTime));
            else if (_owned.TryGetValue(request.Target, out var bytes))
                success = bytes.Length <= ChunkSize
                    ? WriteProperty(request.Requestor, property, request.Target, 8, bytes)
                    : StartSend(request.Requestor, property, request.Target, bytes);
            else
                success = false;
        }
        var reply = new SelectionNotifyWire
        {
            Type = SelectionNotify, Requestor = (uint)request.Requestor, Time = (uint)request.Time,
            Selection = (uint)request.Selection, Target = (uint)request.Target,
            Property = success ? (uint)property : 0,
        };
        Checked(xcb_send_event_checked(_xcb, 0, (uint)request.Requestor, 0, in reply));
    }

    private bool StartSend(nuint window, nuint property, nuint target, byte[] data)
    {
        uint mask = PropertyChangeMask;
        if (_outgoing.Count >= 8 || _outgoing.ContainsKey((window, property)) ||
            !Checked(xcb_change_window_attributes_checked(_xcb, (uint)window, EventMaskAttribute, in mask)))
            return false;
        if (!WriteProperty(window, property, Atom("INCR"), 32, BitConverter.GetBytes(data.Length)))
        {
            UnwatchRequestor(window);
            return false;
        }
        _outgoing.Add((window, property), new(target, data));
        return true;
    }

    private void SendChunk(nuint window, nuint property, Outgoing send)
    {
        var length = Math.Min(ChunkSize, send.Data.Length - send.Offset);
        if (!WriteProperty(window, property, send.Target, 8, send.Data.AsSpan(send.Offset, length).ToArray()) || length == 0)
        {
            _outgoing.Remove((window, property));
            UnwatchRequestor(window);
            return;
        }
        send.Offset += length;
        send.Deadline = Environment.TickCount64 + TransferTimeoutMs;
    }

    private void UnwatchRequestor(nuint window)
    {
        if (!_outgoing.Keys.Any(key => key.Window == window))
        {
            uint mask = 0;
            Checked(xcb_change_window_attributes_checked(_xcb, (uint)window, EventMaskAttribute, in mask));
        }
    }

    private void ExpireTransfers()
    {
        var now = Environment.TickCount64;
        if (_incoming is { } transfer && transfer.Deadline <= now)
            FinishRead();
        if (_outgoing.Count == 0)
            return;
        foreach (var key in _outgoing.Where(pair => pair.Value.Deadline <= now).Select(pair => pair.Key).ToArray())
        {
            _outgoing.Remove(key);
            UnwatchRequestor(key.Window);
        }
    }

    private Property? ReadProperty(nuint window, nuint property, int limit, bool delete = false)
    {
        var cookie = xcb_get_property(_xcb, delete ? (byte)1 : (byte)0, (uint)window, (uint)property,
            0, 0, (uint)((limit + 3) / 4));
        var reply = xcb_get_property_reply(_xcb, cookie, out var error);
        try
        {
            if (reply == 0)
                return null;
            var header = Marshal.PtrToStructure<PropertyReply>(reply);
            var length = xcb_get_property_value_length(reply);
            if (header.Atom == 0 || header.BytesAfter != 0 || length > limit)
                return null;
            var bytes = new byte[length];
            Marshal.Copy(xcb_get_property_value(reply), bytes, 0, bytes.Length);
            return new(header.Atom, header.Format, bytes);
        }
        finally
        {
            free(reply);
            free(error);
        }
    }

    private bool WriteProperty(nuint window, nuint property, nuint type, byte format, byte[] data) =>
        Checked(xcb_change_property_checked(_xcb, 0, (uint)window, (uint)property, (uint)type,
            format, (uint)(data.Length / (format / 8)), data));

    private (nuint Atom, string Name)[] DecodeFormats(byte[] bytes)
    {
        var formats = new List<(nuint, string)>();
        for (var i = 0; i + 4 <= bytes.Length; i += 4)
        {
            var atom = BitConverter.ToUInt32(bytes, i);
            var reply = xcb_get_atom_name_reply(_xcb, xcb_get_atom_name(_xcb, atom), out var error);
            try
            {
                if (reply != 0 && xcb_get_atom_name_name_length(reply) is > 0 and <= 256 and var length)
                {
                    var name = Marshal.PtrToStringUTF8(xcb_get_atom_name_name(reply), length);
                    if (name is not null)
                        formats.Add((atom, name));
                }
            }
            finally
            {
                free(reply);
                free(error);
            }
        }
        return formats.ToArray();
    }

    private string? FocusedApp() =>
        ActiveWindowClass()?.Split('\0', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

    private string? ActiveWindowClass()
    {
        var active = ReadProperty(_root, Atom("_NET_ACTIVE_WINDOW"), 4);
        var window = active is { Format: 32, Data.Length: 4 } ? BitConverter.ToUInt32(active.Data) : 0;
        if (window == 0)
            return null;
        var wmClass = ReadProperty(window, Atom("WM_CLASS"), 1024);
        return wmClass is { Format: 8 } ? Encoding.Latin1.GetString(wmClass.Data) : null;
    }
}
