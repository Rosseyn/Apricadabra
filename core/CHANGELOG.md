# Changelog

All notable changes to the Apricadabra Core protocol will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- Protocol v2: API negotiation in handshake (`commands` in hello, `apiStatus`/`coreVersion` in welcome)
- Core upgrade flow: `core_upgrade` and `core_restarting` messages for live version management
- `--debug-messages` flag for developer warnings on unknown modes/actions
- `--version` flag for core version discovery
- `apricadabra-core.version` file for plugin version detection
- Formal protocol spec at `core/docs/protocol.md`
- Standardized plugin bindings schema (`%APPDATA%/Apricadabra/<plugin>/bindings.json`)

### Changed
- Protocol version check changed from strict equality to minimum version (older plugins accepted)
- Unknown button modes default to `momentary`, unknown axis modes default to `hold`
- Malformed and unknown action types are silently dropped (no-op)

### Deprecated
- Nothing yet. Deprecations will be listed here with migration guidance.
