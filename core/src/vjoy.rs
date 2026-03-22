use std::collections::HashMap;
#[cfg(windows)]
use std::sync::Arc;

/// Axis identifiers matching vJoy axes 1-8.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Axis {
    X = 1,
    Y = 2,
    Z = 3,
    Rx = 4,
    Ry = 5,
    Rz = 6,
    Slider1 = 7,
    Slider2 = 8,
}

impl Axis {
    pub fn from_id(id: u8) -> Option<Axis> {
        match id {
            1 => Some(Axis::X),
            2 => Some(Axis::Y),
            3 => Some(Axis::Z),
            4 => Some(Axis::Rx),
            5 => Some(Axis::Ry),
            6 => Some(Axis::Rz),
            7 => Some(Axis::Slider1),
            8 => Some(Axis::Slider2),
            _ => None,
        }
    }
}

pub trait VirtualJoystick: Send {
    fn acquire(&mut self, device_id: u8) -> anyhow::Result<()>;
    fn set_axis(&mut self, axis: Axis, value: f32) -> anyhow::Result<()>;
    fn set_button(&mut self, button: u8, pressed: bool) -> anyhow::Result<()>;
    fn release(&mut self) -> anyhow::Result<()>;
}

/// Mock implementation for testing. Records all calls for assertion.
#[derive(Debug, Clone)]
pub struct MockJoystick {
    pub acquired: bool,
    pub device_id: Option<u8>,
    pub axes: HashMap<Axis, f32>,
    pub buttons: HashMap<u8, bool>,
}

impl MockJoystick {
    pub fn new() -> Self {
        Self {
            acquired: false,
            device_id: None,
            axes: HashMap::new(),
            buttons: HashMap::new(),
        }
    }
}

impl VirtualJoystick for MockJoystick {
    fn acquire(&mut self, device_id: u8) -> anyhow::Result<()> {
        self.acquired = true;
        self.device_id = Some(device_id);
        Ok(())
    }

    fn set_axis(&mut self, axis: Axis, value: f32) -> anyhow::Result<()> {
        let clamped = value.clamp(0.0, 1.0);
        self.axes.insert(axis, clamped);
        Ok(())
    }

    fn set_button(&mut self, button: u8, pressed: bool) -> anyhow::Result<()> {
        self.buttons.insert(button, pressed);
        Ok(())
    }

    fn release(&mut self) -> anyhow::Result<()> {
        self.acquired = false;
        Ok(())
    }
}

// HID usage values for vJoy axes
#[cfg(windows)]
const HID_USAGE_X: u32 = 0x30;
#[cfg(windows)]
const HID_USAGE_Y: u32 = 0x31;
#[cfg(windows)]
const HID_USAGE_Z: u32 = 0x32;
#[cfg(windows)]
const HID_USAGE_RX: u32 = 0x33;
#[cfg(windows)]
const HID_USAGE_RY: u32 = 0x34;
#[cfg(windows)]
const HID_USAGE_RZ: u32 = 0x35;
#[cfg(windows)]
const HID_USAGE_SL0: u32 = 0x36;
#[cfg(windows)]
const HID_USAGE_SL1: u32 = 0x37;

/// Real vJoy backend using runtime dynamic loading of vJoyInterface.dll.
#[cfg(windows)]
pub struct VJoyBackend {
    device_id: u32,
    lib: Arc<libloading::Library>,
}

// SAFETY: The vJoy DLL functions are thread-safe for a single device.
#[cfg(windows)]
unsafe impl Send for VJoyBackend {}

#[cfg(windows)]
impl VJoyBackend {
    pub fn new() -> anyhow::Result<Self> {
        let lib = unsafe { Self::load_library()? };

        // Check if vJoy is enabled
        unsafe {
            let vjoy_enabled: libloading::Symbol<unsafe extern "C" fn() -> i32> =
                lib.get(b"vJoyEnabled")
                    .map_err(|e| anyhow::anyhow!("Failed to find vJoyEnabled: {e}"))?;
            if vjoy_enabled() == 0 {
                anyhow::bail!("vJoy driver is not installed or not enabled");
            }
        }

        Ok(Self {
            device_id: 0,
            lib: Arc::new(lib),
        })
    }

    unsafe fn load_library() -> anyhow::Result<libloading::Library> {
        // Try loading from system PATH first
        if let Ok(lib) = libloading::Library::new("vJoyInterface.dll") {
            return Ok(lib);
        }

        // Try known vJoy installation paths
        let candidates = [
            r"C:\Program Files\vJoy\x64\vJoyInterface.dll",
            r"C:\Program Files (x86)\vJoy\x86\vJoyInterface.dll",
            r"C:\Program Files\vJoy\x86\vJoyInterface.dll",
        ];

        for path in &candidates {
            if let Ok(lib) = libloading::Library::new(path) {
                tracing::info!("Loaded vJoyInterface.dll from {path}");
                return Ok(lib);
            }
        }

        anyhow::bail!("Failed to load vJoyInterface.dll. Is vJoy installed?")
    }

    fn axis_to_usage(axis: Axis) -> u32 {
        match axis {
            Axis::X => HID_USAGE_X,
            Axis::Y => HID_USAGE_Y,
            Axis::Z => HID_USAGE_Z,
            Axis::Rx => HID_USAGE_RX,
            Axis::Ry => HID_USAGE_RY,
            Axis::Rz => HID_USAGE_RZ,
            Axis::Slider1 => HID_USAGE_SL0,
            Axis::Slider2 => HID_USAGE_SL1,
        }
    }
}

#[cfg(windows)]
impl VirtualJoystick for VJoyBackend {
    fn acquire(&mut self, device_id: u8) -> anyhow::Result<()> {
        self.device_id = device_id as u32;
        // vJoy status constants:
        // VJD_STAT_OWN  = 0 — owned by this caller (already acquired)
        // VJD_STAT_FREE = 1 — free, can be acquired
        // VJD_STAT_BUSY = 2 — owned by another caller
        // VJD_STAT_MISS = 3 — device doesn't exist
        unsafe {
            let get_status: libloading::Symbol<unsafe extern "C" fn(u32) -> i32> =
                self.lib.get(b"GetVJDStatus")?;
            let status = get_status(self.device_id);

            match status {
                0 => {
                    tracing::info!("vJoy device {} already owned by us", device_id);
                    return Ok(());
                }
                1 => {} // Free — proceed to acquire
                2 => anyhow::bail!("vJoy device {} is owned by another process", device_id),
                3 => anyhow::bail!("vJoy device {} does not exist", device_id),
                _ => anyhow::bail!("vJoy device {} has unknown status {}", device_id, status),
            }

            let acquire: libloading::Symbol<unsafe extern "C" fn(u32) -> i32> =
                self.lib.get(b"AcquireVJD")?;
            if acquire(self.device_id) == 0 {
                anyhow::bail!("Failed to acquire vJoy device {}", device_id);
            }
        }
        Ok(())
    }

    fn set_axis(&mut self, axis: Axis, value: f32) -> anyhow::Result<()> {
        let vjoy_value = (value.clamp(0.0, 1.0) * 32767.0) as i32;
        let usage = Self::axis_to_usage(axis);
        unsafe {
            let set_axis: libloading::Symbol<unsafe extern "C" fn(i32, u32, u32) -> i32> =
                self.lib.get(b"SetAxis")?;
            set_axis(vjoy_value, self.device_id, usage);
        }
        Ok(())
    }

    fn set_button(&mut self, button: u8, pressed: bool) -> anyhow::Result<()> {
        unsafe {
            let set_btn: libloading::Symbol<unsafe extern "C" fn(i32, u32, u8) -> i32> =
                self.lib.get(b"SetBtn")?;
            set_btn(pressed as i32, self.device_id, button);
        }
        Ok(())
    }

    fn release(&mut self) -> anyhow::Result<()> {
        unsafe {
            let relinquish: libloading::Symbol<unsafe extern "C" fn(u32)> =
                self.lib.get(b"RelinquishVJD")?;
            relinquish(self.device_id);
        }
        Ok(())
    }
}
