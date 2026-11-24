import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import {BUS, PATH} from './extension-core.js';

const APP_PATH = /^\/snap\/vstavka\/(?:[0-9]+|x[0-9]+)\/app\/Vstavka\.Desktop$/;
const APP_PROFILE = 'snap.vstavka.vstavka (enforce)';
const NOFOLLOW = Gio.FileQueryInfoFlags.NOFOLLOW_SYMLINKS;

export class SnapPeer {
    constructor(exportObject, onClose) {
        this.onClose = onClose;
        this.uid = new Gio.Credentials().get_unix_user();
        const directory = GLib.build_filenamev([GLib.get_user_runtime_dir(), 'snap.vstavka']);
        const info = Gio.File.new_for_path(directory);
        if (GLib.mkdir_with_parents(directory, 0o700) !== 0)
            return;
        const attributes = info.query_info('standard::type,unix::uid,unix::mode', NOFOLLOW, null);
        if (attributes.get_file_type() !== Gio.FileType.DIRECTORY ||
            attributes.get_attribute_uint32('unix::uid') !== this.uid ||
            (attributes.get_attribute_uint32('unix::mode') & 0o077) !== 0)
            return;

        const path = GLib.build_filenamev([directory, 'vstavka-shell.sock']);
        clearStaleSocket(path, this.uid);
        this.server = Gio.DBusServer.new_sync('unix:path=' + Gio.dbus_address_escape_value(path),
            Gio.DBusServerFlags.AUTHENTICATION_REQUIRE_SAME_USER, Gio.dbus_generate_guid(), null, null);
        this.serverSignal = this.server.connect('new-connection', (server, connection) => {
            if (this.server !== server || this.connection || !isVstavkaSnap(connection.get_peer_credentials(), this.uid))
                return false;
            this.object = exportObject(connection);
            this.connection = connection;
            this.closed = connection.connect('closed', () => this.clear());
            return true;
        });
        this.server.start();
    }

    authorize(invocation) {
        const connection = invocation.get_connection();
        return this.isAllowed(connection) ? connection : null;
    }

    isAllowed(connection) {
        return !!connection && this.connection === connection && !connection.is_closed();
    }

    emitSignal(connection, member, value) {
        if (this.isAllowed(connection))
            connection.emit_signal(null, PATH, BUS, member, value);
    }

    clear() {
        const connection = this.connection;
        if (!connection)
            return;
        this.connection = null;
        connection.disconnect(this.closed);
        this.closed = null;
        this.object.unexport();
        this.object = null;
        this.onClose(connection);
    }

    dispose() {
        const server = this.server;
        this.server = null;
        if (server) {
            server.disconnect(this.serverSignal);
            server.stop();
        }
        const connection = this.connection;
        this.clear();
        if (connection && !connection.is_closed())
            connection.close(null, null);
    }
}

function clearStaleSocket(path, uid) {
    const file = Gio.File.new_for_path(path);
    const info = file.query_exists(null) && file.query_info('unix::uid,unix::mode', NOFOLLOW, null);
    if (!info || info.get_attribute_uint32('unix::uid') !== uid ||
        (info.get_attribute_uint32('unix::mode') & 0o170000) !== 0o140000)
        return;

    const socket = Gio.Socket.new(Gio.SocketFamily.UNIX, Gio.SocketType.STREAM, Gio.SocketProtocol.DEFAULT);
    socket.set_blocking(false);
    try {
        socket.connect(Gio.UnixSocketAddress.new(path), null);
    } catch (error) {
        if (error.matches(Gio.io_error_quark(), Gio.IOErrorEnum.CONNECTION_REFUSED))
            file.delete(null);
    } finally {
        socket.close();
    }
}

function isVstavkaSnap(credentials, uid) {
    try {
        if (credentials?.get_unix_user() !== uid)
            return false;
        const pid = credentials.get_unix_pid();
        if (pid <= 0)
            return false;

        const proc = '/proc/' + pid;
        const path = GLib.file_read_link(proc + '/exe');
        if (!APP_PATH.test(path))
            return false;
        const file = Gio.File.new_for_path(path).query_info('standard::type,unix::uid,unix::mode', NOFOLLOW, null);
        if (file.get_file_type() !== Gio.FileType.REGULAR || file.get_attribute_uint32('unix::uid') !== 0 ||
            (file.get_attribute_uint32('unix::mode') & 0o022) !== 0)
            return false;

        const profile = proc + '/attr/apparmor/current';
        const [, label] = GLib.file_get_contents(GLib.file_test(profile, GLib.FileTest.EXISTS) ?
            profile : proc + '/attr/current');
        return new TextDecoder().decode(label).trim() === APP_PROFILE;
    } catch {
        return false;
    }
}
