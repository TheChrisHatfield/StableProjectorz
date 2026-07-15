<!-- PROMOTED: learning-loop beacon T8 dataset recipe — tertiary until Spec task activated -->

# MLP training dataset recipe — smart-value-paint

**Hook:** `compiler.pipeline`, `spec.flow`, `learning.beacon`  
**Status:** recipe locked from cartridge sources — **not** an active Spec task until you confirm T8  
**Beacons:** SMART_VALUE_PAINT_DEV_1 · Paint Transformer self-training · MLP Decimacon orient (labels = decision heads only)

<!-- ROSETTA-NAV -->

## Agent navigation

1. [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) — `planning.rosetta`
2. [`cartridge-insights.md`](./cartridge-insights.md) — tertiary  
3. Shipped DTO — `ValuePaintProposal` / `IValuePaintAssist` (`Assets/_gm/Features/Paint/SmartValuePaint/`)

## Goal (confirmed)

Train a **small MLP** to map **local paint state → value/stroke decision**, not to generate full images. Rows are **(state → decision)** pairs that match the shipped proposal DTO so inference plugs into `TryAccept` / the paint sink.

## What one row is

```text
state  = features from canvas/reference/history at a patch or stroke center
label  = ValuePaintProposal fields (bins, blend, edge, width, opacity, role)
```

**Not a row:** a finished painting, an SDXL prompt alone, or a Decimacon vault packet.

## Labels (must match Spec R2 / DTO)

| Label | Type | Notes |
|-------|------|--------|
| `current_bin` | enum 0–4 | highlight…accent_dark |
| `desired_bin` | enum 0–4 | next tonal target |
| `blend_strength_01` | float | |
| `edge_softness_01` | float | map later to hardness UI (T7) |
| `brush_width_01` | float | |
| `opacity_01` | float | |
| `stroke_role` | enum | block-in, reinforce, bridge, soften, accent dark |
| optional `delta_value` | float | continuous offset current→desired |

## Inputs (feature vector — engineer first, CNN later)

Per sample (patch / candidate stroke center):

- Canvas patch + optional reference/target patch (or pooled stats)
- Local luminance / value histogram
- Edge / gradient magnitude
- Optional depth / normal / visibility from forge RTs
- Stroke history summary: last *n* deltas, pressure, velocity, angle
- Current brush hints if known

## How to manufacture the dataset (recipe)

### Stage 1 — Value-structure corpus

1. Obtain target images (artist refs **or** SDXL style-consistent set — optional).
2. Quantize to **5 (or 7) value bands** → value maps.
3. Emit rows: local state → `current_bin` / `desired_bin` (+ optional `delta_value`).

### Stage 2 — Decision / stroke-policy corpus

1. Build **in-progress** canvases (partial fills, noise, mid-paint states) — same idea as Paint Transformer **self-training**, but **value-aware**.
2. Teacher sources (any mix):
   - Heuristic / SBR stroke planner over value maps
   - Forge engine synthetic stroke telemetry
   - Human accepts from `TryAccept` → logged proposals (highest quality fine-tune)
3. Each accepted teacher step → one row with full R2 labels.

### Stage 3 — Narrow first, then widen

Start narrow (e.g. portraits or one style family, 5 bands only, block-in + reinforce roles). Then add roles, edges, and styles.

## Agentic builder (optional)

```text
SDXL/targets → value maps → stroke/value teacher → renderer/logs → JSONL rows → train MLP
```

The agent builds **rows**, not the final artwork. Accepted user strokes become golden rows.

## Suggested JSONL schema (one line = one sample)

```json
{
  "id": "svp_000001",
  "mean_luminance_01": 0.52,
  "hist_value_bins": [0.05, 0.1, 0.5, 0.25, 0.1],
  "edge_mag_01": 0.3,
  "history": { "n": 4, "dx": [], "dy": [], "pressure": [], "angle_deg": [] },
  "patch_ref": "optional://path_or_embedding",
  "label": {
    "current_bin": "Midtone",
    "desired_bin": "Shadow",
    "blend_strength_01": 0.7,
    "edge_softness_01": 0.4,
    "brush_width_01": 0.55,
    "opacity_01": 0.6,
    "stroke_role": "BlockIn"
  },
  "source": "synthetic_teacher|forge_log|human_accept"
}
```

## Training order

1. Fit **bin classifier** (and/or `delta_value`) on Stage 1.  
2. Fit remaining heads on Stage 2 (multi-head MLP fine).  
3. Export weights → T5 `IValuePaintAssist` implementation.

## Explicit non-goals (locked)

- MLP does **not** emit full images or Paint Transformer stroke-sets for v1.  
- Dataset pipeline ≠ shipping Decimacon DAG / MoS runtime.  
- No public medical “stroke prediction” datasets — wrong domain.

## Corroborating sources

| Source | Contribution |
|--------|----------------|
| SMART_VALUE_PAINT_DEV_1 | Recipe, labels, SDXL-as-target, agentic builder |
| Paint Transformer PDF | Self-training without off-the-shelf stroke sets |
| MLP Decimacon ORIENT/EXTRA | Decision heads / control MLP role (not image synth) |
| Shipped `ValuePaintProposal` | Schema alignment for inference |

## Loop outcome

**CONFIRM** understanding of dataset creation · **PROMOTE** this recipe file · **BACKLOG** T8 harness until user confirms.
