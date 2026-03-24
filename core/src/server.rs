use crate::axis::AxisManager;
use crate::button::ButtonManager;
use crate::config::Config;
use crate::protocol::*;
use crate::vjoy::{Axis, VirtualJoystick};

use std::collections::HashMap;
use std::sync::Arc;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::{NamedPipeServer, ServerOptions};
use tokio::net::UdpSocket;
use tokio::sync::{mpsc, Mutex};
use tokio::time::{self, Duration, Instant};
use tracing::{error, info, warn};

const PROTOCOL_VERSION: u32 = 1;
const HEARTBEAT_INTERVAL: Duration = Duration::from_secs(5);
const HEARTBEAT_TIMEOUT: Duration = Duration::from_secs(30);
const TICK_INTERVAL: Duration = Duration::from_millis(16); // ~60Hz

// UDP ports: core listens for commands, sends broadcasts
const UDP_COMMAND_PORT: u16 = 19871;
const UDP_BROADCAST_PORT: u16 = 19872;

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
        if let Err(e) = self.joystick.acquire(self.config.vjoy_device_id) {
            error!("Failed to acquire vJoy device {}: {e}", self.config.vjoy_device_id);
            return Err(e);
        }
        info!("Acquired vJoy device {}", self.config.vjoy_device_id);

        let axis_mgr = Arc::new(Mutex::new(AxisManager::new()));
        let button_mgr = Arc::new(Mutex::new(ButtonManager::new()));
        let joystick = Arc::new(Mutex::new(self.joystick));

        let mut client_counter: u64 = 0;
        let connected_clients = Arc::new(std::sync::atomic::AtomicU64::new(0));
        let (disconnect_tx, mut disconnect_rx) = mpsc::channel::<u64>(32);

        // UDP sockets
        let cmd_socket = UdpSocket::bind(format!("127.0.0.1:{UDP_COMMAND_PORT}")).await?;
        let broadcast_socket = UdpSocket::bind("127.0.0.1:0").await?;
        info!("UDP command port: {UDP_COMMAND_PORT}");

        let broadcast_targets: Arc<Mutex<HashMap<u64, std::net::SocketAddr>>> = Arc::new(Mutex::new(HashMap::new()));

        // Pipe accept loop (handshake + heartbeat only)
        let accept_axis = axis_mgr.clone();
        let accept_button = button_mgr.clone();
        let accept_clients = connected_clients.clone();
        let accept_disconnect_tx = disconnect_tx.clone();
        let accept_broadcast_targets = broadcast_targets.clone();
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

                let axis = accept_axis.clone();
                let button = accept_button.clone();
                let clients = accept_clients.clone();
                let disc_tx = accept_disconnect_tx.clone();
                let bt = accept_broadcast_targets.clone();

                tokio::spawn(async move {
                    Self::handle_client(client_id, pipe, axis, button, bt).await;
                    clients.fetch_sub(1, std::sync::atomic::Ordering::Relaxed);
                    let _ = disc_tx.send(client_id).await;
                    info!("Client {client_id} disconnected");
                });
            }
        });

        // Main tick loop
        let mut tick_interval = time::interval(TICK_INTERVAL);
        let mut last_change = Instant::now();
        let mut last_broadcast = Instant::now();
        let mut has_pending_broadcast = false;
        let debounce = Duration::from_millis(100);
        let max_interval = Duration::from_millis(250);

        // UDP command receive buffer
        let mut cmd_buf = [0u8; 4096];

        loop {
            tokio::select! {
                _ = tick_interval.tick() => {
                    let mut axes = axis_mgr.lock().await;
                    let mut buttons = button_mgr.lock().await;

                    // Process spring decay
                    axes.tick_spring_decay();

                    // Process disconnect decay if active
                    axes.tick_disconnect_decay();

                    buttons.process_pending();
                    buttons.process_rapid_ticks();

                    let axis_changes = axes.take_changed();
                    let button_changes = buttons.take_changed();

                    if !axis_changes.is_empty() || !button_changes.is_empty() {
                        let mut joy = joystick.lock().await;
                        for (&id, &value) in &axis_changes {
                            if let Some(axis) = Axis::from_id(id) {
                                let _ = joy.set_axis(axis, value);
                            }
                        }
                        for (&id, &pressed) in &button_changes {
                            let _ = joy.set_button(id, pressed);
                        }

                        last_change = Instant::now();
                        has_pending_broadcast = true;
                    }

                    // Broadcast state via UDP: debounce 100ms, forced max every 250ms
                    if has_pending_broadcast {
                        let since_change = last_change.elapsed();
                        let since_broadcast = last_broadcast.elapsed();

                        if since_change >= debounce || since_broadcast >= max_interval {
                            last_broadcast = Instant::now();
                            has_pending_broadcast = false;

                            let msg = ServerMessage::State {
                                axes: axes.get_all(),
                                buttons: buttons.get_all(),
                            };
                            if let Ok(json) = serde_json::to_string(&msg) {
                                let targets = broadcast_targets.lock().await;
                                for (_id, addr) in targets.iter() {
                                    let _ = broadcast_socket.send_to(json.as_bytes(), addr).await;
                                }
                            }
                        }
                    }
                }

                // Receive commands via UDP
                result = cmd_socket.recv_from(&mut cmd_buf) => {
                    if let Ok((len, _addr)) = result {
                        if let Ok(text) = std::str::from_utf8(&cmd_buf[..len]) {
                            let trimmed = text.trim();
                            // Check for shutdown command
                            if trimmed.contains("\"shutdown\"") {
                                info!("Received shutdown command via UDP");
                                let _ = joystick.lock().await.release();
                                break;
                            }
                            if let Ok(msg) = serde_json::from_str::<ClientMessage>(trimmed) {
                                Self::process_command(&self.config, &axis_mgr, &button_mgr, msg).await;
                            }
                        }
                    }
                }

                Some(client_id) = disconnect_rx.recv() => {
                    info!("Client {client_id} cleanup");
                    broadcast_targets.lock().await.remove(&client_id);
                    if connected_clients.load(std::sync::atomic::Ordering::Relaxed) == 0 {
                        info!("All clients disconnected");
                    }
                }

                _ = shutdown_rx.changed() => {
                    if *shutdown_rx.borrow() {
                        info!("Shutting down");
                        let _ = joystick.lock().await.release();
                        break;
                    }
                }
            }
        }

        Ok(())
    }

    async fn process_command(
        config: &Config,
        axis_mgr: &Arc<Mutex<AxisManager>>,
        button_mgr: &Arc<Mutex<ButtonManager>>,
        msg: ClientMessage,
    ) {
        match msg {
            ClientMessage::Axis { axis, mode, diff, sensitivity, decay_rate, steps } => {
                let sens = sensitivity.unwrap_or(config.default_sensitivity);
                let mut axes = axis_mgr.lock().await;
                match mode {
                    AxisMode::Hold => axes.apply_hold(axis, diff, sens),
                    AxisMode::Spring => {
                        let dr = decay_rate.unwrap_or(config.default_decay_rate);
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
                info!("Reset axis {axis} to position {position}");
                axis_mgr.lock().await.reset(axis, position);
            }
            ClientMessage::Hello { .. } | ClientMessage::HeartbeatAck => {}
        }
    }

    async fn handle_client(
        client_id: u64,
        pipe: NamedPipeServer,
        axis_mgr: Arc<Mutex<AxisManager>>,
        button_mgr: Arc<Mutex<ButtonManager>>,
        broadcast_targets: Arc<Mutex<HashMap<u64, std::net::SocketAddr>>>,
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
            ClientMessage::Hello { version, name, broadcast_port } => {
                info!("Client {client_id} hello: {name} v{version}");
                if version != PROTOCOL_VERSION {
                    let err = ServerMessage::Error {
                        code: "unsupported_version".to_string(),
                        message: format!("Server supports protocol v{PROTOCOL_VERSION}, client sent v{version}"),
                    };
                    let _ = Self::send_message(&mut writer, &err).await;
                    return;
                }

                let axes = axis_mgr.lock().await.get_all();
                let buttons = button_mgr.lock().await.get_all();
                let welcome = ServerMessage::Welcome { version: PROTOCOL_VERSION, axes, buttons };
                if Self::send_message(&mut writer, &welcome).await.is_err() {
                    return;
                }

                let port = broadcast_port.unwrap_or(UDP_BROADCAST_PORT);
                let addr: std::net::SocketAddr = format!("127.0.0.1:{port}").parse().unwrap();
                broadcast_targets.lock().await.insert(client_id, addr);
                info!("Client {client_id} registered broadcast target: {addr}");
            }
            _ => return,
        }

        line.clear();

        // Pipe only handles heartbeat now
        let mut heartbeat_interval = time::interval(HEARTBEAT_INTERVAL);
        let mut last_ack = Instant::now();

        loop {
            tokio::select! {
                result = reader.read_line(&mut line) => {
                    match result {
                        Ok(0) | Err(_) => break,
                        Ok(_) => {
                            if let Ok(msg) = serde_json::from_str::<ClientMessage>(line.trim()) {
                                if matches!(msg, ClientMessage::HeartbeatAck) {
                                    last_ack = Instant::now();
                                }
                            }
                            line.clear();
                        }
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
