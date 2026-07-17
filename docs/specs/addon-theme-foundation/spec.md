# Add-on Theme Foundation Specification

## Navigation

- Hooks: `spec.flow`, `context.delta`, `change.validation`
- Delta micro: [`../../delta/20_micro/addon-theme-foundation.md`](../../delta/20_micro/addon-theme-foundation.md)
- Plan: [`plan.md`](./plan.md)
- Tasks: [`tasks.md`](./tasks.md)

## Goal

Allow a trusted local add-on to supply a small set of runtime color tokens through the
existing add-on API, with visible behavior in add-on-created ribbon controls and a safe
reset path.

## Requirements

1. `spz.ui.get_theme` returns the active theme id and complete effective token set.
2. `spz.ui.apply_theme` accepts a non-empty theme id and one or more supported color
   tokens, validates the entire request, and applies it atomically.
3. `spz.ui.reset_theme` restores the built-in StableProjectorz token values.
4. Applying or resetting a theme updates existing add-on panels and controls; controls
   created afterward use the active tokens.
5. The Python SDK and FastAPI mirror expose the same three operations.
6. API capabilities advertise the operations and increment the add-on RPC version.
7. Invalid ids, token names, and colors return `success: false` without changing the
   active theme.

## Initial tokens

- `panel_bg`
- `control_bg`
- `field_bg`
- `accent`
- `text_primary`
- `text_muted`
- `handle`

Colors use `#RRGGBB` or `#RRGGBBAA`.

## Non-goals

- A Nomad Sculpt preset or add-on.
- Automatic recoloring of existing core application UI.
- Font, material, sprite, spacing, or layout replacement.
- Saving the selected theme between application launches.
