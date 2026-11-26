import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Clipboard} from './clipboard.js';
import {Windows} from './windows.js';

export const BUS = 'io.github.xopoko.Vstavka.Shell1';
export const PATH = '/io/github/xopoko/Vstavka/Shell';
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

export class VstavkaExtension extends Extension {
    enable() {
        try {
            this.hotkeySignal = global.display.connect('accelerator-activated', (_display, action) => {
                if (action === this.hotkeyAction && this.sessionAvailable() && this.isAllowed(this.hotkeySender))
                    this.transport.emitSignal(this.hotkeySender, 'HotkeyPressed', null);
            });
            this.windows = new Windows();
            this.clipboard = new Clipboard(
                (sender, args) => this.clipboardChanged(sender, args),
                () => this.sessionAvailable() && this.clipboardActive &&
                    this.isAllowed(this.clipboardSender) ? this.clipboardSender : null);
            const [, bytes] = this.dir.get_child('shell.xml').load_contents(null);
            const xml = new TextDecoder().decode(bytes);
            this.transport = this.startTransport(connection => this.exportObject(connection, xml),
                sender => this.revoke(sender));
        } catch (error) {
            this.disable();
            throw error;
        }
    }

    disable() {
        this.transport?.dispose();
        this.transport = null;
        this.clipboardSender = null;
        this.clipboardActive = false;
        this.clipboardWrite = null;
        this.releaseHotkey();
        if (this.hotkeySignal)
            global.display.disconnect(this.hotkeySignal);
        this.hotkeySignal = null;
        this.clipboard?.dispose();
        this.clipboard = null;
        this.windows?.dispose();
        this.windows = null;
    }

    sessionAvailable() {
        return !!this.transport && !Main.sessionMode.isLocked && !Main.sessionMode.isGreeter;
    }

    exportObject(connection, xml) {
        const object = Gio.DBusExportedObject.wrapJSObject(xml, this);
        object.export(connection, PATH);
        return object;
    }

    isAllowed(sender) {
        return this.transport?.isAllowed(sender) === true;
    }

    async authorizedSender(invocation) {
        if (!this.sessionAvailable()) {
            invocation.return_dbus_error(BUS + '.SessionUnavailable', 'The GNOME session must be unlocked.');
            return null;
        }
        const transport = this.transport;
        const sender = await transport.authorize(invocation);
        if (!sender || this.transport !== transport || !this.sessionAvailable() || !transport.isAllowed(sender)) {
            invocation.return_dbus_error(BUS + '.AccessDenied', 'The caller executable is not allowed by Vstavka.');
            return null;
        }
        return sender;
    }

    revoke(sender) {
        if (this.clipboardWrite?.sender === sender)
            this.clipboardWrite = null;
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
            this.isAllowed(sender))
            this.transport.emitSignal(sender, 'ClipboardChanged', new GLib.Variant('(saysss)', args));
    }

    async SetClipboardAsync([mime, data], invocation) {
        const sender = await this.authorizedSender(invocation);
        if (!sender)
            return;
        try {
            const success = this.clipboard.write(mime, data);
            if (success)
                this.clipboardWrite = {sender, revision: this.clipboard.revision};
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
            const write = this.clipboardWrite;
            const wroteClipboard = write?.sender === sender;
            const clipboardChanged = () => wroteClipboard && write.revision !== this.clipboard?.revision;
            const success = await this.windows.paste(() => this.sessionAvailable() &&
                this.isAllowed(sender) && wroteClipboard && !clipboardChanged());
            if (!success && this.sessionAvailable() && clipboardChanged()) {
                invocation.return_dbus_error(BUS + '.ClipboardChanged', 'The clipboard changed before pasting.');
                return;
            }
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
