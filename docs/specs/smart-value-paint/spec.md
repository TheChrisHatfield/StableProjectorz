<!-- PROMOTED: smart-value-paint — review before handoff -->

# Spec: smart-value-paint

**Hook:** `spec.flow`  
**Status:** draft  
**Delta:** [smart-value-paint.md](../../delta/20_micro/smart-value-paint.md)

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Before implementing, load the hook map — do not jump straight into requirements.

1. [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) — hook `planning.rosetta`
2. Follow **UNLOCKS**: `context.delta` → [`00_holistic.md`](../../delta/00_holistic.md), [`10_macro.md`](../../delta/10_macro.md)
3. This feature — hook `spec.flow`:
   - Delta micro: [`smart-value-paint.md`](../../delta/20_micro/smart-value-paint.md)
   - Plan: [`plan.md`](./plan.md)
   - Tasks: [`tasks.md`](./tasks.md)

## What

Add an **adaptive value-scale paint assist** that proposes tonal bins and stroke parameters so the artist can block-in and refine planes within value bands, using the existing StableProjectorz paint/UV stroke path as the sink — not a separate painter.

## Requirements

### R1 — Value banding

The system MUST be able to derive or accept a quantized value map with at least five bands: highlight, light, midtone, shadow, accent dark (names may map to existing UI language).

### R2 — Proposal surface

Given a local canvas/reference patch and optional stroke state, the assist MUST expose proposals for:

- current / desired value bin
- blend strength and edge softness/hardness hints
- brush width / opacity adjustment hints
- stroke role tags: block-in, reinforce plane, bridge planes, soften transition, accent dark

### R3 — Non-destructive assist

Proposals MUST be reviewable by the user; applying a proposal MUST write through the existing paint stack (layer Content / UV apply). Silent overwrite of unrelated layers is forbidden.

### R4 — Spec / CL split

Behavioral acceptance criteria stay in this Spec Kit trio. Operational commands/paths may go to Continual Learning / `AGENTS.md` only after wiring audit (`integration.cl_spec`).

### R5 — MLP baseline (design)

v1 design MUST treat an MLP (or equivalent small predictor) as the **baseline** for parameter prediction / value-bin classification. Temporal sequence models MAY be noted as future work; they are not required for v1 acceptance.

## Out of scope

- Full-image generative painting as the primary interaction
- Required Decimacon orchestration brain at runtime for v1
- Shipping a complete SDXL training pipeline before a proposal UI/API exists

## Open questions

- Exact input feature vector schema (hand-crafted stats vs small CNN embedding)
- Where proposals appear in Paint tab UI vs non-UI API-first slice
- How SDXL/Forge outputs feed value maps without blocking offline authoring
