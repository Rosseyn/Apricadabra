#![cfg(windows)]

use apricadabra_core::config::Config;
use apricadabra_core::server::Server;
use apricadabra_core::vjoy::MockJoystick;

use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::ClientOptions;
use tokio::time::{self, Duration};

fn test_config(pipe_name: &str) -> Config {
    Config {
        pipe_name: pipe_name.to_string(),
        ..Config::default()
    }
}

async fn send_line(writer: &mut (impl AsyncWriteExt + Unpin), msg: &str) {
    writer.write_all(format!("{msg}\n").as_bytes()).await.unwrap();
    writer.flush().await.unwrap();
}

async fn read_line(reader: &mut (impl AsyncBufReadExt + Unpin)) -> String {
    let mut line = String::new();
    reader.read_line(&mut line).await.unwrap();
    line
}

#[tokio::test]
async fn test_hello_welcome_handshake() {
    let pipe_name = r"\\.\pipe\apricadabra_test_handshake";
    let config = test_config(pipe_name);
    let joystick = Box::new(MockJoystick::new());
    let server = Server::new(config, joystick);

    let (_shutdown_tx, shutdown_rx) = tokio::sync::watch::channel(false);

    let server_handle = tokio::spawn(async move {
        let _ = server.run(shutdown_rx).await;
    });

    // Give server time to start
    time::sleep(Duration::from_millis(100)).await;

    // Connect client
    let pipe = ClientOptions::new().open(pipe_name).unwrap();
    let (reader, mut writer) = tokio::io::split(pipe);
    let mut reader = BufReader::new(reader);

    // Send hello
    send_line(&mut writer, r#"{"type":"hello","version":1,"name":"test"}"#).await;

    // Expect welcome
    let welcome = read_line(&mut reader).await;
    assert!(welcome.contains("\"type\":\"welcome\""));
    assert!(welcome.contains("\"version\":1"));

    server_handle.abort();
}

#[tokio::test]
async fn test_axis_hold_state_broadcast() {
    let pipe_name = r"\\.\pipe\apricadabra_test_axis";
    let config = test_config(pipe_name);
    let joystick = Box::new(MockJoystick::new());
    let server = Server::new(config, joystick);

    let (_shutdown_tx, shutdown_rx) = tokio::sync::watch::channel(false);

    let server_handle = tokio::spawn(async move {
        let _ = server.run(shutdown_rx).await;
    });

    time::sleep(Duration::from_millis(100)).await;

    let pipe = ClientOptions::new().open(pipe_name).unwrap();
    let (reader, mut writer) = tokio::io::split(pipe);
    let mut reader = BufReader::new(reader);

    // Handshake
    send_line(&mut writer, r#"{"type":"hello","version":1,"name":"test"}"#).await;
    let _ = read_line(&mut reader).await; // welcome

    // Send axis event
    send_line(&mut writer, r#"{"type":"axis","axis":1,"mode":"hold","diff":10,"sensitivity":0.01}"#).await;

    // Read messages until we get a state broadcast (may receive heartbeats first)
    let state = time::timeout(Duration::from_secs(2), async {
        loop {
            let line = read_line(&mut reader).await;
            if line.contains("\"type\":\"state\"") {
                return line;
            }
        }
    })
    .await
    .expect("Timed out waiting for state broadcast");
    assert!(state.contains("\"type\":\"state\""));

    server_handle.abort();
}
