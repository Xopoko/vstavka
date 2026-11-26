import Gdk from 'gi://Gdk?version=4.0';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Gtk from 'gi://Gtk?version=4.0';
import System from 'system';

const SHELL = 'io.github.xopoko.Vstavka.Shell1';
const PROBE = 'io.github.xopoko.Vstavka.TestProbe';
const PATH = '/io/github/xopoko/Vstavka/';
const TEXT = 'text/plain;charset=utf-8';
const EDITOR_TITLE = 'Vstavka Test Editor';
const TERMINAL_TITLE = 'Vstavka Test Terminal';
const clipboardEvents = [];
let shortcutCount = 0;
const bus = Gio.DBus.session;
const encoder = new TextEncoder();
Gio._promisify(Gio.DBusConnection.prototype, 'call', 'call_finish');

function delay(ms) {
    return new Promise(resolve => GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
        resolve();
        return GLib.SOURCE_REMOVE;
    }));
}

async function until(description, predicate) {
    const end = GLib.get_monotonic_time() + 5 * GLib.USEC_PER_SEC;
    do {
        const result = predicate();
        if (result)
            return result;
        await delay(25);
    } while (GLib.get_monotonic_time() < end);
    throw new Error('Timed out waiting for ' + description);
}

function check(condition, description) {
    if (!condition)
        throw new Error(description);
    print('PASS ' + description);
}

function call(service, method, signature = null, args = []) {
    const object = service === SHELL ? 'Shell' : 'TestProbe';
    const result = bus.call_sync(service, PATH + object, service, method,
        signature ? new GLib.Variant(signature, args) : null, null, Gio.DBusCallFlags.NONE, 3000, null);
    return result.deepUnpack()[0];
}

function windowState(title) {
    return JSON.parse(call(PROBE, 'Window', '(s)', [title]));
}

async function pasteHoldingShift(whileHeld) {
    call(PROBE, 'Shift', '(b)', [true]);
    let pending;
    try {
        pending = bus.call(SHELL, PATH + 'Shell', SHELL, 'Paste', null, null,
            Gio.DBusCallFlags.NONE, 3000, null).then(result => result.deepUnpack()[0]);
        await until('editor refocused while Shift is held', () => windowState(EDITOR_TITLE)?.focused);
        await whileHeld(pending);
    } finally {
        call(PROBE, 'Shift', '(b)', [false]);
    }
    return pending;
}

function startPeer(executableVariable, mode) {
    return Gio.Subprocess.new([GLib.getenv(executableVariable), '-m', '/vstavka/tests/peer.js', mode],
        Gio.SubprocessFlags.NONE);
}

function waitPeer(process) {
    return new Promise((resolve, reject) => process.wait_check_async(null, (child, result) => {
        try {
            resolve(child.wait_check_finish(result));
        } catch (error) {
            reject(error);
        }
    }));
}

async function test(editor, entry) {
    const display = editor.get_display();
    check(display.constructor.name.includes('Wayland'), 'GTK client is native Wayland');
    const clipboard = display.get_clipboard();
    const subscribe = (name, handler) => bus.signal_subscribe(SHELL, SHELL, name, PATH + 'Shell', null,
        Gio.DBusSignalFlags.NONE, (_connection, _sender, _object, _iface, _member, args) => handler(args.deepUnpack()));
    subscribe('ClipboardChanged', args => clipboardEvents.push(args));
    subscribe('HotkeyPressed', () => shortcutCount++);
    call(SHELL, 'SubscribeClipboard');
    const denied = startPeer('VSTAVKA_UNLISTED_CLIENT', 'denied');
    const home = GLib.get_home_dir();
    await until('unlisted process subscribed to signal matches and received AccessDenied', () =>
        GLib.file_test(home + '/unlisted-ready', GLib.FileTest.EXISTS));

    await until('Wayland keyboard announced', () => display.get_default_seat().get_keyboard());
    await until('editor shown by Mutter', () => windowState(EDITOR_TITLE)?.visible);
    call(PROBE, 'Focus', '(s)', [EDITOR_TITLE]);
    await until('editor focused', () => windowState(EDITOR_TITLE)?.focused);
    entry.grab_focus();
    const copy = new Gtk.EventControllerKey({propagation_phase: Gtk.PropagationPhase.CAPTURE});
    let offer = null;
    let copies = 0;
    copy.connect('key-pressed', (_controller, keyval) => {
        if (keyval !== Gdk.KEY_F6)
            return false;
        clipboard.set_content(offer);
        copies++;
        return true;
    });
    editor.add_controller(copy);
    const sourceText = 'Clipboard from GTK – Zażółć';
    offer = Gdk.ContentProvider.new_for_bytes(TEXT, new GLib.Bytes(encoder.encode(sourceText)));
    call(PROBE, 'CopyKey');
    await until('GTK handled the copy key', () => copies > 0);
    const [mime, data, appId, displayName, icon] = await until('ClipboardChanged', () => clipboardEvents.shift());
    check(mime === TEXT && new TextDecoder().decode(data) === sourceText &&
        appId === 'io.github.xopoko.VstavkaTest.desktop' && displayName === EDITOR_TITLE,
    'ClipboardChanged carries GTK UTF-8 text and editor identity');
    check(icon.endsWith('/vstavka-test-editor.png'), 'ClipboardChanged carries a themed PNG icon path');

    const [, png] = GLib.file_get_contents(icon);
    for (const [format, payload] of [
        ['text/uri-list', encoder.encode('file:///tmp/a%20b\r\n')],
        ['image/png', png],
    ]) {
        offer = Gdk.ContentProvider.new_for_bytes(format, new GLib.Bytes(payload));
        call(PROBE, 'CopyKey');
        const [actualFormat, actualData] = await until(format, () => clipboardEvents.shift());
        check(actualFormat === format && actualData.length === payload.length &&
            actualData.every((byte, index) => byte === payload[index]), format + ' payload transferred byte for byte');
    }

    offer = Gdk.ContentProvider.new_union([
        Gdk.ContentProvider.new_for_bytes(TEXT, new GLib.Bytes(encoder.encode('private'))),
        Gdk.ContentProvider.new_for_bytes('application/x-vstavka-private', new GLib.Bytes([1])),
    ]);
    const before = copies;
    call(PROBE, 'CopyKey');
    await until('GTK applied private selection', () => copies > before);
    await delay(350);
    check(clipboardEvents.length === 0, 'private clipboard marker suppressed');

    call(SHELL, 'CheckAccess');
    check(call(SHELL, 'SetAccelerator', '(s)', ['<Control><Alt>v']), 'Ctrl+Alt+V registered');
    call(PROBE, 'Shortcut', '(b)', [false]);
    await until('HotkeyPressed', () => shortcutCount === 1);
    print('PASS Ctrl+Alt+V emits HotkeyPressed');
    check(call(SHELL, 'SetAccelerator', '(s)', ['<Shift><Control>F8']), 'custom Ctrl+Shift+F8 registered');
    for (const invalid of ['v', '<Shift>v', '<Shift><Shift>v', '<Control>F25', '<Control>v\n'])
        check(!call(SHELL, 'SetAccelerator', '(s)', [invalid]), 'invalid accelerator refused: ' + JSON.stringify(invalid));
    check(!call(SHELL, 'SetAccelerator', '(s)', ['<Alt>F4']), 'occupied Alt+F4 refused');
    call(PROBE, 'Shortcut', '(b)', [true]);
    await until('custom HotkeyPressed', () => shortcutCount === 2);
    print('PASS custom shortcut emits HotkeyPressed after refused replacements');
    GLib.file_set_contents(home + '/authorized-traffic', new Uint8Array([1]));
    await waitPeer(denied);

    check(!call(SHELL, 'Paste'), 'paste requires a captured target');
    call(SHELL, 'CaptureTarget');
    call(SHELL, 'PreparePanel');
    const panelApp = new Gtk.Application({application_id: 'io.github.xopoko.Vstavka'});
    panelApp.register(null);
    const panel = new Gtk.ApplicationWindow({application: panelApp, title: 'Vstavka',
        default_width: 420, default_height: 180, child: new Gtk.Label({label: 'Panel'})});
    // Exercise compositor mapping without GTK also issuing an activation token.
    panel.set_visible(true);
    await until('panel bottom aligned, above and sticky', () => {
        const state = windowState('Vstavka');
        return state?.visible && state.bottom === state.workBottom && state.above && state.sticky;
    });
    print('PASS panel bottom placement, above and sticky in Mutter');
    if (!windowState('Vstavka').focused)
        call(PROBE, 'Focus', '(s)', ['Vstavka']);
    await until('panel focused', () => windowState('Vstavka')?.focused);
    const value = 'Paste from Vstavka ✓';
    check(call(SHELL, 'SetClipboard', '(say)', [TEXT, encoder.encode(value)]), 'SetClipboard accepted UTF-8 text');
    await until('GTK received the extension selection', () => !clipboard.is_local() && clipboard.get_formats().contain_mime_type(TEXT));
    const actual = await new Promise((resolve, reject) => clipboard.read_text_async(null, (source, result) => {
        try {
            resolve(source.read_text_finish(result));
        } catch (error) {
            reject(error);
        }
    }));
    check(actual === value, 'GTK reads clipboard written by extension');
    check(clipboardEvents.length === 0, 'extension clipboard write does not echo');

    const editorKeys = new Gtk.EventControllerKey({propagation_phase: Gtk.PropagationPhase.CAPTURE});
    let pasteModifiers;
    editorKeys.connect('key-pressed', (_controller, keyval, _keycode, state) => {
        if (Gdk.keyval_to_lower(keyval) === Gdk.KEY_v)
            pasteModifiers = state;
        return false;
    });
    editor.add_controller(editorKeys);
    const sent = await pasteHoldingShift(async pending => {
        const completed = await Promise.race([pending.then(() => true), delay(80).then(() => false)]);
        check(!completed, 'Paste remains pending while Shift is held');
    });
    check(sent, 'Paste accepted when Shift is released');
    await until('pasted editor entry', () => entry.text === value);
    const editorModifiers = Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.SHIFT_MASK;
    check((pasteModifiers & editorModifiers) === Gdk.ModifierType.CONTROL_MASK,
        'Paste refocused the editor and sent Ctrl+V');

    pasteModifiers = undefined;
    call(SHELL, 'CaptureTarget');
    call(PROBE, 'Focus', '(s)', ['Vstavka']);
    await until('panel focused before clipboard replacement test', () => windowState('Vstavka')?.focused);
    const replacement = 'New clipboard contents from the editor';
    const pasteError = await pasteHoldingShift(async () => {
        offer = Gdk.ContentProvider.new_for_bytes(TEXT, new GLib.Bytes(encoder.encode(replacement)));
        call(PROBE, 'CopyKey');
        await until('extension observed the clipboard replacement', () =>
            clipboardEvents.some(([, bytes]) => new TextDecoder().decode(bytes) === replacement));
    }).then(() => null, error => {
        if (Gio.DBusError.get_remote_error(error) !== SHELL + '.ClipboardChanged')
            throw error;
        return error;
    });
    check(pasteError && pasteModifiers === undefined, 'Paste refused after clipboard replacement');

    const terminalApp = new Gtk.Application({application_id: 'io.github.xopoko.VstavkaTestTerminal'});
    terminalApp.register(null);
    const terminalEntry = new Gtk.Entry();
    const terminal = new Gtk.ApplicationWindow({application: terminalApp, title: TERMINAL_TITLE,
        default_width: 500, default_height: 220, child: terminalEntry});
    const keys = new Gtk.EventControllerKey({propagation_phase: Gtk.PropagationPhase.CAPTURE});
    const modifiers = Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.SHIFT_MASK;
    let terminalPasted = false;
    keys.connect('key-pressed', (_controller, keyval, _keycode, state) => {
        if (Gdk.keyval_to_lower(keyval) !== Gdk.KEY_v || (state & modifiers) !== modifiers)
            return false;
        terminalPasted = true;
        return true;
    });
    terminal.add_controller(keys);
    terminal.set_visible(true);
    await until('terminal shown by Mutter', () => windowState(TERMINAL_TITLE)?.visible);
    call(PROBE, 'Focus', '(s)', [TERMINAL_TITLE]);
    await until('terminal focused', () => windowState(TERMINAL_TITLE)?.focused);
    terminalEntry.grab_focus();
    call(SHELL, 'CaptureTarget');
    call(PROBE, 'Focus', '(s)', ['Vstavka']);
    await until('panel focused for terminal paste', () => windowState('Vstavka')?.focused);
    check(call(SHELL, 'SetClipboard', '(say)', [TEXT, encoder.encode(value)]), 'SetClipboard accepted terminal text');
    check(call(SHELL, 'Paste'), 'Paste accepted for the captured terminal');
    await until('terminal received Ctrl+Shift+V', () => terminalPasted);
    check(windowState(TERMINAL_TITLE).focused, 'Paste refocused the terminal and sent Ctrl+Shift+V');
    terminal.destroy();

    panel.destroy();
    await until('panel removed from Mutter', () => !windowState('Vstavka'));
    call(SHELL, 'ReleaseHotkey');
    call(SHELL, 'UnsubscribeClipboard');
    await waitPeer(startPeer('VSTAVKA_ALLOWED_CLIENT', 'busy'));
}

const app = new Gtk.Application({application_id: 'io.github.xopoko.VstavkaTest'});
let status = 1;
app.connect('activate', () => {
    const entry = new Gtk.Entry();
    const editor = new Gtk.ApplicationWindow({application: app, title: EDITOR_TITLE,
        default_width: 500, default_height: 220, child: entry});
    editor.present();
    test(editor, entry).then(() => { status = 0; }).catch(error => printerr(error.message + '\n' + error.stack))
        .finally(() => app.quit());
});
app.run([]);
System.exit(status);
