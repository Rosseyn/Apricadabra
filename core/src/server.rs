use crate::axis::AxisManager;
use crate::button::ButtonManager;
use crate::config::Config;
use crate::protocol::*;
use crate::vjoy::{Axis, VirtualJoystick};

use std::collections::HashMap;
use std::sync::Arc;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::{NamedPipeServer, ServerOptions};
use tokio::sync::{broadcast, mpsc, Mutex};
use tokio::time::{self, Duration, Instant};
use tracing::{error, info, warn};

// Pipe name is configurable via Config.pipe_name (default: \\.\pipe\apricadabra)
// This allows integration tests to use a unique pipe name.
const PROTOCOL_VERSION: u32 = 1;
const HEARTBEAT_INTERVAL: Duration = Duration::from_secs(3);
const HEARTBEAT_TIMEOUT: Duration = Duration::from_secs(9); // 3 missed beats
const TICK_INTERVAL: Duration = Duration::from_millis(16); // ~60Hz

/// Inbound message from a client with client ID.
struct ClientInput {
    client_id: u64,
    message: ClientMessage,
}

/// Core server state.
pub struct Server {
    config: Config,
    joystick: Box<dyn VirtualJoystick>,
}

impl Server {
    pub fn new(config: Config, joystick: Box<dyn VirtualJoystick>) -> Self {
        Self { config, joystick }
    }

    pub async fn run(mut self, mut shutdown_rx: tokio::sync::watch::Receiver<bool>) -> anyhow::Result<()> {
        // Acquire vJoy device
        if let Err(e) = self.joystick.acquire(self.config.vjoy_device_id) {
            error!("Failed to acquire vJoy device {}: {e}", self.config.vjoy_device_id);
            return Err(e);
        }
        info!("Acquired vJoy device {}", self.config.vjoy_device_id);

        let axis_mgr = Arc::new(Mutex::new(AxisManager::new()));
        let button_mgr = Arc::new(Mutex::new(ButtonManager::new()));
        let joystick = Arc::new(Mutex::new(self.joystick));

        // Channel for client messages -> main loop
        let (input_tx, mut input_rx) = mpsc::channel::<ClientInput>(256);

        // Broadcast channel for server -> all clients
        let (broadcast_tx, _) = broadcast::channel::<String>(256);

        let mut client_counter: u64 = 0;
        let connected_clients = Arc::new(std::sync::atomic::AtomicU64::new(0));

        // Channel for client disconnect notifications
        let (disconnect_tx, mut disconnect_rx) = mpsc::channel::<u64>(32);

        // Spawn pipe accept loop
        let accept_input_tx = input_tx.clone();
        let accept_broadcast_tx = broadcast_tx.clone();
        let accept_axis = axis_mgr.clone();
        let accept_button = button_mgr.clone();
        let accept_clients = connected_clients.clone();
        let accept_disconnect_tx = disconnect_tx.clone();
        let pipe_name = self.config.pipe_name.clone();

        tokio::spawn(async move {
            loop {
                let pipe = match ServerOptions::new()
                    .first_pipe_instance(client_counter == 0)
                    .create(&pipe_name)
                {
                    Ok(pipe) => pipe,
                    Err(e) => {
                        error!("Failed to create named pipe: {e}");
                        time::sleep(Duration::from_secs(1)).await;
                        continue;
                    }
                };

                if let Err(e) = pipe.connect().await {
                    error!("Pipe connect error: {e}");
                    continue;
                }

                client_counter += 1;
                let client_id = client_counter;
                info!("Client {client_id} connected");

                accept_clients.fetch_add(1, std::sync::atomic::Ordering::Relaxed);

                let tx = accept_input_tx.clone();
                let broadcast_rx = accept_broadcast_tx.subscribe();
                let axis = accept_axis.clone();
                let button = accept_button.clone();
                let clients = accept_clients.clone();
                let disc_tx = accept_disconnect_tx.clone();

                tokio::spawn(async move {
                    Self::handle_client(client_id, pipe, tx, broadcast_rx, axis, button).await;
                    clients.fetch_sub(1, std::sync::atomic::Ordering::Relaxed);
                    let _ = disc_tx.send(client_id).await;
                    info!("Client {client_id} disconnected");
                });
            }
        });

        // Main tick loop: process inputs, update state, broadcast, decay
        let mut tick_interval = time::interval(TICK_INTERVAL);

        loop {
            tokio::select! {
                _ = tick_interval.tick() => {
                    let mut axes = axis_mgr.lock().await;
                    let mut buttons = button_mgr.lock().await;

                    // Process spring decay
                    axes.tick_spring_decay();

                    // Process disconnect decay if active
                    axes.tick_disconnect_decay();

                    // Process pending button actions (pulse releases, double press timing)
                    buttons.process_pending();

                    // Collect changes and broadcast
                    let axis_changes = axes.take_changed();
                    let button_changes = buttons.take_changed();

                    if !axis_changes.is_empty() || !button_changes.is_empty() {
                        // Update vJoy
                        let mut joy = joystick.lock().await;
                        for (&id, &value) in &axis_changes {
                            if let Some(axis) = Axis::from_id(id) {
                                let _ = joy.set_axis(axis, value);
                            }
                        }
                        for (&id, &pressed) in &button_changes {
                            let _ = joy.set_button(id, pressed);
                        }

                        // Broadcast state to plugins
                        let msg = ServerMessage::State {
                            axes: axis_changes,
                            buttons: button_changes,
                        };
                        if let Ok(json) = serde_json::to_string(&msg) {
                            let _ = broadcast_tx.send(json);
                        }
                    }
                }

                Some(input) = input_rx.recv() => {
                    match input.message {
                        ClientMessage::Axis { axis, mode, diff, sensitivity, decay_rate, steps } => {
                            let sens = sensitivity.unwrap_or(self.config.default_sensitivity);
                            let mut axes = axis_mgr.lock().await;
                            match mode {
                                AxisMode::Hold => axes.apply_hold(axis, diff, sens),
                                AxisMode::Spring => {
                                    let dr = decay_rate.unwrap_or(self.config.default_decay_rate);
                                    axes.apply_spring(axis, diff, sens, dr);
                                }
                                AxisMode::Detent => {
                                    axes.apply_detent(axis, diff, steps.unwrap_or(5));
                                }
                            }
                        }
                        ClientMessage::Button { button, mode, state, delay, rate, short_button, long_button, threshold } => {
                            let mut buttons = button_mgr.lock().await;
                            match mode {
                                ButtonMode::Momentary => match state {
                                    Some(ButtonState::Down) => buttons.momentary_down(button),
                                    Some(ButtonState::Up) => buttons.momentary_up(button),
                                    None => {}
                                },
                                ButtonMode::Toggle => {
                                    if matches!(state, Some(ButtonState::Down)) {
                                        buttons.toggle(button);
                                    }
                                }
                                ButtonMode::Pulse => buttons.pulse(button),
                                ButtonMode::Double => buttons.double_press(button, delay.unwrap_or(50)),
                                ButtonMode::Rapid => match state {
                                    Some(ButtonState::Down) => buttons.rapid_start(button, rate.unwrap_or(100)),
                                    Some(ButtonState::Up) => buttons.rapid_stop(button),
                                    None => {}
                                },
                                ButtonMode::LongShort => {
                                    let sb = short_button.unwrap_or(button);
                                    let lb = long_button.unwrap_or(button);
                                    let th = threshold.unwrap_or(500);
                                    match state {
                                        Some(ButtonState::Down) => buttons.longshort_down(sb, lb, th),
                                        Some(ButtonState::Up) => { buttons.longshort_up(sb, lb, th); }
                                        None => {}
                                    }
                                }
                            }
                        }
                        ClientMessage::Reset { axis, position } => {
                            axis_mgr.lock().await.reset(axis, position);
                        }
                        ClientMessage::Hello { .. } | ClientMessage::HeartbeatAck => {
                            // Handled in client handler
                        }
                    }
                }

                Some(client_id) = disconnect_rx.recv() => {
                    info!("Client {client_id} cleanup");
                    if connected_clients.load(std::sync::atomic::Ordering::Relaxed) == 0 {
                        info!("All clients disconnected, starting decay");
                        axis_mgr.lock().await.start_disconnect_decay();
                    }
                }

                _ = shutdown_rx.changed() => {
                    if *shutdown_rx.borrow() {
                        info!("Broadcasting shutdown to all clients");
                        if let Ok(json) = serde_json::to_string(&ServerMessage::Shutdown) {
                            let _ = broadcast_tx.send(json);
                        }
                        // Give clients a moment to receive the message
                        time::sleep(Duration::from_millis(100)).await;
                        // Release vJoy
                        let _ = joystick.lock().await.release();
                        break;
                    }
                }
            }
        }

        Ok(())
    }

    async fn handle_client(
        client_id: u64,
        pipe: NamedPipeServer,
        input_tx: mpsc::Sender<ClientInput>,
        mut broadcast_rx: broadcast::Receiver<String>,
        axis_mgr: Arc<Mutex<AxisManager>>,
        button_mgr: Arc<Mutex<ButtonManager>>,
    ) {
        let (reader, mut writer) = tokio::io::split(pipe);
        let mut reader = BufReader::new(reader);
        let mut line = String::new();

        // Wait for hello
        line.clear();
        match reader.read_line(&mut line).await {
            Ok(0) | Err(_) => return,
            Ok(_) => {}
        }

        let hello: ClientMessage = match serde_json::from_str(line.trim()) {
            Ok(msg) => msg,
            Err(_) => return,
        };

        match hello {
            ClientMessage::Hello { version, name } => {
                info!("Client {client_id} hello: {name} v{version}");
                if version != PROTOCOL_VERSION {
                    let err = ServerMessage::Error {
                        code: "unsupported_version".to_string(),
                        message: format!("Server supports protocol v{PROTOCOL_VERSION}, client sent v{version}"),
                    };
                    let _ = Self::send_message(&mut writer, &err).await;
                    return;
                }

                // Send welcome with current state
                let axes = axis_mgr.lock().await.get_all();
                let buttons = button_mgr.lock().await.get_all();
                let welcome = ServerMessage::Welcome { version: PROTOCOL_VERSION, axes, buttons };
                if Self::send_message(&mut writer, &welcome).await.is_err() {
                    return;
                }
            }
            _ => return, // First message must be hello
        }

        line.clear(); // Clear hello message before entering read loop

        let mut heartbeat_interval = time::interval(HEARTBEAT_INTERVAL);
        let mut last_ack = Instant::now();

        loop {
            tokio::select! {
                result = reader.read_line(&mut line) => {
                    match result {
                        Ok(0) | Err(_) => break, // Client disconnected
                        Ok(_) => {
                            if let Ok(msg) = serde_json::from_str::<ClientMessage>(line.trim()) {
                                match msg {
                                    ClientMessage::HeartbeatAck => {
                                        last_ack = Instant::now();
                                    }
                                    _ => {
                                        let _ = input_tx.send(ClientInput { client_id, message: msg }).await;
                                    }
                                }
                            }
                            line.clear();
                        }
                    }
                }

                Ok(broadcast_msg) = broadcast_rx.recv() => {
                    let msg = format!("{broadcast_msg}\n");
                    if writer.write_all(msg.as_bytes()).await.is_err() {
                        break;
                    }
                }

                _ = heartbeat_interval.tick() => {
                    if last_ack.elapsed() > HEARTBEAT_TIMEOUT {
                        warn!("Client {client_id} heartbeat timeout");
                        break;
                    }
                    if Self::send_message(&mut writer, &ServerMessage::Heartbeat).await.is_err() {
                        break;
                    }
                }
            }
        }
    }

    async fn send_message(
        writer: &mut (impl AsyncWriteExt + Unpin),
        msg: &ServerMessage,
    ) -> anyhow::Result<()> {
        let mut json = serde_json::to_string(msg)?;
        json.push('\n');
        writer.write_all(json.as_bytes()).await?;
        writer.flush().await?;
        Ok(())
    }
}
