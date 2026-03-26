use crate::protocol::ApiStatus;
use std::collections::HashMap;

pub struct ApiRegistry {
    commands: HashMap<String, ApiStatus>,
}

impl ApiRegistry {
    pub fn new() -> Self {
        let mut commands = HashMap::new();
        commands.insert("axis".to_string(), ApiStatus::Exists);
        commands.insert("button".to_string(), ApiStatus::Exists);
        commands.insert("reset".to_string(), ApiStatus::Exists);
        commands.insert("shutdown".to_string(), ApiStatus::Exists);
        Self { commands }
    }

    pub fn status(&self, command: &str) -> ApiStatus {
        self.commands.get(command).cloned().unwrap_or(ApiStatus::Undefined)
    }

    pub fn resolve(&self, requested: &[String]) -> HashMap<String, ApiStatus> {
        requested.iter().map(|cmd| (cmd.clone(), self.status(cmd))).collect()
    }
}
