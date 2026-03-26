use apricadabra_core::protocol::*;
use std::collections::HashMap;

#[test]
fn test_parse_hello() {
    let json = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    assert!(matches!(msg, ClientMessage::Hello { version: 1, ref name, .. } if name == "loupedeck"));
}

#[test]
fn test_parse_axis_hold() {
    let json = r#"{"type":"axis","axis":1,"mode":"hold","diff":3,"sensitivity":0.5}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Axis { axis, mode: AxisMode::Hold, diff, sensitivity, .. } => {
            assert_eq!(axis, 1);
            assert_eq!(diff, 3);
            assert!((sensitivity.unwrap() - 0.5).abs() < f32::EPSILON);
        }
        _ => panic!("Expected Axis Hold"),
    }
}

#[test]
fn test_parse_axis_spring() {
    let json = r#"{"type":"axis","axis":2,"mode":"spring","diff":-1,"sensitivity":0.5,"decayRate":0.3}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Axis { axis, mode: AxisMode::Spring, diff, decay_rate, .. } => {
            assert_eq!(axis, 2);
            assert_eq!(diff, -1);
            assert!((decay_rate.unwrap() - 0.3).abs() < f32::EPSILON);
        }
        _ => panic!("Expected Axis Spring"),
    }
}

#[test]
fn test_parse_axis_detent() {
    let json = r#"{"type":"axis","axis":3,"mode":"detent","diff":1,"steps":5}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Axis { axis, mode: AxisMode::Detent, diff, steps, .. } => {
            assert_eq!(axis, 3);
            assert_eq!(diff, 1);
            assert_eq!(steps.unwrap(), 5);
        }
        _ => panic!("Expected Axis Detent"),
    }
}

#[test]
fn test_parse_button_momentary() {
    let json = r#"{"type":"button","button":1,"mode":"momentary","state":"down"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Button { button, mode: ButtonMode::Momentary, state, .. } => {
            assert_eq!(button, 1);
            assert_eq!(state.unwrap(), ButtonState::Down);
        }
        _ => panic!("Expected Button Momentary"),
    }
}

#[test]
fn test_parse_button_longshort() {
    let json = r#"{"type":"button","button":6,"mode":"longshort","state":"down","shortButton":6,"longButton":7,"threshold":500}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Button { mode: ButtonMode::LongShort, short_button, long_button, threshold, .. } => {
            assert_eq!(short_button.unwrap(), 6);
            assert_eq!(long_button.unwrap(), 7);
            assert_eq!(threshold.unwrap(), 500);
        }
        _ => panic!("Expected Button LongShort"),
    }
}

#[test]
fn test_parse_reset() {
    let json = r#"{"type":"reset","axis":1,"position":0.5}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Reset { axis, position } => {
            assert_eq!(axis, 1);
            assert!((position - 0.5).abs() < f32::EPSILON);
        }
        _ => panic!("Expected Reset"),
    }
}

#[test]
fn test_hello_with_broadcast_port() {
    let json = r#"{"type":"hello","version":1,"name":"streamdeck","broadcastPort":19873}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, broadcast_port, .. } => {
            assert_eq!(version, 1);
            assert_eq!(name, "streamdeck");
            assert_eq!(broadcast_port, Some(19873));
        }
        _ => panic!("Expected Hello"),
    }
}

#[test]
fn test_hello_without_broadcast_port() {
    let json = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, broadcast_port, .. } => {
            assert_eq!(version, 1);
            assert_eq!(name, "loupedeck");
            assert_eq!(broadcast_port, None);
        }
        _ => panic!("Expected Hello"),
    }
}

#[test]
fn test_parse_heartbeat_ack() {
    let json = r#"{"type":"heartbeat_ack"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    assert!(matches!(msg, ClientMessage::HeartbeatAck));
}

#[test]
fn test_serialize_welcome() {
    let msg = ServerMessage::Welcome {
        version: 1,
        axes: vec![(1, 0.5), (2, 0.73)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
        api_status: None,
        core_version: None,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"welcome\""));
    assert!(json.contains("\"version\":1"));
}

#[test]
fn test_serialize_state() {
    let msg = ServerMessage::State {
        axes: vec![(1, 0.73)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"state\""));
}

#[test]
fn test_serialize_heartbeat() {
    let msg = ServerMessage::Heartbeat;
    let json = serde_json::to_string(&msg).unwrap();
    assert_eq!(json, r#"{"type":"heartbeat"}"#);
}

#[test]
fn test_serialize_error() {
    let msg = ServerMessage::Error {
        code: "vjoy_not_installed".to_string(),
        message: "vJoy driver not found.".to_string(),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"error\""));
    assert!(json.contains("vjoy_not_installed"));
}

#[test]
fn test_serialize_shutdown() {
    let msg = ServerMessage::Shutdown;
    let json = serde_json::to_string(&msg).unwrap();
    assert_eq!(json, r#"{"type":"shutdown"}"#);
}

#[test]
fn test_parse_hello_v2_with_commands() {
    let json = r#"{"type":"hello","version":1,"name":"streamdeck","commands":["axis","button","reset"]}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, commands, .. } => {
            assert_eq!(version, 1);
            assert_eq!(name, "streamdeck");
            assert_eq!(commands.unwrap(), vec!["axis", "button", "reset"]);
        }
        _ => panic!("Expected Hello"),
    }
}

#[test]
fn test_parse_hello_v2_without_commands() {
    let json = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, commands, .. } => {
            assert_eq!(version, 1);
            assert_eq!(name, "loupedeck");
            assert_eq!(commands, None);
        }
        _ => panic!("Expected Hello"),
    }
}

#[test]
fn test_serialize_welcome_v2() {
    let mut api_status = HashMap::new();
    api_status.insert("axis".to_string(), ApiStatus::Exists);
    api_status.insert("reset".to_string(), ApiStatus::Deprecated);
    api_status.insert("foobar".to_string(), ApiStatus::Undefined);

    let msg = ServerMessage::Welcome {
        version: 1,
        axes: vec![(1, 0.5)].into_iter().collect(),
        buttons: vec![(1, false)].into_iter().collect(),
        api_status: Some(api_status),
        core_version: Some("2.0.0".to_string()),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"welcome\""));
    assert!(json.contains("\"apiStatus\""));
    assert!(json.contains("\"exists\""));
    assert!(json.contains("\"deprecated\""));
    assert!(json.contains("\"undefined\""));
    assert!(json.contains("\"coreVersion\":\"2.0.0\""));
}

#[test]
fn test_serialize_welcome_v1_compat() {
    let msg = ServerMessage::Welcome {
        version: 1,
        axes: vec![(1, 0.5)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
        api_status: None,
        core_version: None,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"welcome\""));
    assert!(json.contains("\"version\":1"));
    // v1 compat: None fields should be omitted from JSON
    assert!(!json.contains("apiStatus"));
    assert!(!json.contains("coreVersion"));
}

#[test]
fn test_parse_core_upgrade() {
    let json = r#"{"type":"core_upgrade","newVersion":"2.1.0","estimatedStartupMs":3000}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::CoreUpgrade { new_version, estimated_startup_ms } => {
            assert_eq!(new_version, "2.1.0");
            assert_eq!(estimated_startup_ms, Some(3000));
        }
        _ => panic!("Expected CoreUpgrade"),
    }
}

#[test]
fn test_serialize_core_restarting() {
    let msg = ServerMessage::CoreRestarting {
        core_start_timeout: 10000,
        reason: "upgrade".to_string(),
        requested_by: Some("streamdeck".to_string()),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"core_restarting\""));
    assert!(json.contains("\"coreStartTimeout\":10000"));
    assert!(json.contains("\"reason\":\"upgrade\""));
    assert!(json.contains("\"requestedBy\":\"streamdeck\""));
}

#[test]
fn test_serialize_core_restarting_shutdown() {
    let msg = ServerMessage::CoreRestarting {
        core_start_timeout: 5000,
        reason: "shutdown".to_string(),
        requested_by: None,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"core_restarting\""));
    assert!(json.contains("\"coreStartTimeout\":5000"));
    assert!(json.contains("\"reason\":\"shutdown\""));
    assert!(!json.contains("requestedBy"));
}

#[test]
fn test_serialize_warning() {
    let mut context = HashMap::new();
    context.insert("axis".to_string(), "3".to_string());
    context.insert("limit".to_string(), "1.0".to_string());

    let msg = ServerMessage::Warning {
        code: "axis_limit_exceeded".to_string(),
        message: "Axis value exceeds configured limit".to_string(),
        context,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"warning\""));
    assert!(json.contains("\"code\":\"axis_limit_exceeded\""));
    assert!(json.contains("\"message\":\"Axis value exceeds configured limit\""));
    assert!(json.contains("\"axis\":\"3\""));
    assert!(json.contains("\"limit\":\"1.0\""));
}
