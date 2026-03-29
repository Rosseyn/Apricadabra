# Apricadabra Build Script
# Usage:
#   .\scripts\build.ps1              # Build everything
#   .\scripts\build.ps1 core         # Build core only
#   .\scripts\build.ps1 loupedeck    # Build Loupedeck plugin only
#   .\scripts\build.ps1 streamdeck   # Build Stream Deck plugin only
#   .\scripts\build.ps1 trackpad     # Build trackpad plugin (core lib + UI)
#   .\scripts\build.ps1 sdk          # Build C# client SDK only
#   .\scripts\build.ps1 core trackpad  # Build multiple targets

param(
    [Parameter(Position=0, ValueFromRemainingArguments)]
    [string[]]$Targets
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$AllTargets = @("core", "sdk", "loupedeck", "streamdeck", "trackpad")
if (-not $Targets -or $Targets.Count -eq 0) {
    $Targets = $AllTargets
}

$Failed = @()
$Built = @()

function Write-Step($msg) { Write-Host "`n[$msg]" -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host "  OK: $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "  FAIL: $msg" -ForegroundColor Red }

foreach ($target in $Targets) {
    switch ($target.ToLower()) {
        "core" {
            Write-Step "Building Core (Rust)"
            Push-Location "$Root\core"
            try {
                cargo build --release
                Write-Ok "core\target\release\apricadabra-core.exe"
                $Built += "core"
            } catch {
                Write-Fail "Core build failed: $_"
                $Failed += "core"
            }
            Pop-Location
        }

        "sdk" {
            Write-Step "Building Apricadabra.Client SDK"
            Push-Location "$Root\core\sdk\csharp\Apricadabra.Client"
            try {
                dotnet build -c Release
                Write-Ok "Apricadabra.Client.dll"
                $Built += "sdk"
            } catch {
                Write-Fail "SDK build failed: $_"
                $Failed += "sdk"
            }
            Pop-Location
        }

        "loupedeck" {
            Write-Step "Building Loupedeck Plugin"
            Push-Location "$Root\loupedeck-plugin\ApricadabraPlugin\src"
            try {
                dotnet build -c Release
                Write-Ok "ApricadabraPlugin.dll"
                $Built += "loupedeck"
            } catch {
                Write-Fail "Loupedeck build failed: $_"
                $Failed += "loupedeck"
            }
            Pop-Location
        }

        "streamdeck" {
            Write-Step "Building Stream Deck Plugin"
            Push-Location "$Root\streamdeck-plugin"
            try {
                npm run build
                Write-Ok "com.apricadabra.streamdeck.sdPlugin\bin\plugin.js"
                $Built += "streamdeck"
            } catch {
                Write-Fail "Stream Deck build failed: $_"
                $Failed += "streamdeck"
            }
            Pop-Location
        }

        "trackpad" {
            Write-Step "Building Trackpad Plugin"
            Push-Location "$Root\trackpad-plugin\Apricadabra.Trackpad"
            try {
                dotnet build -c Release
                Write-Ok "Apricadabra.Trackpad.exe"
                $Built += "trackpad"
            } catch {
                Write-Fail "Trackpad build failed: $_"
                $Failed += "trackpad"
            }
            Pop-Location
        }

        default {
            Write-Fail "Unknown target: $target"
            Write-Host "  Valid targets: $($AllTargets -join ', ')" -ForegroundColor Yellow
            $Failed += $target
        }
    }
}

# Summary
Write-Host "`n--- Build Summary ---" -ForegroundColor White
if ($Built.Count -gt 0) {
    Write-Host "  Built: $($Built -join ', ')" -ForegroundColor Green
}
if ($Failed.Count -gt 0) {
    Write-Host "  Failed: $($Failed -join ', ')" -ForegroundColor Red
    exit 1
}
