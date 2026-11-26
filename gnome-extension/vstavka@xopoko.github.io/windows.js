import Clutter from 'gi://Clutter';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';

const APP_ID = 'io.github.xopoko.vstavka';
const BLOCKING_MODIFIERS = Clutter.ModifierType.SHIFT_MASK | Clutter.ModifierType.CONTROL_MASK |
    Clutter.ModifierType.MOD1_MASK | Clutter.ModifierType.MOD4_MASK | Clutter.ModifierType.MOD5_MASK |
    Clutter.ModifierType.SUPER_MASK | Clutter.ModifierType.META_MASK;
const modifiersHeld = () => (global.get_pointer()[2] & BLOCKING_MODIFIERS) !== 0;
const isApp = window => {
    if (!window)
        return false;
    const app = Shell.WindowTracker.get_default().get_window_app(window);
    return [window.get_wm_class(), window.get_wm_class_instance(), window.get_gtk_application_id(), app?.get_id()]
        .some(id => id?.toLowerCase().replace(/\.desktop$/, '') === APP_ID);
};
const isTerminal = window => {
    const app = Shell.WindowTracker.get_default().get_window_app(window);
    return app?.get_app_info()?.get_categories()?.split(';').includes('TerminalEmulator') ?? false;
};

export class Windows {
    constructor() {
        this.lastFocused = null;
        this.target = null;
        this.monitor = null;
        this.panels = new Map();
        this.disposed = false;
        this.keyboard = null;
        this.pasteWait = null;
        this.focusSignal = global.display.connect('notify::focus-window', () => this.rememberFocus());
        this.mapSignal = global.window_manager.connect('map', (_wm, actor) => this.map(actor.meta_window));
        this.destroySignal = global.window_manager.connect('destroy', (_wm, actor) => this.unmap(actor.meta_window));
        this.rememberFocus();
    }

    dispose() {
        this.disposed = true;
        global.display.disconnect(this.focusSignal);
        global.window_manager.disconnect(this.mapSignal);
        global.window_manager.disconnect(this.destroySignal);
        for (const [window, panel] of this.panels) {
            this.unmap(window);
            if (panel.sizeSignal && window.get_compositor_private()) {
                if (!panel.wasAbove)
                    window.unmake_above();
                if (!panel.wasSticky)
                    window.unstick();
            }
        }
        if (this.pasteWait)
            this.pasteWait(false);
        this.keyboard = null;
        this.lastFocused = null;
        this.target = null;
    }

    rememberFocus() {
        const window = global.display.focus_window;
        if (window && !isApp(window))
            this.lastFocused = window;
    }

    captureTarget() {
        this.rememberFocus();
        this.target = this.lastFocused;
    }

    preparePanel() {
        this.monitor = global.display.get_current_monitor();
        for (const [window, panel] of this.panels) {
            panel.monitor = this.monitor;
            if (panel.sizeSignal)
                this.position(window, panel);
        }
    }

    map(window) {
        if (!window || window.get_title() !== 'Vstavka' || !isApp(window))
            return;
        const panel = {monitor: this.monitor ?? global.display.get_current_monitor()};
        this.monitor = null;
        this.unmap(window);
        this.panels.set(window, panel);
        // The map signal can precede insertion into Mutter's window stack.
        panel.idle = GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => {
            panel.idle = null;
            if (window.get_compositor_private()) {
                panel.wasAbove = window.is_above();
                panel.wasSticky = window.is_on_all_workspaces();
                window.make_above();
                window.stick();
                panel.sizeSignal = window.connect('size-changed', () => this.position(window, panel));
                this.position(window, panel);
            }
            return GLib.SOURCE_REMOVE;
        });
    }

    unmap(window) {
        const panel = this.panels.get(window);
        if (!panel)
            return;
        this.panels.delete(window);
        if (panel.idle)
            GLib.source_remove(panel.idle);
        if (panel.sizeSignal)
            window.disconnect(panel.sizeSignal);
    }

    position(window, panel) {
        const monitor = Main.layoutManager.monitors[panel.monitor] ?
            panel.monitor : global.display.get_current_monitor();
        const area = window.get_work_area_for_monitor(monitor);
        const frame = window.get_frame_rect();
        const x = area.x + Math.max(0, Math.floor((area.width - frame.width) / 2));
        const y = area.y + Math.max(0, area.height - frame.height);
        if (frame.x !== x || frame.y !== y)
            window.move_frame(false, x, y);
    }

    async paste(allowed) {
        const target = this.target;
        this.target = null;
        if (this.pasteWait || Main.actionMode !== Shell.ActionMode.NORMAL)
            return false;
        if (!target?.get_compositor_private() || isApp(target))
            return false;

        target.activate(global.get_current_time());
        const ready = await this.waitForPaste(target);
        if (this.disposed || !ready || !allowed() || global.display.focus_window !== target ||
            Main.actionMode !== Shell.ActionMode.NORMAL || modifiersHeld())
            return false;

        this.keyboard ??= Clutter.get_default_backend().get_default_seat()
            ?.create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);
        if (!this.keyboard)
            return false;
        const terminal = isTerminal(target);
        const send = (key, state) => this.keyboard.notify_keyval(GLib.get_monotonic_time(), key, state);
        send(Clutter.KEY_Control_L, Clutter.KeyState.PRESSED);
        if (terminal)
            send(Clutter.KEY_Shift_L, Clutter.KeyState.PRESSED);
        send(Clutter.KEY_v, Clutter.KeyState.PRESSED);
        send(Clutter.KEY_v, Clutter.KeyState.RELEASED);
        if (terminal)
            send(Clutter.KEY_Shift_L, Clutter.KeyState.RELEASED);
        send(Clutter.KEY_Control_L, Clutter.KeyState.RELEASED);
        return true;
    }

    waitForPaste(target) {
        let reachedTarget = global.display.focus_window === target;
        if (reachedTarget && !modifiersHeld())
            return Promise.resolve(true);
        // Shift+Enter can close the panel while Shift is still held.
        const started = GLib.get_monotonic_time();
        return new Promise(resolve => {
            const timeout = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 20, () => {
                const focused = global.display.focus_window === target;
                const elapsed = GLib.get_monotonic_time() - started;
                const ready = focused && !modifiersHeld();
                if (ready || elapsed >= 750_000 || (!focused && (reachedTarget || elapsed >= 300_000))) {
                    this.pasteWait(ready);
                    return GLib.SOURCE_REMOVE;
                }
                reachedTarget ||= focused;
                return GLib.SOURCE_CONTINUE;
            });
            this.pasteWait = success => {
                GLib.source_remove(timeout);
                this.pasteWait = null;
                resolve(success);
            };
        });
    }
}
