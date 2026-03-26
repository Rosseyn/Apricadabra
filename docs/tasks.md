# Tasks

Backlog of work items. All tasks should live here and be cloned from the base worktree.

## Pending

- [ ] **Refactor LongShort into its own button type** — LongShort is currently a mode on the button command, but it behaves fundamentally differently (discriminates between two buttons based on hold duration). Move it to its own command type to clean up the button mode interface.

- [ ] **Add MultiButton mode** — New button mode that fires up to 5 buttons sequentially on a single press. Use case: macro-style key sequences. Needs configurable delay between each button fire, and the list of buttons (1-5 entries) to fire in order.
