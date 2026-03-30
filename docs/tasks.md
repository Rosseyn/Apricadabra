# Tasks

Backlog of work items. All tasks should live here and be cloned from the base worktree.

## Pending

- [ ] **Refactor LongShort into its own button type** — LongShort is currently a mode on the button command, but it behaves fundamentally differently (discriminates between two buttons based on hold duration). Move it to its own command type to clean up the button mode interface.

- [ ] **Add MultiButton mode** — New button mode that fires up to 5 buttons sequentially on a single press. Use case: macro-style key sequences. Needs configurable delay between each button fire, and the list of buttons (1-5 entries) to fire in order.

- [ ] **Stream Deck plugin: production icons** — Replace placeholder SVG icons with polished artwork. Need: plugin icon (256x256 + 512x512 PNG), category icon (28x28 + 56x56 PNG), action list icons (20x20 + 40x40 PNG). All action list icons must be white monochrome on transparent background.

- [ ] **Stream Deck plugin: add showAlert/showOk feedback** — Action handlers should call `ev.action.showAlert()` when core is disconnected and `ev.action.showOk()` on successful fire for modes without visual feedback. Per Elgato marketplace guidelines.

- [ ] **Stream Deck plugin: fix `this.core` vs `this.connection` field name** — ButtonAction constructor assigns to `this.core` but field is declared as `connection`. Likely runtime error. Audit all action files for consistency.

- [ ] **Extract npm package for TypeScript client** — Extract `streamdeck-plugin/src/core-connection.ts` into `@apricadabra/client` npm package, similar to the C# SDK extraction.
