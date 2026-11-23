import Gio from 'gi://Gio';
import GLib from 'gi://GLib';

const SETTINGS_KEY = 'allowed-executables';
const BUS_DAEMON = 'org.freedesktop.DBus';

export class AuthorizedClients {
    constructor(settings, revoke) {
        this.settings = settings;
        this.revoke = revoke;
        this.clients = new Map();
        this.paths = this.readPaths();
        this.signal = settings.connect('changed::' + SETTINGS_KEY, () => this.refresh());
    }

    readPaths() {
        return new Set(this.settings.get_strv(SETTINGS_KEY).filter(path => GLib.path_is_absolute(path)));
    }

    refresh() {
        this.paths = this.readPaths();
        for (const [sender, client] of this.clients) {
            const allowed = this.paths.has(client.executable);
            if (client.allowed && !allowed)
                this.revoke(sender);
            client.allowed = allowed;
        }
    }

    async authorize(sender) {
        if (!sender?.startsWith(':'))
            return false;
        let client = this.clients.get(sender);
        if (!client) {
            const watch = Gio.bus_watch_name_on_connection(Gio.DBus.session, sender,
                Gio.BusNameWatcherFlags.NONE, null, () => this.drop(sender));
            client = {watch, executable: null, allowed: false, lookup: null};
            this.clients.set(sender, client);
            client.lookup = executableFor(sender).then(path => {
                client.executable = path;
                client.allowed = this.paths.has(path);
            });
        }
        await client.lookup;
        return this.clients.get(sender) === client && client.allowed;
    }

    isAllowed(sender) {
        return this.clients.get(sender)?.allowed === true;
    }

    drop(sender) {
        const client = this.clients.get(sender);
        if (client) {
            this.clients.delete(sender);
            Gio.bus_unwatch_name(client.watch);
            this.revoke(sender);
        }
    }

    dispose() {
        this.settings.disconnect(this.signal);
        for (const sender of this.clients.keys())
            this.drop(sender);
    }
}

async function executableFor(sender) {
    const pid = await new Promise(resolve => {
        Gio.DBus.session.call(BUS_DAEMON, '/org/freedesktop/DBus', BUS_DAEMON,
            'GetConnectionUnixProcessID', new GLib.Variant('(s)', [sender]), new GLib.VariantType('(u)'),
            Gio.DBusCallFlags.NONE, 1500, null, (bus, result) => {
                try {
                    resolve(bus.call_finish(result).deepUnpack()[0]);
                } catch {
                    resolve(0);
                }
            });
    });
    try {
        return pid > 0 ? GLib.file_read_link('/proc/' + pid + '/exe') : null;
    } catch {
        return null;
    }
}
