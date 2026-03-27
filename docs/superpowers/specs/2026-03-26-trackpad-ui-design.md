# Trackpad UI Design

**Date:** 2026-03-26
**Sub-project:** 3b of 3 (Protocol → C# Client Library → Trackpad Listener → Trackpad UI)
**Status:** Draft

## Overview

A WPF system tray application (`Apricadabra.Trackpad`) that provides a configuration interface for the trackpad listener library. Users configure gesture-to-action bindings, adjust recognition thresholds, and monitor live gesture detection — all while the trackpad captures gestures in the background for sim games.

## Context

The `Apricadabra.Trackpad.Core` library (sub-project 3a) handles all input capture, gesture recognition, and action dispatch. This UI project wraps it in a user-friendly Windows application with a system tray presence, binding editor, settings panel, and live test visualization.

---

## 1. Project Structure

```
trackpad-plugin/
├── Apricadabra.Trackpad/                    # WPF app (.NET 8, Windows)
│   ├── Apricadabra.Trackpad.csproj
│   ├── App.xaml / App.xaml.cs               # Entry point, system tray, single-instance
│   ├── MainWindow.xaml / .cs                # Main config window
│   ├── Views/
│   │   ├── BindingsView.xaml / .cs          # Binding list with inline editing
│   │   └── SettingsView.xaml / .cs          # Global settings (thresholds, sensitivities)
│   ├── Controls/
│   │   ├── TestPanel.xaml / .cs             # Live gesture test panel
│   │   └── BindingEditor.xaml / .cs         # Inline binding edit row
│   ├── ViewModels/
│   │   ├── MainViewModel.cs                 # Orchestrates TrackpadService, tab state
│   │   ├── BindingsViewModel.cs             # Binding list CRUD, inline edit state
│   │   └── SettingsViewModel.cs             # Settings two-way binding
│   └── Themes/
│       ├── LightTheme.xaml                  # Light theme resource dictionary
│       └── DarkTheme.xaml                   # Dark theme resource dictionary
```

**Target framework:** .NET 8.0-windows (WPF)

**Dependencies:**
- `Apricadabra.Trackpad.Core` (project reference)
- `Apricadabra.Client` (transitive via Core)
- `Hardcodet.NotifyIcon.Wpf` (NuGet — system tray icon for WPF)

---

## 2. System Tray Behavior

The app runs as a system tray application. The trackpad service runs in the background regardless of whether the config window is visible.

**Startup:**
- App starts minimized to system tray (no window shown)
- `TrackpadService.Start()` is called immediately — gesture capture begins
- Tray icon appears with the Apricadabra icon

**Tray icon context menu:**
- **Open** — shows the config window
- **Start / Stop** — toggles the trackpad service (changes between "Start" and "Stop")
- **Exit** — stops the service and exits the application

**Tray icon interactions:**
- Double-click opens the config window
- Single-click does nothing (avoids accidental opens)

**Window close behavior:**
- Clicking the window close button (X) minimizes to tray — does not exit
- The app only exits via tray menu "Exit"

**Single instance:** Only one instance of the app can run. If launched again, the existing instance's window is brought to foreground.

---

## 3. Main Window Layout

The window has three areas: top bar, left panel, and right panel.

**Window properties:**
- Default size: 800x500
- Minimum size: 600x400
- Resizable
- Position and size saved to `TrackpadSettings` (restored on next open)
- Dark theme by default

### Top Bar

Left side: subtle tab toggle with underline indicator
- **Bindings** (default active)
- **Settings**

Right side (right-aligned):
- Connection status: colored dot (green = connected, red = disconnected) + text ("Connected · Core v0.1.0")
- Device selector dropdown: "All Devices ▾" or specific device name

The device dropdown is populated from `TrackpadService.Input.AvailableDevices` and refreshes on hot-plug events. Selecting a device updates `TrackpadSettings.SelectedDevicePath` and the input filter immediately.

### Left Panel

Switches content based on active tab:
- **Bindings tab** → `BindingsView`
- **Settings tab** → `SettingsView`

### Right Panel

`TestPanel` — always visible regardless of active tab. Fixed width (~200px), separated by a vertical border.

---

## 4. Bindings View

A scrollable list of binding rows with inline editing and an "Add Binding" button at the bottom.

### Collapsed Row (Display Mode)

```
3-finger swipe left → Button 5 (pulse)        [edit] [×]
```

- Gesture description on the left (human-readable: finger count + gesture type + direction)
- Action description in accent color (Axis/Button + number + mode)
- Edit and delete controls on the right

### Expanded Row (Edit Mode)

Clicking "edit" or "+ Add Binding" expands the row inline:

```
Gesture:  [Scroll ▾]  Fingers: [2 ▾]  Direction: [Up ▾]
Action:   [Axis ▾]    Axis: [1 ▾]     Mode: [Hold ▾]
          Sensitivity: [====○====] 0.02
                                    [Save] [Cancel]
```

**Gesture fields:**
- Type: dropdown — Scroll, Pinch, Rotate, Swipe, Tap
- Fingers: dropdown — 2, 3, 4 (hidden for Pinch which is always 2)
- Direction: dropdown — context-dependent:
  - Scroll/Swipe: Up, Down, Left, Right
  - Pinch: In, Out
  - Rotate: Clockwise, CounterClockwise
  - Tap: None (hidden)

**Action fields:**
- Type: dropdown — Axis, Button
- If Axis: Axis number (1-8), Mode (Hold/Spring/Detent)
- If Button: Button number (1-128), Mode (Momentary/Toggle/Pulse/Double/Rapid/LongShort)

**Mode-specific fields (shown/hidden dynamically):**
- Sensitivity slider — for axis actions
- Decay rate slider — for Spring mode
- Steps slider — for Detent mode
- Delay slider — for Double mode
- Rate slider — for Rapid mode
- Short/Long button + threshold — for LongShort mode

**Behavior:**
- Only one row can be in edit mode at a time
- Save persists to `bindings.json` immediately
- Cancel collapses without saving
- Adding a new binding opens an empty expanded row at the bottom

---

## 5. Settings View

All settings apply immediately (no save button). Changes update `TrackpadService.Settings` in real-time and auto-save to `settings.json`.

### Gesture Recognition

| Setting | Control | Range | Default |
|---------|---------|-------|---------|
| Scroll finger count | Dropdown | 2, 3, 4 | 2 |
| Swipe distance threshold | Slider + value | 0.05 - 0.50 | 0.15 |
| Swipe speed threshold | Slider + value | 0.1 - 1.0 | 0.3 |
| Tap max duration | Slider + value | 100 - 1000ms | 300 |
| Tap max movement | Slider + value | 0.01 - 0.10 | 0.03 |

### Sensitivity

| Setting | Control | Range | Default |
|---------|---------|-------|---------|
| Scroll sensitivity | Slider + value | 0.1 - 5.0 | 1.0 |
| Pinch sensitivity | Slider + value | 0.1 - 5.0 | 1.0 |
| Rotate sensitivity | Slider + value | 0.1 - 5.0 | 1.0 |

### Appearance

| Setting | Control | Options | Default |
|---------|---------|---------|---------|
| Theme | Dropdown | Light, Dark, System | Dark |

---

## 6. Test Panel

The persistent right panel providing live gesture feedback. Subscribes to `TrackpadService` events.

### Trackpad Visualization

A rounded rectangle representing the touchpad surface (~140x100px). Active finger contact positions rendered as colored dots, positioned proportionally using the normalized 0.0-1.0 X/Y from `ContactFrame`. Dots appear when fingers touch and disappear when lifted.

**Data source:** `TrackpadService.Input.OnContactFrame` — updates at ~60Hz, UI updates throttled to ~30fps via dispatcher.

### Gesture Display

Below the visualization:
- **Gesture name + direction** in prominent text (e.g., "Scroll Up", "3-finger Swipe Left")
- **Details** in secondary text: finger count, delta value
- Clears ~500ms after all fingers lift (short hold so the user can read it)

**Data source:** `TrackpadService.Recognizer.OnGestureEvent`

### Matched Binding

If the detected gesture matches a configured binding, show the matched action in a highlighted box (e.g., "→ Axis 1 (hold)"). If the Bindings tab is active, briefly highlight the matched row in the binding list.

**Data source:** Cross-reference `GestureEvent` against `BindingConfig.Bindings`

---

## 7. Theming

Three theme options: Light, Dark, System.

**Implementation:** Two WPF resource dictionaries (`LightTheme.xaml`, `DarkTheme.xaml`) defining colors, brushes, and control styles. `App.xaml.cs` loads the appropriate dictionary at startup and swaps it when the user changes the setting.

**System theme** follows Windows dark/light mode via `SystemParameters` or registry watch.

**Default:** Dark — matches the sim gaming use case (dark cockpit environments).

### Color Palette (Dark Theme)

| Role | Color |
|------|-------|
| Background primary | #1d1d1f |
| Background secondary | #2d2d2f |
| Background tertiary | #3d3d3f |
| Border | #424245 |
| Text primary | #f5f5f7 |
| Text secondary | #86868b |
| Accent | #0a84ff |
| Success (connected) | #30d158 |
| Error (disconnected) | #ff453a |

---

## 8. Window Position Memory

`TrackpadSettings` gains additional fields for window state:

```json
{
  "windowLeft": 100,
  "windowTop": 100,
  "windowWidth": 800,
  "windowHeight": 500,
  "theme": "dark"
}
```

On window open: restore saved position/size. Validate the position is still on-screen (in case monitors changed). If off-screen, center on primary monitor.

On window close (minimize to tray): save current position/size.

---

## 9. ViewModel Architecture

Standard WPF MVVM without a framework (no Prism/MVVM Toolkit needed — the app is simple enough).

### MainViewModel

- Holds `TrackpadService` instance
- Exposes `ActiveTab` (Bindings/Settings)
- Exposes `IsConnected`, `CoreVersion`, `AvailableDevices`, `SelectedDevice`
- Commands: `StartStopCommand`, `OpenWindowCommand`, `ExitCommand`

### BindingsViewModel

- Wraps `BindingConfig.Bindings` as an `ObservableCollection`
- Tracks which row is in edit mode (`EditingBinding`)
- Add/Save/Cancel/Delete operations
- Persists on save via `BindingConfig.Save()`

### SettingsViewModel

- Two-way binds to `TrackpadSettings` properties
- Each property setter calls `Save()` and updates the service in real-time
- Exposes theme options

---

## Implementation Order

1. Project scaffold (.csproj, App.xaml, MainWindow.xaml shell)
2. Themes (DarkTheme.xaml, LightTheme.xaml, theme switching)
3. System tray integration (NotifyIcon, context menu, single instance)
4. MainWindow layout (top bar with tabs, device selector, connection status, split panels)
5. TestPanel control (trackpad visualization, gesture display, matched binding)
6. SettingsView (sliders, dropdowns, live apply)
7. BindingsView (binding list, collapsed rows)
8. BindingEditor (inline editing, dynamic fields)
9. ViewModels (MainViewModel, BindingsViewModel, SettingsViewModel)
10. Window position memory
11. Wire everything together and test

---

## Sub-project Dependencies

This spec depends on:
- **Sub-project 3a** (Trackpad Listener): Completed. Consumed as project reference.

This completes the Apricadabra trackpad plugin.
