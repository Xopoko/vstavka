import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {AuthorizedClients} from './authorization.js';
import {Clipboard} from './clipboard.js';
import {Windows} from './windows.js';

const BUS = 'io.github.xopoko.Vstavka.Shell1';
const PATH = '/io/github/xopoko/Vstavka/Shell';
const KEY = 'vstavka-hotkey';
const ACCELERATORS = new Set(['<Control><Shift>v', '<Control><Alt>v', '<Alt>v']);

export default class VstavkaExtension extends Extension {
    enable() {
        try {
            this.settings = this.getSettings();
            this.authorization = new AuthorizedClients(this.settings, sender => this.revoke(sender));
            this.windows = new Windows();
            this.clipboard = new Clipboard(
                (sender, args) => this.clipboardChanged(sender, args),
                () => this.sessionAvailable() && this.clipboardActive &&
                    this.authorization.isAllowed(this.clipboardSender) ? this.clipboardSender : null);
            const [, bytes] = this.dir.get_child('shell.xml').load_contents(null);
            this.object = Gio.DBusExportedObject.wrapJSObject(new TextDecoder().decode(bytes), this);
            this.object.export(Gio.DBus.session, PATH);
            this.bus = Gio.bus_own_name_on_connection(Gio.DBus.session, BUS,
                Gio.BusNameOwnerFlags.DO_NOT_QUEUE, null, null);
        } catch (error) {
            this.disable();
            throw error;
        }
    }

    disable() {
        if (this.bus)
            Gio.bus_unown_name(this.bus);
        this.bus = null;
        this.object?.unexport();
        this.object = null;
        this.authorization?.dispose();
        this.authorization = null;
        this.clipboardSender = null;
        this.clipboardActive = false;
        this.releaseHotkey();
        this.clipboard?.dispose();
        this.clipboard = null;
        this.windows?.dispose();
        this.windows = null;
        this.settings = null;
    }

    sessionAvailable() {
        return !!this.object && !Main.sessionMode.isLocked && !Main.sessionMode.isGreeter;
    }

    async authorizedSender(invocation) {
        const sender = invocation.get_sender();
        const authorization = this.authorization;
        if (!this.sessionAvailable()) {
            invocation.return_dbus_error(BUS + '.SessionUnavailable', 'The GNOME session must be unlocked.');
            return null;
        }
        if (!await authorization.authorize(sender) || this.authorization !== authorization ||
            !this.sessionAvailable() || !authorization.isAllowed(sender)) {
            invocation.return_dbus_error(BUS + '.AccessDenied', 'The caller executable is not allowed by Vstavka.');
            return null;
        }
        return sender;
    }

    revoke(sender) {
        if (this.clipboardSender === sender) {
            this.clipboardSender = null;
            this.clipboardActive = false;
        }
        if (this.hotkeySender === sender)
            this.releaseHotkey();
    }

    async SubscribeClipboardAsync(_params, invocation) {
        const sender = await this.authorizedSender(invocation);
        if (!sender)
            return;
        if (this.clipboardSender && this.clipboardSender !== sender) {
            invocation.return_dbus_error(BUS + '.ClipboardBusy', 'Another allowed client owns the clipboard subscription.');
            return;
        }
        this.clipboardSender = sender;
        this.clipboardActive = true;
        invocation.return_value(null);
    }

    async UnsubscribeClipboardAsync(_params, invocation) {
        const sender = await this.authorizedSender(invocation);
        if (!sender)
            return;
        if (this.clipboardSender === sender)
            this.clipboardActive = false;
        invocation.return_value(null);
    }

    clipboardChanged(sender, args) {
        if (this.sessionAvailable() && this.clipboardActive && sender === this.clipboardSender &&
            this.authorization.isAllowed(sender))
            Gio.DBus.session.emit_signal(sender, PATH, BUS, 'ClipboardChanged', new GLib.Variant('(saysss)', args));
    }

    async SetClipboardAsync([mime, data], invocation) {
        if (!await this.authorizedSender(invocation))
            return;
        try {
            const success = this.clipboard.write(mime, data);
            invocation.return_value(new GLib.Variant('(b)', [success]));
        } catch (error) {
            invocation.return_dbus_error(BUS + '.ClipboardWriteFailed', error.message);
        }
    }

    async SetAcceleratorAsync([accelerator], invocation) {
        const sender = await this.authorizedSender(invocation);
        if (!sender)
            return;
        const success = ACCELERATORS.has(accelerator) && this.registerHotkey(sender, accelerator);
        invocation.return_value(new GLib.Variant('(b)', [success]));
    }

    registerHotkey(sender, accelerator) {
        if (this.hotkeySender && this.hotkeySender !== sender)
            return false;
        if (this.hotkeySender && this.accelerator === accelerator)
            return true;
        const probe = global.display.grab_accelerator(accelerator, Meta.KeyBindingFlags.NONE);
        if (probe === Meta.KeyBindingAction.NONE)
            return false;
        global.display.ungrab_accelerator(probe);

        const previous = this.settings.get_strv(KEY);
        const wasRegistered = !!this.hotkeySender;
        if (wasRegistered)
            Main.wm.removeKeybinding(KEY);
        if (!this.settings.set_strv(KEY, [accelerator]) || !this.addKeybinding()) {
            this.settings.set_strv(KEY, previous);
            if (wasRegistered)
                this.addKeybinding();
            return false;
        }
        this.hotkeySender = sender;
        this.accelerator = accelerator;
        return true;
    }

    addKeybinding() {
        return Main.wm.addKeybinding(KEY, this.settings, Meta.KeyBindingFlags.IGNORE_AUTOREPEAT,
            Shell.ActionMode.NORMAL | Shell.ActionMode.OVERVIEW, () => {
                if (this.sessionAvailable() && this.authorization.isAllowed(this.hotkeySender))
                    Gio.DBus.session.emit_signal(this.hotkeySender, PATH, BUS, 'HotkeyPressed', null);
            }) !== Meta.KeyBindingAction.NONE;
    }

    async ReleaseHotkeyAsync(_params, invocation) {
        const sender = await this.authorizedSender(invocation);
        if (!sender)
            return;
        if (sender === this.hotkeySender)
            this.releaseHotkey();
        invocation.return_value(null);
    }

    releaseHotkey() {
        if (this.hotkeySender)
            Main.wm.removeKeybinding(KEY);
        this.hotkeySender = null;
        this.accelerator = null;
    }

    async CaptureTargetAsync(_params, invocation) {
        if (!await this.authorizedSender(invocation))
            return;
        this.windows.captureTarget();
        invocation.return_value(null);
    }

    async PasteAsync(_params, invocation) {
        const sender = await this.authorizedSender(invocation);
        if (!sender)
            return;
        try {
            const success = await this.windows.paste(() => this.sessionAvailable() &&
                this.authorization.isAllowed(sender));
            invocation.return_value(new GLib.Variant('(b)', [success]));
        } catch (error) {
            invocation.return_dbus_error(BUS + '.PasteFailed', error.message);
        }
    }

    async PreparePanelAsync(_params, invocation) {
        if (!await this.authorizedSender(invocation))
            return;
        this.windows.preparePanel();
        invocation.return_value(null);
    }
}
