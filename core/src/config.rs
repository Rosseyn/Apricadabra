use serde::Deserialize;
use std::path::PathBuf;
use tracing::info;

#[derive(Debug, Deserialize)]
pub struct Config {
    #[serde(default = "default_device_id")]
    pub vjoy_device_id: u8,
    #[serde(default = "default_sensitivity")]
    pub default_sensitivity: f32,
    #[serde(default = "default_decay_rate")]
    pub default_decay_rate: f32,
    #[serde(default = "default_log_level")]
    pub log_level: String,
    #[serde(default = "default_pipe_name")]
    pub pipe_name: String,
}

fn default_device_id() -> u8 { 1 }
fn default_sensitivity() -> f32 { 0.02 }
fn default_decay_rate() -> f32 { 0.95 }
fn default_log_level() -> String { "info".to_string() }
fn default_pipe_name() -> String { r"\\.\pipe\apricadabra".to_string() }

impl Default for Config {
    fn default() -> Self {
        Self {
            vjoy_device_id: default_device_id(),
            default_sensitivity: default_sensitivity(),
            default_decay_rate: default_decay_rate(),
            log_level: default_log_level(),
            pipe_name: default_pipe_name(),
        }
    }
}

impl Config {
    pub fn load() -> Self {
        let path = Self::config_path();
        match std::fs::read_to_string(&path) {
            Ok(contents) => {
                match serde_json::from_str(&contents) {
                    Ok(config) => {
                        info!("Loaded config from {}", path.display());
                        config
                    }
                    Err(e) => {
                        tracing::warn!("Failed to parse config: {e}, using defaults");
                        Self::default()
                    }
                }
            }
            Err(_) => {
                info!("No config file found at {}, using defaults", path.display());
                Self::default()
            }
        }
    }

    pub fn config_dir() -> PathBuf {
        dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("Apricadabra")
    }

    fn config_path() -> PathBuf {
        Self::config_dir().join("config.json")
    }
}
