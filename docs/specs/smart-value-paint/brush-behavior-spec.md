# Brush Behavior Spec: Value Assist (smart-value-paint)

**Hook:** `spec.flow`
**Parent spec:** [spec.md](./spec.md) (R1–R5)
**Status:** binding — audit code against this before changing Value Assist brush behavior
**Created:** 2026-07-18 after repeated regressions (gray paint, flat wash, dead collapse, live vs accept fights)

## Why this document exists

Value Assist bugs kept recurring because intended behavior lived in commit messages
and chat, not in a testable contract. Every fix below was a regression against an
unwritten rule. The rules are now written. **Check code against this file before
editing the brush; update this file first when behavior is intended to change.**

## Definitions

- **Value plane / band** — one of five luminance bins (R1): Highlight ≥0.85,
  Light ≥0.65, Midtone ≥0.40, Shadow ≥0.20, AccentDark <0.20
  (`DeterministicValuePaintAssist.BandFromLuminance`, Rec.709 luminance).
- **Band luminance** — representative target luminance per band:
  Highlight 0.92 · Light 0.75 · Midtone 0.50 · Shadow 0.30 · AccentDark 0.10
  (`ValuePaintProposalApplier.LuminanceForBand`).
- **Chroma base** — the artist's brush color whose hue/saturation must be preserved
  when stepping value.
- **Arm** — write color/size/opacity/hardness into the existing ribbon
  (`SD_WorkflowOptionsRibbon_UI`, `BrushRibbon_UI_*`). Arming never paints by itself;
  strokes still flow through `Inpaint_MaskPainter` → `Apply_into_ColorBrushTex` (R3).

## B1 — Color-preserving value remap (NEVER paint gray)

1. Any color armed by Value Assist MUST be derived from a chroma base via
   `ColorAtDesiredValue(base, band)`: scale RGB so Rec.709 luminance hits the band
   luminance, keeping channel ratios (hue).
2. When channel clamp undershoots the target (saturated colors → Highlight/Light),
   lift toward white only as much as needed to reach the band luminance.
3. Pure gray output is allowed ONLY when the chroma base is achromatic or black
   (luminance < 1e-4).
4. `GrayForBand` exists for band swatches/debug ONLY. It MUST NOT be used to arm
   the brush.

## B2 — Live predict semantics (follow the form)

1. Live samples the surface under the tip (GPU readback in
   `Inpaint_MaskPainter.UpdateValueAssistLiveCursorSample`; alpha < 0.04 skipped —
   never feed the brush its own empty output).
2. Live arms the **value plane under the tip**: `DesiredBin = CurrentBin =
   BandFromLuminance(surface)`. Models that collapse desired→Midtone are overridden
   (`ValuePaintLivePredictor.TryPredictFromSurface`). Painting across light and
   shadow MUST change the armed value.
3. **Chroma base is locked per Live session** (`_liveChromaBase`): remapping from an
   already-shifted brush each tick washes hue. The lock resets on Dismiss, Accept
   suppress, Live toggle, and `ClearArmed`.
4. Live applies plane-scaled opacity through `OpacityInfluence01`
   (Highlight 0.38 · Light 0.52 · Mid 0.65 · Shadow 0.78 · AccentDark 0.88):
   dark planes lay denser than highlights. One flat opacity for all planes is the
   "flat wash" bug.
5. Live color writes are **quiet** (`SetBrushColorQuietFromApi`) — no mode switch,
   no attention animation.
6. Live is **opt-in** (default OFF). A stable user color pick must stay stable until
   the user enables Live.

## B3 — Propose / Accept / Dismiss (snapshot workflow)

1. **Propose** snapshots BOTH the proposal and the brush color at Propose time
   (`_proposalBaseColor`). It clears live soft-arm suppression.
2. **Accept** remaps the **Propose-time base**, not the (possibly Live-drifted)
   current brush: `TryAccept(proposal, proposeBaseColor, out reason)`.
3. Accept is validate-then-commit: every refusal path returns false + reason BEFORE
   any ribbon mutation. Refusals: disabled, wrong workflow/mode (must be
   Inpaint_Color), smudge, erase, missing ribbon UI, unresolved paint target.
   A failed Accept MUST NOT wipe a prior arm.
4. A successful Accept consumes the pending snapshot (Accept button disables) and
   holds an **Accept lock**: Live may not overwrite it. After the first stroke lands
   on the armed target (`OnColorBrushApplied`), the lock demotes to a live soft-arm
   so Live can resume without Dismiss.
5. **Dismiss** clears pending proposal + armed state, suppresses live soft-arm until
   Live is re-enabled or a new Propose happens, and invalidates the predictor so no
   stale Live UI resurrects.

## B4 — Status/swatch priority (single writer order)

Status line and swatch have exactly one priority order; a lower entry may never
overwrite a higher one within the same frame:

1. Disabled → "Off"
2. "Accept refused — …" (kept while relevant)
3. **User Accept arm** (armed, not via live) → "Armed …"
4. **Pending Propose snapshot** → "Proposed … — Accept to arm brush."
5. **Live** (active + has proposal) → "Live A→B · impl"
6. Armed via live (fallback) → "Armed …"
7. Idle

"Dismissed." must persist until a state above changes it (predictor invalidation on
Dismiss guarantees Live cannot repaint it next frame).

## B5 — Non-destructive apply path (R3 restated for the brush)

1. Value Assist never invents a painter. Only arming; strokes go through the
   existing stack onto `ActiveLayer.Content`.
2. Target verification: `ResolveColorPaintTarget` must match ActiveLayer.Content;
   NoColor path refuses color proposals.
3. `OnColorBrushApplied` only flips `SawApplyOnArmedTarget` when the destination is
   the resolved target (reference equality).

## B6 — Tool Options panel behavior

1. **Accordion:** Brush options and Value Assist are mutually exclusive; opening one
   force-collapses the other, including its pinned collapse bar.
2. **Collapse is always reachable:** header toggle (▼/▴) + in-panel "Collapse ▲" +
   pinned viewport bar. Pinned bar MUST be destroyed/rebound on chrome rebuild —
   a stale bar with a dead listener (deferred Destroy) is the "collapse does
   nothing" bug.
3. Dials sync from `PaintTab_ValueAssistOptions` via `Changed`; panel re-syncs on
   enable. `SetIsOnWithoutNotify` for store→UI writes (no feedback loops).
4. Defaults: Enabled ON · Neural ON · Live OFF · Hardness ON ·
   Blend 1.0 · SizeInfluence 0.35 · OpacityInfluence 1.0.
   Rationale: assist ready but silent until the user paints with it; size/opacity
   steps visible without stealing the tip.

## B7 — Artist-role boundary (what Value Assist is NOT)

1. It steps **value** on the artist's chosen color(s). It does not choose hues,
   palettes, warm/cool relationships, or reflected-light colors.
2. Multi-color value painting stays user-driven: the user picks each color; the
   assist helps each color sit on the right value step.
3. Full generative painting, automatic palette generation, and stroke-sequence
   models are out of scope for v1 (parent spec).

## Compliance audit — 2026-07-18

| Rule | Code | Status |
|------|------|--------|
| B1.1–.4 | `ValuePaintProposalApplier.ColorAtDesiredValue` / `TryAccept` / `TryLiveArm`; swatch + cursor use remap | PASS (commits `b9d9105`, `8defe8c`) |
| B2.2 | `ValuePaintLivePredictor.TryPredictFromSurface` forces plane from surface luminance | PASS (`92b370f`) |
| B2.3 | `_liveChromaBase` lock + resets | PASS (`f36d6a7`) |
| B2.4 | Live opacity via `OpacityForPlane` + `OpacityInfluence01` | PASS (`92b370f`) |
| B2.6 | `PaintTab_ValueAssistOptions._livePredict = false` | PASS |
| B3.1–.2 | `_proposalBaseColor` + `TryAccept(proposal, base, out reason)` | PASS (`2c502e9`) |
| B3.3 | validate-then-commit refusal chain | PASS |
| B3.4 | Accept lock + demote in `OnColorBrushApplied` | PASS (`cfc2e7b`) |
| B3.5 | Dismiss: `SuppressLiveSoftArm` + `InvalidateAssist` | PASS (`de318f6`) |
| B4 | `RefreshStatusLine` order + `Update` guards | PASS (`de318f6`) |
| B5 | `ResolveColorPaintTarget` / `OnColorBrushApplied` | PASS |
| B6.1 | `CloseBrushOptionsPanel` / `CollapseUnder` accordion | PASS (`23f7ee8`) |
| B6.2 | pin destroy + rebind in `EnsurePinnedCollapseBar` | PASS (`1b7781e`) |
| B6.4 | defaults in `PaintTab_ValueAssistOptions` | PASS (`92b370f`) |

### Known limitations (accepted, documented)

- `MlpValuePaintAssist.ProposeFromColor` reduces color to luminance (7-float
  features are value-only by design). Chroma preservation happens at apply time
  (B1), not in the model.
- The MLP multi-head frequently collapses DesiredBin→Midtone; Live overrides the
  plane (B2.2). Retraining with plane-follow targets is future work (T9 follow-up).
- Turning Live OFF after an accepted stroke clears the (demoted) arm — intentional
  consequence of B3.4.

## Change protocol

1. Intending to change brush behavior? Edit this spec first (rule + rationale).
2. Then change code to match, citing the rule ID in the commit message.
3. Run the Editor checks (`StableProjectorz/Smart Value Paint/...`) and a Play Mode
   stroke pass: Live OFF stability, Live ON plane-follow, Propose→Accept→paint,
   Dismiss persistence, accordion + collapse reachability.
