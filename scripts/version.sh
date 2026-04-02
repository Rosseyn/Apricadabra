#!/usr/bin/env bash
# Apricadabra Version Bump Script
# Usage:
#   ./scripts/version.sh core 0.2.0        # Bump core version, commit, tag
#   ./scripts/version.sh sdk 0.2.0         # Bump SDK version, commit, tag
#   ./scripts/version.sh loupedeck 1.1.0   # Bump Loupedeck plugin, commit, tag
#   ./scripts/version.sh streamdeck 1.1.0  # Bump Stream Deck plugin, commit, tag
#   ./scripts/version.sh trackpad 1.1.0    # Bump Trackpad plugin, commit, tag
#   ./scripts/version.sh all 0.2.0         # Bump everything to same version

set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if [ -z "$1" ] || [ -z "$2" ]; then
    echo "Usage: $0 <component> <version>"
    echo "Components: core, sdk, loupedeck, streamdeck, trackpad, all"
    exit 1
fi

COMPONENT="$1"
VERSION="$2"

bump_core() {
    local v="$1"
    echo "  core → $v"
    sed -i "s/^version = \".*\"/version = \"$v\"/" "$ROOT/core/Cargo.toml"
    echo "$v" > "$ROOT/core/apricadabra-core.version"
    git add "$ROOT/core/Cargo.toml" "$ROOT/core/apricadabra-core.version"
}

bump_sdk() {
    local v="$1"
    echo "  sdk → $v"
    sed -i "s/<Version>.*<\/Version>/<Version>$v<\/Version>/" "$ROOT/core/sdk/csharp/Apricadabra.Client/Apricadabra.Client.csproj"
    git add "$ROOT/core/sdk/csharp/Apricadabra.Client/Apricadabra.Client.csproj"
}

bump_loupedeck() {
    local v="$1"
    echo "  loupedeck → $v"
    sed -i "s/^version: .*/version: $v/" "$ROOT/loupedeck-plugin/ApricadabraPlugin/src/package/metadata/LoupedeckPackage.yaml"
    git add "$ROOT/loupedeck-plugin/ApricadabraPlugin/src/package/metadata/LoupedeckPackage.yaml"
}

bump_streamdeck() {
    local v="$1"
    echo "  streamdeck → $v"
    # package.json
    cd "$ROOT/streamdeck-plugin"
    sed -i "s/\"version\": \".*\"/\"version\": \"$v\"/" package.json
    # manifest.json top-level Version only (not Nodejs.Version or Software.MinimumVersion)
    sed -i "0,/\"Version\": \".*\"/{s/\"Version\": \".*\"/\"Version\": \"$v.0\"/}" com.apricadabra.streamdeck.sdPlugin/manifest.json
    git add package.json com.apricadabra.streamdeck.sdPlugin/manifest.json
    cd "$ROOT"
}

bump_trackpad() {
    local v="$1"
    echo "  trackpad → $v"
    # No dedicated version file — the .csproj doesn't have a <Version> tag
    # Version comes from the release tag. Nothing to update in source.
    echo "  (trackpad version set by release tag only)"
}

echo "Bumping version to $VERSION..."

case "$COMPONENT" in
    core)
        bump_core "$VERSION"
        git commit -m "chore: bump core to v$VERSION"
        git tag "core-v$VERSION"
        echo "Tagged: core-v$VERSION"
        ;;
    sdk)
        bump_sdk "$VERSION"
        git commit -m "chore: bump SDK to v$VERSION"
        git tag "sdk-csharp-v$VERSION"
        echo "Tagged: sdk-csharp-v$VERSION"
        ;;
    loupedeck)
        bump_loupedeck "$VERSION"
        git commit -m "chore: bump Loupedeck plugin to v$VERSION"
        git tag "loupedeck-v$VERSION"
        echo "Tagged: loupedeck-v$VERSION"
        ;;
    streamdeck)
        bump_streamdeck "$VERSION"
        git commit -m "chore: bump Stream Deck plugin to v$VERSION"
        git tag "streamdeck-v$VERSION"
        echo "Tagged: streamdeck-v$VERSION"
        ;;
    trackpad)
        bump_trackpad "$VERSION"
        git tag "trackpad-v$VERSION"
        echo "Tagged: trackpad-v$VERSION"
        ;;
    all)
        bump_core "$VERSION"
        bump_sdk "$VERSION"
        bump_loupedeck "$VERSION"
        bump_streamdeck "$VERSION"
        bump_trackpad "$VERSION"
        git commit -m "chore: bump all components to v$VERSION"
        git tag "core-v$VERSION"
        git tag "sdk-csharp-v$VERSION"
        git tag "loupedeck-v$VERSION"
        git tag "streamdeck-v$VERSION"
        git tag "trackpad-v$VERSION"
        echo "Tagged all: core-v$VERSION sdk-csharp-v$VERSION loupedeck-v$VERSION streamdeck-v$VERSION trackpad-v$VERSION"
        ;;
    *)
        echo "Unknown component: $COMPONENT"
        echo "Valid: core, sdk, loupedeck, streamdeck, trackpad, all"
        exit 1
        ;;
esac

echo ""
echo "Done. Push with:"
echo "  git push github main --tags"
echo "  git push origin main --tags"
