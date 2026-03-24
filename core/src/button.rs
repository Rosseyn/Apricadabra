use std::collections::{HashMap, HashSet};
use std::time::Instant;

const MAX_BUTTONS: usize = 128;
const PULSE_DURATION_MS: u64 = 50;

struct PendingAction {
    button: u8,
    pressed: bool,
    at: Instant,
}

pub struct ButtonManager {
    states: [bool; MAX_BUTTONS],
    changed: HashSet<u8>,
    pending: Vec<PendingAction>,
    rapid_active: HashMap<u8, (u64, Instant)>,
    longshort_start: HashMap<u8, Instant>,
}

pub enum ButtonEvent {
    Short(u8),
    Long(u8),
}

impl ButtonManager {
    pub fn new() -> Self {
        Self {
            states: [false; MAX_BUTTONS],
            changed: HashSet::new(),
            pending: Vec::new(),
            rapid_active: HashMap::new(),
            longshort_start: HashMap::new(),
        }
    }

    pub fn get(&self, button: u8) -> bool {
        self.idx(button).map(|i| self.states[i]).unwrap_or(false)
    }

    pub fn get_all(&self) -> HashMap<u8, bool> {
        (1..=MAX_BUTTONS as u8)
            .filter(|&id| self.states[id as usize - 1])
            .map(|id| (id, true))
            .collect()
    }

    pub fn momentary_down(&mut self, button: u8) {
        self.set(button, true);
    }

    pub fn momentary_up(&mut self, button: u8) {
        self.set(button, false);
    }

    pub fn toggle(&mut self, button: u8) {
        if let Some(i) = self.idx(button) {
            self.states[i] = !self.states[i];
            self.changed.insert(button);
        }
    }

    pub fn pulse(&mut self, button: u8) {
        self.set(button, true);
        self.pending.push(PendingAction {
            button,
            pressed: false,
            at: Instant::now() + std::time::Duration::from_millis(PULSE_DURATION_MS),
        });
    }

    pub fn double_press(&mut self, button: u8, delay_ms: u64) {
        let now = Instant::now();
        let pulse = std::time::Duration::from_millis(PULSE_DURATION_MS);
        let delay = std::time::Duration::from_millis(delay_ms);

        self.set(button, true);
        self.pending.push(PendingAction {
            button,
            pressed: false,
            at: now + pulse,
        });
        self.pending.push(PendingAction {
            button,
            pressed: true,
            at: now + pulse + delay,
        });
        self.pending.push(PendingAction {
            button,
            pressed: false,
            at: now + pulse + delay + pulse,
        });
    }

    pub fn rapid_start(&mut self, button: u8, rate_ms: u64) {
        self.set(button, true);
        self.rapid_active.insert(button, (rate_ms, Instant::now()));
    }

    pub fn rapid_stop(&mut self, button: u8) {
        self.rapid_active.remove(&button);
        self.set(button, false);
    }

    pub fn process_rapid_ticks(&mut self) {
        let now = Instant::now();
        let mut to_toggle = Vec::new();
        for (&button, (rate_ms, last_fire)) in self.rapid_active.iter() {
            if now.duration_since(*last_fire).as_millis() as u64 >= *rate_ms {
                to_toggle.push(button);
            }
        }
        for button in to_toggle {
            if let Some(i) = self.idx(button) {
                self.states[i] = !self.states[i];
                self.changed.insert(button);
            }
            if let Some(entry) = self.rapid_active.get_mut(&button) {
                entry.1 = now;
            }
        }
    }

    pub fn longshort_down(&mut self, short_button: u8, _long_button: u8, _threshold_ms: u64) {
        self.longshort_start.insert(short_button, Instant::now());
    }

    pub fn longshort_up(&mut self, short_button: u8, long_button: u8, threshold_ms: u64) -> Option<u8> {
        if let Some(start) = self.longshort_start.remove(&short_button) {
            let held_ms = start.elapsed().as_millis() as u64;
            let fire_button = if held_ms >= threshold_ms {
                long_button
            } else {
                short_button
            };
            self.pulse(fire_button);
            Some(fire_button)
        } else {
            None
        }
    }

    pub fn process_pending(&mut self) {
        let now = Instant::now();
        let mut i = 0;
        while i < self.pending.len() {
            if now >= self.pending[i].at {
                let action = self.pending.remove(i);
                self.set(action.button, action.pressed);
            } else {
                i += 1;
            }
        }
    }

    pub fn take_changed(&mut self) -> HashMap<u8, bool> {
        let result: HashMap<u8, bool> = self
            .changed
            .iter()
            .filter_map(|&id| self.idx(id).map(|i| (id, self.states[i])))
            .collect();
        self.changed.clear();
        result
    }

    fn set(&mut self, button: u8, pressed: bool) {
        if let Some(i) = self.idx(button) {
            self.states[i] = pressed;
            self.changed.insert(button);
        }
    }

    fn idx(&self, button: u8) -> Option<usize> {
        if button >= 1 && button <= MAX_BUTTONS as u8 {
            Some(button as usize - 1)
        } else {
            None
        }
    }
}
