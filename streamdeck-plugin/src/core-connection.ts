import * as net from "net";
import * as dgram from "dgram";
import { spawn } from "child_process";
import { join } from "path";
import { existsSync } from "fs";

const PIPE_NAME = "\\\\.\\pipe\\apricadabra";
const UDP_COMMAND_PORT = 19871;
const UDP_BROADCAST_PORT = 19873; // Unique port for Stream Deck
const PROTOCOL_VERSION = 2;
const CORE_EXE_NAME = "apricadabra-core.exe";

type StateCallback = (axes: Record<string, number>, buttons: Record<string, boolean>) => void;
type StatusCallback = (status: string) => void;

export class CoreConnection {
    private pipe: net.Socket | null = null;
    private udpSender: dgram.Socket | null = null;
    private udpListener: dgram.Socket | null = null;
    private connected = false;
    private reconnecting = false;
    private coreStartTimeoutUntil = 0;
    private buffer = "";

    public onStateUpdate: StateCallback | null = null;
    public onStatusChange: StatusCallback | null = null;

    async connect(): Promise<void> {
        let delay = 100;
        while (true) {
            try {
                await this.tryConnect();
                return;
            } catch {
                this.tryLaunchCore();
                await this.sleep(delay);
                delay = Math.min(delay * 2, 5000);
            }
        }
    }

    private tryConnect(): Promise<void> {
        return new Promise((resolve, reject) => {
            const pipe = net.createConnection(PIPE_NAME);
            let resolved = false;

            pipe.on("connect", () => {
                this.pipe = pipe;
                const hello = JSON.stringify({
                    type: "hello",
                    version: PROTOCOL_VERSION,
                    name: "streamdeck",
                    broadcastPort: UDP_BROADCAST_PORT,
                    commands: ["axis", "button", "reset"],
                });
                pipe.write(hello + "\n");
            });

            pipe.on("data", (data) => {
                this.buffer += data.toString();
                const lines = this.buffer.split("\n");
                this.buffer = lines.pop() || "";

                for (const line of lines) {
                    if (!line.trim()) continue;
                    let msg;
                    try {
                        msg = JSON.parse(line);
                    } catch {
                        continue;
                    }

                    if (!resolved && msg.type === "welcome") {
                        resolved = true;
                        this.connected = true;
                        this.setupUdp();
                        this.onStatusChange?.("Connected");

                        // Parse v2 fields
                        if (msg.apiStatus) {
                            for (const [cmd, status] of Object.entries(msg.apiStatus)) {
                                if (status === "deprecated") {
                                    console.warn(`[Apricadabra] Command '${cmd}' is deprecated`);
                                } else if (status === "undefined") {
                                    console.error(`[Apricadabra] Command '${cmd}' is undefined — core may need upgrade`);
                                    // TODO: Could trigger core_upgrade flow here
                                }
                            }
                        }
                        if (msg.coreVersion) {
                            console.log(`[Apricadabra] Connected to core v${msg.coreVersion}`);
                        }

                        if (msg.axes || msg.buttons) {
                            this.onStateUpdate?.(msg.axes || {}, msg.buttons || {});
                        }
                        resolve();
                    } else if (msg.type === "heartbeat") {
                        pipe.write(JSON.stringify({ type: "heartbeat_ack" }) + "\n");
                    } else if (msg.type === "error") {
                        this.onStatusChange?.(`Error: ${msg.message}`);
                    } else if (msg.type === "shutdown") {
                        this.onStatusChange?.("Core shutting down");
                        this.disconnect();
                    } else if (msg.type === "core_restarting") {
                        const timeout = msg.coreStartTimeout || 15000;
                        console.log(`[Apricadabra] Core restarting, suppressing auto-launch for ${timeout}ms`);
                        this.coreStartTimeoutUntil = Date.now() + timeout;
                        this.onStatusChange?.("Core restarting...");
                    }
                }
            });

            pipe.on("error", (err) => {
                if (!resolved) reject(err);
                else this.handleDisconnect();
            });

            pipe.on("close", () => {
                if (!resolved) reject(new Error("Pipe closed"));
                else this.handleDisconnect();
            });

            setTimeout(() => {
                if (!resolved) {
                    pipe.destroy();
                    reject(new Error("Timeout"));
                }
            }, 2000);
        });
    }

    private setupUdp(): void {
        this.udpSender = dgram.createSocket("udp4");

        this.udpListener = dgram.createSocket({ type: "udp4", reuseAddr: true });
        this.udpListener.bind(UDP_BROADCAST_PORT, "127.0.0.1");
        this.udpListener.on("message", (data) => {
            try {
                const msg = JSON.parse(data.toString());
                if (msg.type === "state") {
                    this.onStateUpdate?.(msg.axes || {}, msg.buttons || {});
                }
            } catch {}
        });
    }

    send(message: Record<string, unknown>): void {
        if (!this.connected || !this.udpSender) return;
        const buf = Buffer.from(JSON.stringify(message));
        this.udpSender.send(buf, UDP_COMMAND_PORT, "127.0.0.1");
    }

    private handleDisconnect(): void {
        if (this.reconnecting) return;
        this.reconnecting = true;
        this.connected = false;
        this.onStatusChange?.("Disconnected");
        this.cleanup();
        setTimeout(() => {
            this.reconnecting = false;
            this.connect();
        }, 1000);
    }

    private disconnect(): void {
        this.connected = false;
        this.cleanup();
    }

    private cleanup(): void {
        this.pipe?.destroy();
        this.pipe = null;
        this.udpSender?.close();
        this.udpSender = null;
        this.udpListener?.close();
        this.udpListener = null;
    }

    private tryLaunchCore(): void {
        if (Date.now() < this.coreStartTimeoutUntil) {
            return; // Suppress auto-launch during core restart
        }
        const appData = process.env.APPDATA || "";
        const candidates = [
            join(appData, "Apricadabra", CORE_EXE_NAME),
            join(__dirname, CORE_EXE_NAME),
            join(__dirname, "..", CORE_EXE_NAME),
        ];
        for (const corePath of candidates) {
            if (existsSync(corePath)) {
                spawn(corePath, [], { detached: true, stdio: "ignore" }).unref();
                return;
            }
        }
    }

    private sleep(ms: number): Promise<void> {
        return new Promise((r) => setTimeout(r, ms));
    }
}
