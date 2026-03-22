use apricadabra_core::axis::AxisManager;

#[test]
fn test_initial_state_all_centered() {
    let mgr = AxisManager::new();
    for id in 1..=8 {
        assert!((mgr.get(id) - 0.5).abs() < f32::EPSILON, "Axis {id} should start at 0.5");
    }
}

#[test]
fn test_hold_positive_diff() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 10, 0.01);
    assert!((mgr.get(1) - 0.6).abs() < 0.001);
}

#[test]
fn test_hold_negative_diff() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, -5, 0.02);
    assert!((mgr.get(1) - 0.4).abs() < 0.001);
}

#[test]
fn test_hold_clamps_high() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 1000, 0.1);
    assert!((mgr.get(1) - 1.0).abs() < f32::EPSILON);
}

#[test]
fn test_hold_clamps_low() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, -1000, 0.1);
    assert!((mgr.get(1) - 0.0).abs() < f32::EPSILON);
}

#[test]
fn test_reset_to_position() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 100, 0.01);
    mgr.reset(1, 0.25);
    assert!((mgr.get(1) - 0.25).abs() < f32::EPSILON);
}

#[test]
fn test_detent_5_steps_forward() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, 1, 5);
    assert!((mgr.get(1) - 0.75).abs() < 0.001);
}

#[test]
fn test_detent_5_steps_backward() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, -1, 5);
    assert!((mgr.get(1) - 0.25).abs() < 0.001);
}

#[test]
fn test_detent_clamps_at_max() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, 10, 5);
    assert!((mgr.get(1) - 1.0).abs() < 0.001);
}

#[test]
fn test_detent_clamps_at_min() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, -10, 5);
    assert!((mgr.get(1) - 0.0).abs() < 0.001);
}

#[test]
fn test_spring_moves_then_decays() {
    let mut mgr = AxisManager::new();
    mgr.apply_spring(1, 10, 0.01, 0.9);
    let after_move = mgr.get(1);
    assert!(after_move > 0.55);

    for _ in 0..100 {
        mgr.tick_spring_decay();
    }
    let after_decay = mgr.get(1);
    assert!((after_decay - 0.5).abs() < 0.01, "Should have decayed close to center, got {after_decay}");
}

#[test]
fn test_spring_new_input_resets_decay() {
    let mut mgr = AxisManager::new();
    mgr.apply_spring(1, 10, 0.01, 0.9);
    for _ in 0..10 {
        mgr.tick_spring_decay();
    }
    let mid = mgr.get(1);
    mgr.apply_spring(1, 10, 0.01, 0.9);
    assert!(mgr.get(1) > mid);
}

#[test]
fn test_decay_all_on_disconnect() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 100, 0.01);
    mgr.apply_hold(2, -100, 0.01);
    mgr.start_disconnect_decay();

    for _ in 0..1000 {
        mgr.tick_disconnect_decay();
    }

    assert!((mgr.get(1) - 0.5).abs() < 0.01, "Axis 1 should decay to center");
    assert!((mgr.get(2) - 0.5).abs() < 0.01, "Axis 2 should decay to center");
}

#[test]
fn test_get_invalid_axis_returns_default() {
    let mgr = AxisManager::new();
    assert!((mgr.get(0) - 0.5).abs() < f32::EPSILON);
    assert!((mgr.get(9) - 0.5).abs() < f32::EPSILON);
}

#[test]
fn test_changed_axes_returns_only_changed() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 10, 0.01);
    let changed = mgr.take_changed();
    assert!(changed.contains_key(&1));
    assert!(!changed.contains_key(&2));

    let changed2 = mgr.take_changed();
    assert!(changed2.is_empty());
}
