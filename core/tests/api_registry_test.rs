use apricadabra_core::api_registry::ApiRegistry;
use apricadabra_core::protocol::ApiStatus;

#[test]
fn test_known_commands_return_exists() {
    let registry = ApiRegistry::new();
    assert_eq!(registry.status("axis"), ApiStatus::Exists);
    assert_eq!(registry.status("button"), ApiStatus::Exists);
    assert_eq!(registry.status("reset"), ApiStatus::Exists);
}

#[test]
fn test_unknown_commands_return_undefined() {
    let registry = ApiRegistry::new();
    assert_eq!(registry.status("haptic"), ApiStatus::Undefined);
    assert_eq!(registry.status("foobar"), ApiStatus::Undefined);
}

#[test]
fn test_resolve_commands_list() {
    let registry = ApiRegistry::new();
    let result = registry.resolve(&["axis".to_string(), "button".to_string(), "haptic".to_string()]);
    assert_eq!(result.get("axis"), Some(&ApiStatus::Exists));
    assert_eq!(result.get("button"), Some(&ApiStatus::Exists));
    assert_eq!(result.get("haptic"), Some(&ApiStatus::Undefined));
}

#[test]
fn test_has_undefined() {
    let registry = ApiRegistry::new();
    let result = registry.resolve(&["axis".to_string(), "haptic".to_string()]);
    assert!(result.values().any(|s| *s == ApiStatus::Undefined));
}
