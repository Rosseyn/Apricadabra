use std::collections::HashMap;

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
