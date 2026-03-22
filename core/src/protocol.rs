use serde::{Deserialize, Serialize};
use std::collections::HashMap;

/// Messages sent from plugins to the core.
#[derive(Debug, Deserialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ClientMessage {
    Hello {
        version: u32,
        name: String,
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

/// Messages sent from the core to plugins.
#[derive(Debug, Serialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ServerMessage {
    Welcome {
        version: u32,
        axes: HashMap<u8, f32>,
        buttons: HashMap<u8, bool>,
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
}
