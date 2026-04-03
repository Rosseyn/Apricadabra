import type { JsonValue } from "@elgato/utils";
import { SingletonAction, DialRotateEvent, DialDownEvent, WillAppearEvent, WillDisappearEvent, DidReceiveSettingsEvent, Action } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";
import { StateDisplay } from "../state-display";
import { renderHoldBar, renderSpringBar, renderDetentBar, type BarRenderResult } from "../axis-bar-renderer";

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
    private core: CoreConnection;
    private stateDisplay: StateDisplay;
    private settingsCache = new Map<string, DialSettings>();
    private layoutSet = new Set<string>();

    constructor(connection: CoreConnection, stateDisplay: StateDisplay) {
        super();
        this.core = connection;
        this.stateDisplay = stateDisplay;
    }

    override onDialRotate(ev: DialRotateEvent<DialSettings>): void {
        try {
            const s = ev.payload.settings;
            if (!s || !s.axis || !s.mode) return;

            let diff = ev.payload.ticks;
            if (s.invert) diff = -diff;

            if (s.mode === "detent") {
                diff = Math.sign(diff);
            }

            const msg: Record<string, unknown> = {
                type: "axis",
                axis: Number(s.axis),
                mode: s.mode,
                diff,
            };

            if (s.mode !== "detent") {
                msg.sensitivity = (s.sensitivity || 20) / 1000;
            }
            if (s.mode === "spring") {
                msg.decayRate = (s.decayRate || 95) / 100;
            }
            if (s.mode === "detent") {
                msg.steps = s.steps || 5;
            }

            this.core.send(msg);
        } catch (err) {
            console.error("[Apricadabra] onDialRotate error:", err);
        }
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
        this.setCustomLayout(ev.action);
        this.updateFeedback(ev.action, ev.payload.settings);
    }

    override onWillDisappear(ev: WillDisappearEvent<DialSettings>): void {
        this.settingsCache.delete(ev.action.id);
        this.layoutSet.delete(ev.action.id);
    }

    override onDidReceiveSettings(ev: DidReceiveSettingsEvent<DialSettings>): void {
        this.settingsCache.set(ev.action.id, ev.payload.settings);
        this.setCustomLayout(ev.action);
        this.updateFeedback(ev.action, ev.payload.settings);
    }

    private setCustomLayout(action: Action<DialSettings>): void {
        if (this.layoutSet.has(action.id)) return;
        if (!action.isDial()) return;
        try {
            action.setFeedbackLayout("layouts/axis-telemetry.json");
            this.layoutSet.add(action.id);
        } catch { /* layout may not be supported on this device */ }
    }

    updateFeedback(action: Action<DialSettings>, settings: DialSettings): void {
        if (!settings.axis || !action.isDial()) return;
        const axisId = Number(settings.axis);
        const value = this.stateDisplay.getAxisValue(axisId);
        const name = AXIS_NAMES[settings.axis] || `Axis ${settings.axis}`;
        const mode = settings.mode || "hold";
        const steps = settings.steps || 5;

        let result: BarRenderResult;
        switch (mode) {
            case "spring":
                result = renderSpringBar(value, name);
                break;
            case "detent":
                result = renderDetentBar(value, steps, name);
                break;
            default:
                result = renderHoldBar(value, name);
                break;
        }

        try {
            const feedback: Record<string, unknown> = {
                title: result.titleText,
                value: { value: result.valueText, color: result.valueColor },
                bar: result.svg,
            };

            if (result.warningText) {
                feedback.warning = { value: result.warningText, color: result.warningColor, enabled: true };
            } else {
                feedback.warning = { value: "", enabled: false };
            }

            action.setFeedback(feedback);
        } catch (err) {
            console.error("[Apricadabra] setFeedback error:", err);
        }
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
