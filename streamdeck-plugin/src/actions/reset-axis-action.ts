import type { JsonValue } from "@elgato/utils";
import { SingletonAction, KeyDownEvent, DialDownEvent } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";

interface ResetSettings {
    [key: string]: JsonValue;
    axis: string;
    position: number;
}

export class ResetAxisAction extends SingletonAction<ResetSettings> {
    private core: CoreConnection;

    constructor(connection: CoreConnection) {
        super();
        this.core = connection;
    }

    override onKeyDown(ev: KeyDownEvent<ResetSettings>): void {
        this.doReset(ev.payload.settings);
    }

    override onDialDown(ev: DialDownEvent<ResetSettings>): void {
        this.doReset(ev.payload.settings);
    }

    private doReset(settings: ResetSettings): void {
        if (!settings.axis) return;
        const position = (settings.position ?? 50) / 100;
        this.core.send({
            type: "reset",
            axis: Number(settings.axis),
            position,
        });
    }
}
