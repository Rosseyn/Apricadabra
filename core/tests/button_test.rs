use apricadabra_core::button::ButtonManager;
use std::time::Duration;

#[test]
fn test_momentary_down_up() {
    let mut mgr = ButtonManager::new();
    mgr.momentary_down(1);
    assert_eq!(mgr.get(1), true);
    mgr.momentary_up(1);
    assert_eq!(mgr.get(1), false);
}

#[test]
fn test_toggle() {
    let mut mgr = ButtonManager::new();
    assert_eq!(mgr.get(5), false);
    mgr.toggle(5);
    assert_eq!(mgr.get(5), true);
    mgr.toggle(5);
    assert_eq!(mgr.get(5), false);
}

#[tokio::test]
async fn test_pulse_fires_and_releases() {
    let mut mgr = ButtonManager::new();
    mgr.pulse(3);
    assert_eq!(mgr.get(3), true);
    tokio::time::sleep(Duration::from_millis(60)).await;
    mgr.process_pending();
    assert_eq!(mgr.get(3), false);
}

#[tokio::test]
async fn test_double_press() {
    let mut mgr = ButtonManager::new();
    mgr.double_press(4, 50);
    assert_eq!(mgr.get(4), true);
    // Wait for all pending actions to complete
    tokio::time::sleep(Duration::from_millis(200)).await;
    mgr.process_pending();
    assert_eq!(mgr.get(4), false);
}

#[test]
fn test_rapid_fire_with_rate() {
    let mut mgr = ButtonManager::new();
    mgr.rapid_start(1, 100); // 100ms rate

    // Initially button is down
    assert!(mgr.get(1));

    // Simulate time passing
    std::thread::sleep(std::time::Duration::from_millis(110));
    mgr.process_rapid_ticks();
    let changed = mgr.take_changed();
    // Button should have toggled
    assert!(changed.contains_key(&1));
    assert!(!mgr.get(1)); // toggled to off
}

#[test]
fn test_rapid_fire_start_stop() {
    let mut mgr = ButtonManager::new();
    mgr.rapid_start(7, 100);
    assert_eq!(mgr.get(7), true);
    mgr.rapid_stop(7);
    mgr.process_pending();
    assert_eq!(mgr.get(7), false);
}

#[tokio::test]
async fn test_longshort_short_press() {
    let mut mgr = ButtonManager::new();
    mgr.longshort_down(6, 7, 500);
    tokio::time::sleep(Duration::from_millis(100)).await;
    let fired = mgr.longshort_up(6, 7, 500);
    assert_eq!(fired, Some(6));
}

#[tokio::test]
async fn test_longshort_long_press() {
    let mut mgr = ButtonManager::new();
    mgr.longshort_down(6, 7, 200);
    tokio::time::sleep(Duration::from_millis(300)).await;
    let fired = mgr.longshort_up(6, 7, 200);
    assert_eq!(fired, Some(7));
}

#[test]
fn test_changed_buttons() {
    let mut mgr = ButtonManager::new();
    mgr.momentary_down(1);
    let changed = mgr.take_changed();
    assert_eq!(changed.get(&1), Some(&true));
    assert!(mgr.take_changed().is_empty());
}

#[test]
fn test_get_invalid_button_returns_false() {
    let mgr = ButtonManager::new();
    assert_eq!(mgr.get(0), false);
    assert_eq!(mgr.get(129), false);
}
