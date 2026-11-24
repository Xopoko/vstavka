using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;

namespace Vstavka.Platform.Linux;

internal static partial class GnomeSnapSocket
{
    private const string BusDaemon = "org.freedesktop.DBus";
    private const string Shell = "org.gnome.Shell";
    private const string ExtensionUrl = "https://extensions.gnome.org/extension/11930/vstavka-clipboard-for-snap/";
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static void OpenCatalog() => _ = OpenCatalogAsync();

    private static async Task OpenCatalogAsync()
    {
        if (DBusAddress.Session is not { } address)
            return;
        try
        {
            using var connection = new DBusConnection(new DBusConnectionOptions(address) { AutoConnect = true });
            var request = CreateOpenRequest(connection);
            await connection.CallMethodAsync(request).WaitAsync(Timeout).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or TimeoutException)
        {
            // The portal can be unavailable while the desktop session is closing.
        }
    }

    private static MessageBuffer CreateOpenRequest(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.freedesktop.portal.Desktop", path: "/org/freedesktop/portal/desktop",
            @interface: "org.freedesktop.portal.OpenURI", member: "OpenURI", signature: "ssa{sv}");
        writer.WriteString(string.Empty);
        writer.WriteString(ExtensionUrl);
        writer.WriteDictionaryEnd(writer.WriteDictionaryStart());
        return writer.CreateMessage();
    }

    public static DBusConnection? Connect()
    {
        if (!OperatingSystem.IsLinux() || DBusAddress.Session is not { } sessionAddress)
            return null;

        var uid = geteuid();
        var userId = uid.ToString(CultureInfo.InvariantCulture);
        var path = Path.Combine("/run/user", userId, "snap.vstavka", "vstavka-shell.sock");
        if (!File.Exists(path))
            return null;

        using var cancellation = new CancellationTokenSource(Timeout);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        DBusConnection? connection = null;
        try
        {
            socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellation.Token).AsTask().GetAwaiter().GetResult();
            var size = (uint)Marshal.SizeOf<PeerCredentials>();
            if (getsockopt(socket.SafeHandle, SolSocket, SoPeerCred, out var peer, ref size) != 0 ||
                size != Marshal.SizeOf<PeerCredentials>() || peer.User != uid || peer.Process <= 0 ||
                (uint)peer.Process != ShellProcess(sessionAddress, cancellation.Token))
            {
                socket.Dispose();
                return null;
            }

            connection = new DBusConnection(new PeerOptions(socket, userId));
            connection.ConnectAsync().AsTask().WaitAsync(cancellation.Token).GetAwaiter().GetResult();
            return connection;
        }
        catch (OperationCanceledException)
        {
            connection?.Dispose();
            socket.Dispose();
            return null;
        }
        catch
        {
            connection?.Dispose();
            socket.Dispose();
            throw;
        }
    }

    private static uint ShellProcess(string sessionAddress, CancellationToken cancellationToken)
    {
        using var connection = new DBusConnection(new DBusConnectionOptions(sessionAddress) { AutoConnect = true });
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: BusDaemon, path: "/org/freedesktop/DBus",
            @interface: BusDaemon, member: "GetConnectionUnixProcessID", signature: "s");
        writer.WriteString(Shell);
        return connection.CallMethodAsync(writer.CreateMessage(), static (reply, _) => reply.GetBodyReader().ReadUInt32())
            .WaitAsync(cancellationToken).GetAwaiter().GetResult();
    }

    private sealed class PeerOptions(Socket socket, string userId) : DBusConnectionOptions
    {
        protected override ValueTask<SetupResult> SetupAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SetupResult { ConnectionStream = new NetworkStream(socket, ownsSocket: true), UserId = userId });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials
    {
        public int Process;
        public uint User;
        public uint Group;
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    [DllImport("libc")]
    private static extern int getsockopt(SafeSocketHandle socket, int level, int option, out PeerCredentials value, ref uint length);
}
