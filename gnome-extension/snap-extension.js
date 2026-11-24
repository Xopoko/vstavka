import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import {VstavkaExtension} from './extension-core.js';
import {SnapPeer} from './snap-peer.js';

export default class VstavkaSnapExtension extends VstavkaExtension {
    enable() {
        if (Meta.is_wayland_compositor?.() ?? GLib.getenv('XDG_SESSION_TYPE') !== 'x11')
            super.enable();
    }

    startTransport(exportObject, revoke) {
        return new SnapPeer(exportObject, revoke);
    }
}
