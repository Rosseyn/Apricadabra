#!/usr/bin/env bash
# Apricadabra Build Script (WSL/Linux)
# Usage:
#   ./scripts/build.sh              # Build everything
#   ./scripts/build.sh core         # Build core only
#   ./scripts/build.sh loupedeck    # Build Loupedeck plugin only
#   ./scripts/build.sh streamdeck   # Build Stream Deck plugin only
#   ./scripts/build.sh trackpad     # Build trackpad plugin (core lib + UI)
#   ./scripts/build.sh sdk          # Build C# client SDK only
#   ./scripts/build.sh validate        # Validate Stream Deck plugin
#   ./scripts/build.sh pack-sd         # Package .streamDeckPlugin
#   ./scripts/build.sh core trackpad   # Build multiple targets

set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ALL_TARGETS="core sdk loupedeck streamdeck trackpad"
TARGETS="${@:-$ALL_TARGETS}"

BUILT=()
FAILED=()

step() { echo -e "\n\033[36m[$1]\033[0m"; }
ok() { echo -e "  \033[32mOK: $1\033[0m"; }
fail() { echo -e "  \033[31mFAIL: $1\033[0m"; }

for target in $TARGETS; do
    case "$target" in
        core)
            step "Building Core (Rust)"
            if (cd "$ROOT/core" && cargo build --release); then
                ok "core/target/release/apricadabra-core.exe"
                BUILT+=("core")
            else
                fail "Core build failed"
                FAILED+=("core")
            fi
            ;;

        sdk)
            step "Building Apricadabra.Client SDK"
            if (cd "$ROOT/core/sdk/csharp/Apricadabra.Client" && dotnet build -c Release); then
                ok "Apricadabra.Client.dll"
                BUILT+=("sdk")
            else
                fail "SDK build failed"
                FAILED+=("sdk")
            fi
            ;;

        loupedeck)
            step "Building Loupedeck Plugin"
            if (cd "$ROOT/loupedeck-plugin/ApricadabraPlugin/src" && dotnet build -c Release); then
                ok "ApricadabraPlugin.dll"
                BUILT+=("loupedeck")
            else
                fail "Loupedeck build failed"
                FAILED+=("loupedeck")
            fi
            ;;

        streamdeck)
            step "Building Stream Deck Plugin"
            if (cd "$ROOT/streamdeck-plugin" && chmod +x node_modules/.bin/* 2>/dev/null; npm run build); then
                ok "plugin.js"
                BUILT+=("streamdeck")
            else
                fail "Stream Deck build failed"
                FAILED+=("streamdeck")
            fi
            ;;

        trackpad)
            step "Building Trackpad Plugin"
            if (cd "$ROOT/trackpad-plugin/Apricadabra.Trackpad" && dotnet build -c Release); then
                ok "Apricadabra.Trackpad.exe"
                BUILT+=("trackpad")
            else
                fail "Trackpad build failed"
                FAILED+=("trackpad")
            fi
            ;;

        validate)
            step "Validating Stream Deck Plugin"
            if streamdeck validate "$ROOT/streamdeck-plugin/com.apricadabra.streamdeck.sdPlugin" --no-update-check; then
                ok "validation passed"
                BUILT+=("validate")
            else
                fail "Validation failed"
                FAILED+=("validate")
            fi
            ;;

        pack-sd)
            step "Packaging Stream Deck Plugin"
            if streamdeck pack "$ROOT/streamdeck-plugin/com.apricadabra.streamdeck.sdPlugin" --output "$ROOT"; then
                ok ".streamDeckPlugin created"
                BUILT+=("pack-sd")
            else
                fail "Packaging failed"
                FAILED+=("pack-sd")
            fi
            ;;

        *)
            fail "Unknown target: $target"
            echo "  Valid targets: $ALL_TARGETS validate pack-sd"
            FAILED+=("$target")
            ;;
    esac
done

echo -e "\n--- Build Summary ---"
[ ${#BUILT[@]} -gt 0 ] && echo -e "  \033[32mBuilt: ${BUILT[*]}\033[0m"
[ ${#FAILED[@]} -gt 0 ] && echo -e "  \033[31mFailed: ${FAILED[*]}\033[0m" && exit 1
