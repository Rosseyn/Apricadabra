import type { JsonValue } from "@elgato/utils";
import { SingletonAction, DialRotateEvent, DialDownEvent, WillAppearEvent, WillDisappearEvent, DidReceiveSettingsEvent, Action } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";
import { StateDisplay } from "../state-display";

const AXIS_NAMES: Record<string, string> = {
    "1": "X", "2": "Y", "3": "Z", "4": "Rx",
    "5": "Ry", "6": "Rz", "7": "Slider 1", "8": "Slider 2",
};

interface DialSettings {
    [key: string]: JsonValue;
    axis: string;
    mode: string;
    sensitivity: number;
    invert: boolean;
    decayRate: number;
    steps: number;
    encoderButton: string;
}

export class DialAction extends SingletonAction<DialSettings> {
    private connection: CoreConnection;
    private stateDisplay: StateDisplay;
    private settingsCache = new Map<string, DialSettings>();

    constructor(connection: CoreConnection, stateDisplay: StateDisplay) {
        super();
        this.core = connection;
        this.stateDisplay = stateDisplay;
    }

    override onDialRotate(ev: DialRotateEvent<DialSettings>): void {
        const { axis, mode, sensitivity, invert, decayRate, steps } = ev.payload.settings;
        if (!axis || !mode) return;

        let diff = ev.payload.ticks;
        if (invert) diff = -diff;

        if (mode === "detent") {
            diff = Math.sign(diff);
        }

        const msg: Record<string, unknown> = {
            type: "axis",
            axis: Number(axis),
            mode,
            diff,
        };

        if (mode !== "detent") {
            msg.sensitivity = (sensitivity || 20) / 1000;
        }
        if (mode === "spring") {
            msg.decayRate = (decayRate || 95) / 100;
        }
        if (mode === "detent") {
            msg.steps = steps || 5;
        }

        this.core.send(msg);
    }

    override onDialDown(ev: DialDownEvent<DialSettings>): void {
        const { encoderButton } = ev.payload.settings;
        if (!encoderButton || encoderButton === "none") return;

        this.core.send({
            type: "button",
            button: Number(encoderButton),
            mode: "pulse",
        });
    }

    override onWillAppear(ev: WillAppearEvent<DialSettings>): void {
        this.settingsCache.set(ev.action.id, ev.payload.settings);
        this.updateFeedback(ev.action, ev.payload.settings);
    }

    override onWillDisappear(ev: WillDisappearEvent<DialSettings>): void {
        this.settingsCache.delete(ev.action.id);
    }

    override onDidReceiveSettings(ev: DidReceiveSettingsEvent<DialSettings>): void {
        this.settingsCache.set(ev.action.id, ev.payload.settings);
        this.updateFeedback(ev.action, ev.payload.settings);
    }

    updateFeedback(action: Action<DialSettings>, settings: DialSettings): void {
        if (!settings.axis || !action.isDial()) return;
        const axisId = Number(settings.axis);
        const percent = this.stateDisplay.getAxisPercent(axisId);
        const name = AXIS_NAMES[settings.axis] || `Axis ${settings.axis}`;
        action.setFeedback({
            title: name,
            value: `${percent}%`,
            indicator: percent,
        });
    }

    updateFeedbackForAxes(changedAxes: number[]): void {
        if (changedAxes.length === 0) return;
        const changedSet = new Set(changedAxes);
        for (const action of this.actions) {
            const settings = this.settingsCache.get(action.id);
            if (!settings || !settings.axis) continue;
            if (!changedSet.has(Number(settings.axis))) continue;
            this.updateFeedback(action, settings);
        }
    }
}
