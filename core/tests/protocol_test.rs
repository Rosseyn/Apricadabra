use apricadabra_core::protocol::*;

#[test]
fn test_parse_hello() {
    let json = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    assert!(matches!(msg, ClientMessage::Hello { version: 1, name } if name == "loupedeck"));
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
