import type { JsonValue } from "@elgato/utils";
import { SingletonAction, KeyDownEvent, KeyUpEvent } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";

interface ButtonSettings {
    [key: string]: JsonValue;
    button: string;
    mode: string;
    delay: number;
    rate: number;
    shortButton: string;
    longButton: string;
    threshold: number;
}

export class ButtonAction extends SingletonAction<ButtonSettings> {
    private core: CoreConnection;

    constructor(connection: CoreConnection) {
        super();
        this.core = connection;
    }

    override onKeyDown(ev: KeyDownEvent<ButtonSettings>): void {
        const s = ev.payload.settings;
        if (!s.button || !s.mode) return;
        const button = Number(s.button);

        switch (s.mode) {
            case "momentary":
                this.core.send({ type: "button", button, mode: "momentary", state: "down" });
                break;
            case "toggle":
                this.core.send({ type: "button", button, mode: "toggle", state: "down" });
                break;
            case "pulse":
                this.core.send({ type: "button", button, mode: "pulse" });
                break;
            case "double":
                this.core.send({ type: "button", button, mode: "double", delay: s.delay || 50 });
                break;
            case "rapid":
                this.core.send({ type: "button", button, mode: "rapid", state: "down", rate: s.rate || 100 });
                break;
            case "longshort":
                this.core.send({
                    type: "button",
                    button,
                    mode: "longshort",
                    state: "down",
                    shortButton: Number(s.shortButton || button),
                    longButton: Number(s.longButton || button),
                    threshold: s.threshold || 500,
                });
                break;
        }
    }

    override onKeyUp(ev: KeyUpEvent<ButtonSettings>): void {
        const s = ev.payload.settings;
        if (!s.button || !s.mode) return;
        const button = Number(s.button);

        switch (s.mode) {
            case "momentary":
                this.core.send({ type: "button", button, mode: "momentary", state: "up" });
                break;
            case "rapid":
                this.core.send({ type: "button", button, mode: "rapid", state: "up" });
                break;
            case "longshort":
                this.core.send({
                    type: "button",
                    button,
                    mode: "longshort",
                    state: "up",
                    shortButton: Number(s.shortButton || button),
                    longButton: Number(s.longButton || button),
                    threshold: s.threshold || 500,
                });
                break;
        }
    }
}
