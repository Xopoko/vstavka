import Gio from 'gi://Gio';
import {AuthorizedClients} from './authorization.js';
import {BUS, PATH, VstavkaExtension} from './extension-core.js';

export default class VstavkaStandaloneExtension extends VstavkaExtension {
    startTransport(exportObject, revoke) {
        const authorization = new AuthorizedClients(this.getSettings(), revoke);
        let object;
        try {
            object = exportObject(Gio.DBus.session);
            const bus = Gio.bus_own_name_on_connection(Gio.DBus.session, BUS,
                Gio.BusNameOwnerFlags.DO_NOT_QUEUE, null, null);
            return {
                async authorize(invocation) {
                    const sender = invocation.get_sender();
                    return await authorization.authorize(sender) ? sender : null;
                },
                isAllowed: sender => authorization.isAllowed(sender),
                emitSignal: (sender, member, value) => Gio.DBus.session.emit_signal(sender, PATH, BUS, member, value),
                dispose() {
                    Gio.bus_unown_name(bus);
                    object.unexport();
                    authorization.dispose();
                },
            };
        } catch (error) {
            object?.unexport();
            authorization.dispose();
            throw error;
        }
    }
}
