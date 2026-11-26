using Tmds.DBus.Protocol;

namespace Vstavka.Platform.Linux;

internal sealed class GnomeDesktop : ILinuxDesktop
{
    private const string BusName = "io.github.xopoko.Vstavka.Shell1";
    private const string ObjectPath = "/io/github/xopoko/Vstavka/Shell";
    private const string BusDaemon = "org.freedesktop.DBus";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private readonly DBusConnection _connection;
    private readonly string? _owner;
    private readonly Task<Exception?> _disconnected;
    private IDisposable? _clipboardMatch;
    private IDisposable? _hotkeyMatch;
    private bool _supportsCheckAccess = true;
    private bool _targetCaptured;

    private GnomeDesktop(DBusConnection connection, string? owner)
    {
        _connection = connection;
        _owner = owner;
        _disconnected = connection.DisconnectedAsync();
    }

    public bool IsDisconnected => _disconnected.IsCompleted;

    public static GnomeConnection TryConnect()
    {
        if (!OperatingSystem.IsLinux() || DBusAddress.Session is not { } address)
            return new(null, GnomeAccess.Unknown);
        var connection = new DBusConnection(address);
        try
        {
            Wait(connection.ConnectAsync().AsTask());
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: BusDaemon, path: "/org/freedesktop/DBus",
                @interface: BusDaemon, member: "GetNameOwner", signature: "s");
            writer.WriteString(BusName);
            var owner = Wait(connection.CallMethodAsync(writer.CreateMessage(),
                static (message, _) => message.GetBodyReader().ReadString()));
            return Subscribe(connection, owner);
        }
        catch (DBusErrorReplyException error)
        {
            connection.Dispose();
            return new(null, AccessError(error));
        }
        catch (Exception error) when (IsBusFailure(error))
        {
            connection.Dispose();
            return new(null, GnomeAccess.Unknown);
        }
    }

    public static GnomeConnection TryConnectSnap()
    {
        DBusConnection? connection = null;
        try
        {
            connection = GnomeSnapSocket.Connect();
            return connection is null ? new(null, GnomeAccess.Unavailable) : Subscribe(connection, owner: null);
        }
        catch (DBusErrorReplyException error)
        {
            connection?.Dispose();
            return new(null, AccessError(error));
        }
        catch (Exception error) when (IsBusFailure(error) || error is System.Net.Sockets.SocketException)
        {
            connection?.Dispose();
            return new(null, GnomeAccess.Unavailable);
        }
    }

    private static GnomeConnection Subscribe(DBusConnection connection, string? owner)
    {
        var desktop = new GnomeDesktop(connection, owner);
        // Reserve the subscription before the UI starts monitoring.
        Wait(connection.CallMethodAsync(desktop.CreateCall("SubscribeClipboard")));
        return new(desktop);
    }

    public GnomeConnection RefreshAccess()
    {
        try
        {
            if (_supportsCheckAccess)
            {
                try
                {
                    Wait(_connection.CallMethodAsync(CreateCall("CheckAccess")));
                    return new(this);
                }
                catch (DBusErrorReplyException error) when (error.ErrorName == BusDaemon + ".Error.UnknownMethod")
                {
                    _supportsCheckAccess = false;
                }
            }
            Wait(_connection.CallMethodAsync(CreateCall("SubscribeClipboard")));
            return new(this);
        }
        catch (DBusErrorReplyException error)
        {
            var access = AccessError(error);
            return new(access == GnomeAccess.SessionUnavailable ? this : null, access);
        }
        catch (Exception error) when (IsBusFailure(error))
        {
            return new(null, GnomeAccess.Unknown);
        }
    }

    private static GnomeAccess AccessError(DBusErrorReplyException error) => error.ErrorName switch
    {
        BusName + ".AccessDenied" => GnomeAccess.Denied,
        BusName + ".ClipboardBusy" => GnomeAccess.Busy,
        BusName + ".SessionUnavailable" => GnomeAccess.SessionUnavailable,
        BusDaemon + ".Error.NameHasNoOwner" or BusDaemon + ".Error.ServiceUnknown" => GnomeAccess.Unavailable,
        _ => GnomeAccess.Unknown,
    };

    public PlatformCapabilities Capabilities => PlatformCapabilities.WatchClipboard | PlatformCapabilities.GlobalHotkey;

    public event Action<LinuxClipboardData>? ClipboardChanged;
    public event Action? HotkeyPressed;

    public void StartWatching()
    {
        try
        {
            _clipboardMatch = Wait(_connection.AddMatchAsync(
                Rule("ClipboardChanged"),
                static (message, _) =>
                {
                    var reader = message.GetBodyReader();
                    return new LinuxClipboardData(reader.ReadString(), reader.ReadArrayOfByte(),
                        reader.ReadString(), reader.ReadString(), reader.ReadString());
                },
                notification =>
                {
                    if (notification.HasValue)
                        ClipboardChanged?.Invoke(notification.Value);
                }, flags: ObserverFlags.NoSubscribe, emitOnCapturedContext: false).AsTask());
        }
        catch (Exception error) when (_owner is null && IsBusFailure(error))
        {
            // The extension can be disabled between subscription and attaching the signal handler.
            _connection.Dispose();
        }
    }

    public void StopWatching()
    {
        _clipboardMatch?.Dispose();
        _clipboardMatch = null;
        TryCall("UnsubscribeClipboard");
    }

    public bool RegisterHotkey(LinuxGesture gesture)
    {
        try
        {
            _hotkeyMatch ??= Wait(_connection.AddMatchAsync(Rule("HotkeyPressed"),
                static (_, _) => true,
                notification =>
                {
                    if (notification.HasValue)
                        HotkeyPressed?.Invoke();
                }, flags: ObserverFlags.NoSubscribe, emitOnCapturedContext: false).AsTask());
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: _owner, path: ObjectPath,
                @interface: BusName, member: "SetAccelerator", signature: "s");
            writer.WriteString(gesture.Accelerator);
            return Wait(CallBoolean(writer.CreateMessage()));
        }
        catch (Exception error) when (IsBusFailure(error))
        {
            return false;
        }
    }

    public void ReleaseHotkey()
    {
        _hotkeyMatch?.Dispose();
        _hotkeyMatch = null;
        TryCall("ReleaseHotkey");
    }

    public bool Write(string mime, byte[] data)
    {
        try
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: _owner, path: ObjectPath,
                @interface: BusName, member: "SetClipboard", signature: "say");
            writer.WriteString(mime);
            writer.WriteArray(data);
            return Wait(CallBoolean(writer.CreateMessage()));
        }
        catch (Exception error) when (IsBusFailure(error))
        {
            return false;
        }
    }

    public void CaptureTarget() => _targetCaptured = TryCall("CaptureTarget");
    public void PreparePanel() => TryCall("PreparePanel");

    public async Task<PasteResult> PasteAsync()
    {
        if (!_targetCaptured)
            return PasteResult.Manual;
        _targetCaptured = false;
        try
        {
            return await CallBoolean(CreateCall("Paste")).WaitAsync(Timeout).ConfigureAwait(false)
                ? PasteResult.ShortcutSent : PasteResult.Manual;
        }
        catch (DBusErrorReplyException error) when (error.ErrorName == BusName + ".ClipboardChanged")
        {
            return PasteResult.ClipboardChanged;
        }
        catch (Exception error) when (IsBusFailure(error))
        {
            return PasteResult.Manual;
        }
    }

    public void Dispose()
    {
        _clipboardMatch?.Dispose();
        _hotkeyMatch?.Dispose();
        _connection.Dispose();
    }

    private MatchRule Rule(string member) => new()
    {
        Type = MessageType.Signal, Sender = _owner, Interface = BusName, Path = ObjectPath, Member = member,
    };

    private MessageBuffer CreateCall(string member)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: _owner, path: ObjectPath, @interface: BusName, member: member);
        return writer.CreateMessage();
    }

    private bool TryCall(string member)
    {
        try
        {
            Wait(_connection.CallMethodAsync(CreateCall(member)));
            return true;
        }
        catch (Exception error) when (IsBusFailure(error))
        {
            return false;
        }
    }

    private Task<bool> CallBoolean(MessageBuffer message) =>
        _connection.CallMethodAsync(message, static (reply, _) => reply.GetBodyReader().ReadBool());

    private static T Wait<T>(Task<T> task) => task.WaitAsync(Timeout).GetAwaiter().GetResult();
    private static void Wait(Task task) => task.WaitAsync(Timeout).GetAwaiter().GetResult();

    private static bool IsBusFailure(Exception error) =>
        error is DBusExceptionBase or IOException or TimeoutException or ObjectDisposedException;
}
