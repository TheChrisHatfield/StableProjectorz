<!-- PROMOTED: smart-value-paint — review before handoff -->

<!-- BOOTSTRAP: hive_planner init -->

# Holistic — StableProjectorz

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../planning-rosetta-stone.md) (`planning.rosetta`) before using this Delta layer.

- **Hook:** `context.delta`
- **Unlocks:** macro / micro / spec alignment
- **Next:** [`10_macro.md`](./10_macro.md)
- **Active feature:** [`smart-value-paint`](./20_micro/smart-value-paint.md) (`spec.flow`)

## Mission

Ship reliable 3D projection painting in StableProjectorz, then layer **adaptive value-scale assist** so brush work can follow tonal structure (highlight → light → mid → shadow → accent) without replacing the artist.

## Goals

- Keep the modular paint engine (layers, UV stroke apply, inpaint modes) as the execution surface.
- Add predictive / assistive stroke and value-bin suggestions grounded in reference or SD-derived value maps.
- Treat Spec Kit as behavioral truth for this feature; keep `AGENTS.md` operational only (`integration.cl_spec`).

## Constraints

- Unity / `_gm` paint path is source of truth for what runs; planning docs must map to real types before implementation.
- No production MLP training pipeline required for the first Spec Kit slice — start with clear APIs and human-reviewable proposals.
- Cartridge / context-library excerpts are tertiary; they do not override Delta or Spec Kit.

## Non-goals

- Replacing the brush engine with a generative full-image painter.
- Shipping Decimacon-style orchestration as a hard dependency in v1.
- Promoting behavioral rules into `AGENTS.md`.
