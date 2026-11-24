import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Shell from 'gi://Shell';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

const BUS = 'io.github.xopoko.Vstavka.TestProbe';
const XML = `<node><interface name="${BUS}">
    <method name="Window"><arg type="s" direction="in"/><arg type="s" direction="out"/></method>
    <method name="Focus"><arg type="s" direction="in"/></method>
    <method name="Shortcut"><arg type="b" direction="in"/></method>
    <method name="CopyKey"/>
    <method name="InitInput"/>
    <method name="Ready"><arg type="b" direction="out"/></method>
</interface></node>`;

export default class TestProbe extends Extension {
    enable() {
        this.object = Gio.DBusExportedObject.wrapJSObject(XML, this);
        this.object.export(Gio.DBus.session, '/io/github/xopoko/Vstavka/TestProbe');
        this.bus = Gio.bus_own_name_on_connection(Gio.DBus.session, BUS, Gio.BusNameOwnerFlags.NONE, null, null);
    }

    disable() {
        this.object.unexport();
        Gio.bus_unown_name(this.bus);
        this.keyboard = null;
    }

    findActor(title) {
        return global.get_window_actors().find(actor => actor.meta_window.get_title() === title);
    }

    Window(title) {
        const actor = this.findActor(title);
        if (!actor)
            return 'null';
        const window = actor.meta_window;
        const frame = window.get_frame_rect();
        const area = window.get_work_area_current_monitor();
        const app = Shell.WindowTracker.get_default().get_window_app(window);
        return JSON.stringify({
            id: app?.get_id(), visible: actor.is_visible(), focused: global.display.focus_window === window,
            bottom: frame.y + frame.height, workBottom: area.y + area.height,
            above: window.is_above(), sticky: window.is_on_all_workspaces(),
        });
    }

    Focus(title) {
        // Mutter can create a Wayland window actor before adding the window to the stack.
        const actor = this.findActor(title);
        if (actor?.is_visible())
            Main.activateWindow(actor.meta_window);
    }

    Ready() {
        return !Main.layoutManager._startingUp;
    }

    InitInput() {
        this.keyboard ??= Clutter.get_default_backend().get_default_seat()
            .create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);
    }

    Shortcut(custom) {
        this.sendKeys(custom
            ? [Clutter.KEY_Control_L, Clutter.KEY_Shift_L, Clutter.KEY_F8]
            : [Clutter.KEY_Control_L, Clutter.KEY_Alt_L, Clutter.KEY_v]);
    }

    CopyKey() {
        this.sendKeys([Clutter.KEY_F6]);
    }

    sendKeys(keys) {
        for (const key of keys)
            this.keyboard.notify_keyval(GLib.get_monotonic_time(), key, Clutter.KeyState.PRESSED);
        for (const key of keys.reverse())
            this.keyboard.notify_keyval(GLib.get_monotonic_time(), key, Clutter.KeyState.RELEASED);
    }
}
