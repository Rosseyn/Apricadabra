use std::collections::{HashMap, HashSet};
use std::time::Instant;

const NUM_AXES: usize = 8;
const CENTER: f32 = 0.5;
const DISCONNECT_DECAY_FACTOR: f32 = 0.995; // ~30 seconds to center at 60Hz
const SPRING_DEBOUNCE_MS: u128 = 500;

pub struct AxisManager {
    values: [f32; NUM_AXES],
    spring_decay_factors: [Option<f32>; NUM_AXES],
    spring_last_input: [Option<Instant>; NUM_AXES],
    disconnect_decaying: bool,
    changed: HashSet<u8>,
}

impl AxisManager {
    pub fn new() -> Self {
        Self {
            values: [CENTER; NUM_AXES],
            spring_decay_factors: [None; NUM_AXES],
            spring_last_input: [None; NUM_AXES],
            disconnect_decaying: false,
            changed: HashSet::new(),
        }
    }

    pub fn get(&self, axis_id: u8) -> f32 {
        self.idx(axis_id)
            .map(|i| self.values[i])
            .unwrap_or(CENTER)
    }

    pub fn get_all(&self) -> HashMap<u8, f32> {
        (1..=NUM_AXES as u8)
            .map(|id| (id, self.values[id as usize - 1]))
            .collect()
    }

    pub fn apply_hold(&mut self, axis_id: u8, diff: i32, sensitivity: f32) {
        if let Some(i) = self.idx(axis_id) {
            self.values[i] = (self.values[i] + diff as f32 * sensitivity).clamp(0.0, 1.0);
            self.changed.insert(axis_id);
            self.disconnect_decaying = false;
        }
    }

    pub fn apply_spring(&mut self, axis_id: u8, diff: i32, sensitivity: f32, decay_rate: f32) {
        if let Some(i) = self.idx(axis_id) {
            self.values[i] = (self.values[i] + diff as f32 * sensitivity).clamp(0.0, 1.0);
            self.spring_decay_factors[i] = Some(decay_rate);
            self.spring_last_input[i] = Some(Instant::now());
            self.changed.insert(axis_id);
            self.disconnect_decaying = false;
        }
    }

    pub fn apply_detent(&mut self, axis_id: u8, diff: i32, steps: u32) {
        if let Some(i) = self.idx(axis_id) {
            if steps < 2 {
                return;
            }
            let step_size = 1.0 / (steps - 1) as f32;
            let current_step = (self.values[i] / step_size).round() as i32;
            let new_step = (current_step + diff).clamp(0, (steps - 1) as i32);
            self.values[i] = (new_step as f32 * step_size).clamp(0.0, 1.0);
            self.changed.insert(axis_id);
            self.disconnect_decaying = false;
        }
    }

    pub fn reset(&mut self, axis_id: u8, position: f32) {
        if let Some(i) = self.idx(axis_id) {
            self.values[i] = position.clamp(0.0, 1.0);
            self.spring_decay_factors[i] = None;
            self.changed.insert(axis_id);
        }
    }

    pub fn tick_spring_decay(&mut self) {
        let now = Instant::now();
        for i in 0..NUM_AXES {
            if let Some(factor) = self.spring_decay_factors[i] {
                // Wait for debounce period after last input before decaying
                if let Some(last) = self.spring_last_input[i] {
                    if now.duration_since(last).as_millis() < SPRING_DEBOUNCE_MS {
                        continue;
                    }
                }

                let old = self.values[i];
                self.values[i] = CENTER + (self.values[i] - CENTER) * factor;
                if (self.values[i] - old).abs() > 0.0001 {
                    self.changed.insert((i + 1) as u8);
                }
                if (self.values[i] - CENTER).abs() < 0.001 {
                    self.values[i] = CENTER;
                    self.spring_decay_factors[i] = None;
                    self.spring_last_input[i] = None;
                }
            }
        }
    }

    pub fn start_disconnect_decay(&mut self) {
        self.disconnect_decaying = true;
    }

    pub fn tick_disconnect_decay(&mut self) {
        if !self.disconnect_decaying {
            return;
        }
        let mut any_off_center = false;
        for i in 0..NUM_AXES {
            if (self.values[i] - CENTER).abs() < 0.001 {
                self.values[i] = CENTER;
                continue;
            }
            let old = self.values[i];
            self.values[i] = CENTER + (self.values[i] - CENTER) * DISCONNECT_DECAY_FACTOR;
            if (self.values[i] - old).abs() > 0.00001 {
                self.changed.insert((i + 1) as u8);
            }
            if (self.values[i] - CENTER).abs() < 0.001 {
                self.values[i] = CENTER;
            } else {
                any_off_center = true;
            }
        }
        if !any_off_center {
            self.disconnect_decaying = false;
        }
    }

    pub fn take_changed(&mut self) -> HashMap<u8, f32> {
        let result: HashMap<u8, f32> = self
            .changed
            .iter()
            .filter_map(|&id| self.idx(id).map(|i| (id, self.values[i])))
            .collect();
        self.changed.clear();
        result
    }

    fn idx(&self, axis_id: u8) -> Option<usize> {
        if axis_id >= 1 && axis_id <= NUM_AXES as u8 {
            Some(axis_id as usize - 1)
        } else {
            None
        }
    }
}
