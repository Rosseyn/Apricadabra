use apricadabra_core::config::Config;
use apricadabra_core::server::Server;
use apricadabra_core::vjoy::MockJoystick;

use tracing::info;
use tracing_appender::rolling;
use tracing_subscriber::{fmt, layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Handle --stop flag: send UDP shutdown and exit
    if std::env::args().any(|a| a == "--stop") {
        let socket = std::net::UdpSocket::bind("0.0.0.0:0")?;
        socket.send_to(b"{\"type\":\"shutdown\"}", "127.0.0.1:19871")?;
        println!("Shutdown signal sent.");
        return Ok(());
    }

    let config = Config::load();

    // Set up logging
    let log_dir = Config::config_dir().join("logs");
    std::fs::create_dir_all(&log_dir)?;
    // Note: tracing-appender rotates daily but does not enforce file count/size limits.
    // Log retention (spec: 5 files, 10MB max) is deferred — add cleanup logic later.
    let file_appender = rolling::daily(&log_dir, "apricadabra-core.log");

    let env_filter = EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| EnvFilter::new(&config.log_level));

    tracing_subscriber::registry()
        .with(env_filter)
        .with(fmt::layer().with_writer(std::io::stderr))
        .with(fmt::layer().with_writer(file_appender).with_ansi(false))
        .init();

    info!("Apricadabra Core v{} starting", env!("CARGO_PKG_VERSION"));
    info!("vJoy device ID: {}", config.vjoy_device_id);

    // Parse --debug flag
    let debug = std::env::args().any(|a| a == "--debug");
    if debug {
        info!("Debug mode enabled via --debug flag");
    }

    #[cfg(windows)]
    let joystick: Box<dyn apricadabra_core::vjoy::VirtualJoystick> = {
        match apricadabra_core::vjoy::VJoyBackend::new() {
            Ok(backend) => Box::new(backend),
            Err(e) => {
                tracing::error!("vJoy initialization failed: {e}");
                tracing::warn!("Falling back to mock joystick (no game output)");
                Box::new(MockJoystick::new())
            }
        }
    };

    #[cfg(not(windows))]
    let joystick: Box<dyn apricadabra_core::vjoy::VirtualJoystick> = {
        tracing::warn!("Not on Windows — using mock joystick");
        Box::new(MockJoystick::new())
    };
    let server = Server::new(config, joystick);

    // Handle Ctrl+C for graceful shutdown
    let (shutdown_tx, shutdown_rx) = tokio::sync::watch::channel(false);

    let mut server_handle = tokio::spawn(async move {
        server.run(shutdown_rx).await
    });

    tokio::select! {
        _ = tokio::signal::ctrl_c() => {
            info!("Ctrl+C received, shutting down...");
            let _ = shutdown_tx.send(true);
        }
        result = &mut server_handle => {
            info!("Server exited");
            if let Ok(Err(e)) = result {
                tracing::error!("Server error: {e}");
            }
        }
    }

    Ok(())
}
