use apricadabra_core::config::Config;
use apricadabra_core::server::Server;
use apricadabra_core::vjoy::MockJoystick;

use tracing::info;
use tracing_appender::rolling;
use tracing_subscriber::{fmt, layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
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

    let server_handle = tokio::spawn(async move {
        server.run(shutdown_rx).await
    });

    tokio::signal::ctrl_c().await?;
    info!("Shutting down...");
    let _ = shutdown_tx.send(true);

    // Give server time to broadcast shutdown and clean up
    let _ = tokio::time::timeout(
        std::time::Duration::from_secs(2),
        server_handle,
    ).await;

    Ok(())
}
