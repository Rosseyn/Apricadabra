/**
 * Stitch-style axis telemetry bar renderer.
 * Generates SVG strings for Stream Deck encoder LCD feedback.
 *
 * Three modes:
 * - Hold: Grey→cyan intensity gradient, 90%+ warning hue
 * - Spring: Bipolar center-balanced, grey→yellow, 90%+ red warning
 * - Detent: Greyscale discrete steps
 */

const SEGMENT_COUNT = 10;
const BAR_WIDTH = 180;
const BAR_HEIGHT = 14;
const GAP = 2;
const SEG_WIDTH = (BAR_WIDTH - GAP * (SEGMENT_COUNT - 1)) / SEGMENT_COUNT;

// ─── Color ramps ──────────────────────────────────────────

/** Hold mode: grey → desaturated cyan → full cyan → bright cyan */
const HOLD_RAMP = [
    "#3a3a3a",   // 0-9%
    "#3a4a4c",   // 10-19%
    "#3e5558",   // 20-29%
    "#4a6d72",   // 30-39%
    "#0098a8",   // 40-49%
    "#00b8cc",   // 50-59%
    "#00d0e0",   // 60-69%
    "#00e8f5",   // 70-79%
    "#00F0FF",   // 80-89%
    "#5af4ff",   // 90-99% (before warning override)
];

const HOLD_WARNING_COLOR = "#F3E600";
const HOLD_EMPTY_COLOR = "rgba(255,255,255,0.06)";

/** Spring mode: grey → desaturated yellow → full yellow */
const SPRING_RAMP = [
    "#555540",   // near center
    "#6a6a30",   // slight offset
    "#7a7a20",   // moderate
    "#9a9a10",   // strong
    "#b8b800",   // high
    "#c8c800",   // very high
    "#d8d800",   // near max
    "#F3E600",   // max
];

const SPRING_WARNING_COLOR = "#d84020";
const SPRING_CENTER_COLOR = "#8a8a6a";
const SPRING_EMPTY_COLOR = "rgba(255,255,255,0.06)";

/** Detent mode: greyscale */
const DETENT_ACTIVE = "#e0e0e0";
const DETENT_INACTIVE = "#2a2a2a";

// ─── Value text color (follows the gradient) ──────────────

function holdValueColor(percent: number): string {
    if (percent <= 15) return "#8a8a8a";
    if (percent <= 35) return "#5a9ea8";
    if (percent <= 60) return "#00d0e0";
    return "#00F0FF";
}

function springValueColor(offsetPercent: number): string {
    const abs = Math.abs(offsetPercent);
    if (abs <= 10) return "#8a8a6a";
    if (abs <= 40) return "#b8b800";
    return "#F3E600";
}

// ─── SVG Renderers ────────────────────────────────────────

export interface BarRenderResult {
    svg: string;          // Full SVG as data URI
    valueText: string;    // e.g. "72%", "+40%", "3/5"
    valueColor: string;   // hex color for value text
    warningText: string;  // "MAX" or ""
    warningColor: string; // hex color for warning
    titleText: string;    // axis name
}

/**
 * Hold mode: unidirectional 0→100%.
 * @param value 0.0-1.0
 * @param axisName e.g. "THROTTLE_X"
 */
export function renderHoldBar(value: number, axisName: string): BarRenderResult {
    const percent = Math.round(value * 100);
    const filledCount = Math.round(value * SEGMENT_COUNT);
    const isWarning = percent >= 90;

    const segments: string[] = [];
    for (let i = 0; i < SEGMENT_COUNT; i++) {
        const x = i * (SEG_WIDTH + GAP);
        let color: string;

        if (i < filledCount) {
            // Last segment at 90%+ gets warning hue
            if (isWarning && i === SEGMENT_COUNT - 1 && i < filledCount) {
                color = HOLD_WARNING_COLOR;
            } else {
                color = HOLD_RAMP[Math.min(i, HOLD_RAMP.length - 1)];
            }
        } else {
            color = HOLD_EMPTY_COLOR;
        }

        segments.push(`<rect x="${x}" y="0" width="${SEG_WIDTH}" height="${BAR_HEIGHT}" fill="${color}" rx="0"/>`);
    }

    const svg = buildSvg(segments);

    return {
        svg: `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`,
        valueText: `${percent}%`,
        valueColor: holdValueColor(percent),
        warningText: isWarning ? "MAX" : "",
        warningColor: HOLD_WARNING_COLOR,
        titleText: axisName,
    };
}

/**
 * Spring mode: bipolar center-balanced.
 * @param value 0.0-1.0 (0.5 = center)
 * @param axisName e.g. "PITCH_BAL"
 */
export function renderSpringBar(value: number, axisName: string): BarRenderResult {
    const offsetPercent = Math.round((value - 0.5) * 200); // -100 to +100
    const absOffset = Math.abs(value - 0.5) * 2; // 0.0-1.0
    const isWarning = Math.abs(offsetPercent) >= 90;
    const centerIdx = Math.floor(SEGMENT_COUNT / 2); // 5 for 10 segments
    const offsetSegments = Math.round(absOffset * (SEGMENT_COUNT / 2));
    const isRight = value > 0.5;

    const segments: string[] = [];
    const centerX = centerIdx * (SEG_WIDTH + GAP);

    // Draw center marker (thin vertical line)
    segments.push(`<rect x="${centerX}" y="0" width="2" height="${BAR_HEIGHT}" fill="${SPRING_CENTER_COLOR}"/>`);

    for (let i = 0; i < SEGMENT_COUNT; i++) {
        if (i === centerIdx) continue; // skip center (drawn as marker)
        const x = i * (SEG_WIDTH + GAP) + (i >= centerIdx ? 2 + GAP : 0);

        const distFromCenter = Math.abs(i - centerIdx);
        const isOnActiveSide = isRight ? i > centerIdx : i < centerIdx;
        const isFilledSegment = isOnActiveSide && distFromCenter <= offsetSegments;

        let color: string;
        if (isFilledSegment) {
            const rampIdx = Math.min(distFromCenter - 1, SPRING_RAMP.length - 1);
            // Warning hue on the outermost segment at 90%+
            if (isWarning && distFromCenter === offsetSegments) {
                color = SPRING_WARNING_COLOR;
            } else {
                color = SPRING_RAMP[rampIdx];
            }
            // Full height
            segments.push(`<rect x="${x}" y="0" width="${SEG_WIDTH}" height="${BAR_HEIGHT}" fill="${color}"/>`);
        } else {
            // Empty: thin dim line
            const emptyY = BAR_HEIGHT / 2 - 2;
            segments.push(`<rect x="${x}" y="${emptyY}" width="${SEG_WIDTH}" height="4" fill="${SPRING_EMPTY_COLOR}"/>`);
        }
    }

    const sign = offsetPercent > 0 ? "+" : offsetPercent < 0 ? "" : "";
    return {
        svg: `data:image/svg+xml;charset=utf-8,${encodeURIComponent(buildSvg(segments))}`,
        valueText: `${sign}${offsetPercent}%`,
        valueColor: springValueColor(offsetPercent),
        warningText: isWarning ? "MAX" : "",
        warningColor: SPRING_WARNING_COLOR,
        titleText: axisName,
    };
}

/**
 * Detent mode: discrete steps.
 * @param value 0.0-1.0
 * @param steps total number of steps (2-20)
 * @param axisName e.g. "FLAPS"
 */
export function renderDetentBar(value: number, steps: number, axisName: string): BarRenderResult {
    const stepIdx = Math.round(value * (steps - 1)); // 0-based
    const segGap = 4;
    const segW = (BAR_WIDTH - segGap * (steps - 1)) / steps;

    const segments: string[] = [];
    for (let i = 0; i < steps; i++) {
        const x = i * (segW + segGap);
        const isActive = i === stepIdx;
        const color = isActive ? DETENT_ACTIVE : DETENT_INACTIVE;
        segments.push(`<rect x="${x}" y="0" width="${segW}" height="${BAR_HEIGHT}" fill="${color}"${isActive ? ` filter="url(#glow)"` : ""}/>`);
    }

    // Add glow filter for active step
    const defs = `<defs><filter id="glow"><feGaussianBlur stdDeviation="2" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>`;

    return {
        svg: `data:image/svg+xml;charset=utf-8,${encodeURIComponent(buildSvg(segments, defs))}`,
        valueText: `${stepIdx + 1}/${steps}`,
        valueColor: DETENT_ACTIVE,
        warningText: "",
        warningColor: "",
        titleText: axisName,
    };
}

function buildSvg(rects: string[], defs = ""): string {
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${BAR_WIDTH} ${BAR_HEIGHT}" width="${BAR_WIDTH}" height="${BAR_HEIGHT}">${defs}${rects.join("")}</svg>`;
}
