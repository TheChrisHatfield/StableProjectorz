<!-- PROMOTED: multipass meta learning-loop 2026-07-15 — review before handoff -->

# Holistic — StableProjectorz

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../planning-rosetta-stone.md) (`planning.rosetta`) before using this Delta layer.

- **Hook:** `context.delta`
- **Unlocks:** macro / micro / spec alignment
- **Next:** [`10_macro.md`](./10_macro.md)
- **Active feature:** [`smart-value-paint`](./20_micro/smart-value-paint.md) (`spec.flow`)
- **Learning loop:** `learning.loop` (combined literature + Decimacon orientation)

## Mission

Ship reliable 3D projection painting in StableProjectorz, then layer **adaptive value-scale assist** so brush work can follow tonal structure (highlight → light → mid → shadow → accent) without replacing the artist.

## Goals

- Keep the modular paint engine (layers, UV stroke apply, inpaint modes) as the execution surface.
- Add predictive / assistive stroke and value-bin suggestions grounded in reference or SD-derived value maps.
- Keep a clean **control vs execution** split: assist models propose; paint stack commits (`integration.cl_spec` + measured sink).
- Treat Spec Kit as behavioral truth; keep `AGENTS.md` operational only.

## Combined insight laws (multipass — meta)

Triangulated across SMART_VALUE · Paint Transformer · Adaptive Routing/LAVD · MLP Decimacon:

1. **Sink wins** — proposals write only through the existing UV/color path (Tasks 1–4 locked).
2. **Small decision heads first** — value bin / blend / edge / role before stroke-set generators or full Decimacon DAGs.
3. **Scheduler ≠ reasoner** — LAVD/MoS-style routers allocate budget; the MLP/assist reasons about paint decisions (do not merge into one mega-controller in v1).
4. **Selective attention / staged experts** — Decimacon’s staged hybrid + selective self-attention is the long-term family model; **not** a v1 runtime dependency.
5. **Tertiary stays tertiary** — cartridges and PDFs inform Delta/Spec; they do not override Spec Kit ACs.

## Constraints

- Unity / `_gm` paint path is source of truth for what runs; planning docs must map to real types before implementation.
- No production MLP training pipeline required for the first Spec Kit slice — clear APIs + human-reviewable proposals first.
- Cartridge / context-library excerpts are tertiary; they do not override Delta or Spec Kit.
- Full Decimacon orchestration, SDXL training farms, and MoS runtime stay out of scope for smart-value-paint v1 until explicitly opened in Spec.

## Non-goals

- Replacing the brush engine with a generative full-image painter.
- Shipping Decimacon-style orchestration as a hard dependency in v1.
- Replacing `ValuePaintProposal` with Paint Transformer stroke-set tensors for T5.
- Promoting behavioral rules into `AGENTS.md`.
