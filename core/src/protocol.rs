use serde::{Deserialize, Serialize};
use std::collections::HashMap;

/// Messages sent from plugins to the core.
#[derive(Debug, Deserialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ClientMessage {
    Hello {
        version: u32,
        name: String,
        #[serde(default, rename = "broadcastPort")]
        broadcast_port: Option<u16>,
        #[serde(default)]
        commands: Option<Vec<String>>,
    },
    Axis {
        axis: u8,
        mode: AxisMode,
        diff: i32,
        #[serde(default)]
        sensitivity: Option<f32>,
        #[serde(default, rename = "decayRate")]
        decay_rate: Option<f32>,
        #[serde(default)]
        steps: Option<u32>,
    },
    Button {
        button: u8,
        mode: ButtonMode,
        #[serde(default)]
        state: Option<ButtonState>,
        #[serde(default)]
        delay: Option<u64>,
        #[serde(default)]
        rate: Option<u64>,
        #[serde(default, rename = "shortButton")]
        short_button: Option<u8>,
        #[serde(default, rename = "longButton")]
        long_button: Option<u8>,
        #[serde(default)]
        threshold: Option<u64>,
    },
    Reset {
        axis: u8,
        position: f32,
    },
    HeartbeatAck,
    CoreUpgrade {
        #[serde(rename = "newVersion")]
        new_version: String,
        #[serde(default, rename = "estimatedStartupMs")]
        estimated_startup_ms: Option<u64>,
    },
}

#[derive(Debug, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum AxisMode {
    Hold,
    Spring,
    Detent,
}

#[derive(Debug, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ButtonMode {
    Momentary,
    Toggle,
    Pulse,
    Double,
    Rapid,
    #[serde(rename = "longshort")]
    LongShort,
}

#[derive(Debug, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ButtonState {
    Down,
    Up,
}

#[derive(Debug, Serialize, Deserialize, PartialEq, Clone)]
#[serde(rename_all = "snake_case")]
pub enum ApiStatus {
    Exists,
    Deprecated,
    Undefined,
}

/// Messages sent from the core to plugins.
#[derive(Debug, Serialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ServerMessage {
    Welcome {
        version: u32,
        axes: HashMap<u8, f32>,
        buttons: HashMap<u8, bool>,
        #[serde(skip_serializing_if = "Option::is_none", rename = "apiStatus")]
        api_status: Option<HashMap<String, ApiStatus>>,
        #[serde(skip_serializing_if = "Option::is_none", rename = "coreVersion")]
        core_version: Option<String>,
    },
    State {
        axes: HashMap<u8, f32>,
        buttons: HashMap<u8, bool>,
    },
    Heartbeat,
    Error {
        code: String,
        message: String,
    },
    Shutdown,
    CoreRestarting {
        #[serde(rename = "coreStartTimeout")]
        core_start_timeout: u64,
        reason: String,
        #[serde(skip_serializing_if = "Option::is_none", rename = "requestedBy")]
        requested_by: Option<String>,
    },
    Warning {
        code: String,
        message: String,
        context: HashMap<String, String>,
    },
}
