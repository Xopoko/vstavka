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
const MODIFIERS = ['<Control>', '<Alt>', '<Shift>', '<Super>'];
const ACCELERATOR = /^(?<modifiers>(?:<(?:Control|Alt|Shift|Super)>){1,4})(?<key>[a-z0-9]|F(?:[1-9]|1[0-9]|2[0-4])|space)(?![\s\S])/;

function normalizeAccelerator(accelerator) {
    const match = ACCELERATOR.exec(accelerator);
    if (!match)
        return null;
    const modifiers = match.groups.modifiers.match(/<[^>]+>/g);
    if (new Set(modifiers).size !== modifiers.length || modifiers.every(modifier => modifier === '<Shift>'))
        return null;
    return MODIFIERS.filter(modifier => modifiers.includes(modifier)).join('') + match.groups.key;
}

export default class VstavkaExtension extends Extension {
    enable() {
        try {
            this.settings = this.getSettings();
            this.authorization = new AuthorizedClients(this.settings, sender => this.revoke(sender));
            this.hotkeySignal = global.display.connect('accelerator-activated', (_display, action) => {
                if (action === this.hotkeyAction && this.sessionAvailable() && this.authorization.isAllowed(this.hotkeySender))
                    Gio.DBus.session.emit_signal(this.hotkeySender, PATH, BUS, 'HotkeyPressed', null);
            });
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
        if (this.hotkeySignal)
            global.display.disconnect(this.hotkeySignal);
        this.hotkeySignal = null;
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

    async CheckAccessAsync(_params, invocation) {
        if (await this.authorizedSender(invocation))
            invocation.return_value(null);
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
        const normalized = normalizeAccelerator(accelerator);
        const success = normalized !== null && this.registerHotkey(sender, normalized);
        invocation.return_value(new GLib.Variant('(b)', [success]));
    }

    registerHotkey(sender, accelerator) {
        if (this.hotkeySender && this.hotkeySender !== sender)
            return false;
        if (this.hotkeySender && this.accelerator === accelerator)
            return true;
        const action = global.display.grab_accelerator(accelerator, Meta.KeyBindingFlags.IGNORE_AUTOREPEAT);
        if (action === Meta.KeyBindingAction.NONE)
            return false;
        if (!this.settings.set_strv(KEY, [accelerator])) {
            global.display.ungrab_accelerator(action);
            return false;
        }
        this.releaseHotkey();
        Main.wm.allowKeybinding(Meta.external_binding_name_for_action(action), Shell.ActionMode.NORMAL | Shell.ActionMode.OVERVIEW);
        this.hotkeyAction = action;
        this.hotkeySender = sender;
        this.accelerator = accelerator;
        return true;
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
        if (this.hotkeyAction) {
            Main.wm.allowKeybinding(Meta.external_binding_name_for_action(this.hotkeyAction), Shell.ActionMode.NONE);
            global.display.ungrab_accelerator(this.hotkeyAction);
        }
        this.hotkeyAction = null;
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
