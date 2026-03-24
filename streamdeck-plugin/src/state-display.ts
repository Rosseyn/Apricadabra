export class StateDisplay {
    private axes: Map<number, number> = new Map();
    private buttons: Map<number, boolean> = new Map();

    getAxis(id: number): number {
        return this.axes.get(id) ?? 0.5;
    }

    getButton(id: number): boolean {
        return this.buttons.get(id) ?? false;
    }

    getAxisPercent(id: number): number {
        return Math.round(this.getAxis(id) * 100);
    }

    getAxisDisplayString(id: number): string {
        return `${this.getAxisPercent(id)}%`;
    }

    update(axes: Record<string, number>, buttons: Record<string, boolean>): void {
        for (const [key, value] of Object.entries(axes)) {
            this.axes.set(Number(key), value);
        }
        for (const [key, value] of Object.entries(buttons)) {
            this.buttons.set(Number(key), value);
        }
    }

    getChangedAxes(axes: Record<string, number>): number[] {
        const changed: number[] = [];
        for (const [key, value] of Object.entries(axes)) {
            const id = Number(key);
            const old = this.axes.get(id);
            if (old === undefined || Math.abs(old - value) > 0.001) {
                changed.push(id);
            }
        }
        return changed;
    }
}
