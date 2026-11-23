import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import System from 'system';

const BUS = 'io.github.xopoko.Vstavka.Shell1';
const PATH = '/io/github/xopoko/Vstavka/Shell';
const connection = Gio.DBus.session;
const mode = ARGV[0];

function call(method, signature = null, args = []) {
    return connection.call_sync(BUS, PATH, BUS, method, signature ? new GLib.Variant(signature, args) : null,
        null, Gio.DBusCallFlags.NONE, 3000, null);
}

function expectError(suffix, ...request) {
    try {
        call(...request);
    } catch (error) {
        if (Gio.DBusError.get_remote_error(error) === BUS + '.' + suffix)
            return;
        throw error;
    }
    throw new Error(request[0] + ' unexpectedly succeeded');
}

if (mode === 'blank') {
    expectError('AccessDenied', 'SubscribeClipboard');
    print('PASS empty allowlist denies the subscriber');
} else if (mode === 'busy') {
    expectError('ClipboardBusy', 'SubscribeClipboard');
    print('PASS a second allowed connection cannot take the subscription');
} else if (mode === 'acquire') {
    call('SubscribeClipboard');
    print('PASS the clipboard slot is released when its owner disconnects');
} else if (mode === 'denied') {
    let signals = 0;
    for (const name of ['ClipboardChanged', 'HotkeyPressed']) {
        connection.signal_subscribe(BUS, BUS, name, PATH, null, Gio.DBusSignalFlags.NONE, () => signals++);
    }
    for (const request of [
        ['SubscribeClipboard'], ['UnsubscribeClipboard'],
        ['SetClipboard', '(say)', ['text/plain;charset=utf-8', new Uint8Array([65])]],
        ['SetAccelerator', '(s)', ['<Alt>v']], ['ReleaseHotkey'],
        ['CaptureTarget'], ['Paste'], ['PreparePanel'],
    ]) {
        expectError('AccessDenied', ...request);
    }
    print('PASS unlisted executable is denied for all eight D-Bus methods');
    const home = GLib.get_home_dir();
    GLib.file_set_contents(home + '/unlisted-ready', new Uint8Array([1]));
    const loop = new GLib.MainLoop(null, false);
    const deadline = GLib.get_monotonic_time() + 15 * GLib.USEC_PER_SEC;
    let sawTraffic = false;
    GLib.timeout_add(GLib.PRIORITY_DEFAULT, 50, () => {
        if (GLib.file_test(home + '/authorized-traffic', GLib.FileTest.EXISTS)) {
            sawTraffic = true;
            loop.quit();
            return GLib.SOURCE_REMOVE;
        }
        if (GLib.get_monotonic_time() >= deadline) {
            loop.quit();
            return GLib.SOURCE_REMOVE;
        }
        return GLib.SOURCE_CONTINUE;
    });
    loop.run();
    if (!sawTraffic || signals !== 0)
        throw new Error('Unauthorized signal check did not observe the expected traffic window');
    print('PASS unlisted executable receives no clipboard or hotkey signals');
} else {
    System.exit(2);
}
