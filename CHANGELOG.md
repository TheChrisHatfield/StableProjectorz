# StableProjectorz — Changelog

**Unity Player Settings version:** `2.4.5`  
This file tracks changes on top of that baseline until the next release bump.

---

## [Unreleased] — 2026-03-11 — Paint layer stack / inpaint → mesh / SD bridge

### Summary

Multi-layer inpaint display and Stable Diffusion capture now follow the same final blit path as the proven single-layer case, with clearer fallbacks and resolution alignment to the mesh accumulation texture.

### Inpaint / layer display (`Inpaint_MaskPainter.cs`)

- **ApplyColorLayer_To_UV_Textures:** unified flow — resolve a single `source` `RenderUdims`, then one EntireColorLayer blit to accumulation (same pattern as `Count == 1` using `ActiveLayer.Content`).
- **CompositeVisibleLayersIntoTemp:** builds the multi-layer `source` by blitting each visible layer onto a cleared temp with `EntireColorLayer_BlitApply` (same material as single-layer), bottom→top, per-layer opacity; active layer can use `APPLY_LATEST_BRUSH_TOO` while painting.
- **Removed** dependency on `PaintLayerStack_MGR.CompositeTo` for the **mesh display** path (avoids runtime-created stack manager often having no compositor shader assigned; `CompositeTo` / `PaintLayer_CompositeBlend` remains for other uses).
- **GetLayerCompositeOrFallback:** multi-layer path uses `CompositeVisibleLayersIntoTemp` for consistency with viewport/SD mask source; fallbacks to active then layer 0.
- **maskResolution():** prefers accumulation texture width/height when UDIM slice count matches; if accumulation not ready, uses `SceneResolution_MGR.resultTexQuality` so layer RTs align with scene UV quality (reduces multi-blit / scale issues).
- **Earlier in same effort:** removed Inpaint_NoColor + SD-prep early return that skipped layer blit during capture; `EnsureBottomLayerHasSceneForComposite` + scene buffer setup for multi-layer; warnings when composite temp or visible `Content` missing.

### Layer stack manager (`PaintLayerStack_MGR.cs`)

- **CanCompositeLayers** public property (`_compositeBlendMat != null`).
- **CompositeTo:** if stack `_resolution` unset, adopt from first visible layer before allocating ping-pong temps; if temps still null, clear dest and log instead of leaving stale mask data.

### Renderer (`Objects_Renderer_MGR.cs`)

- **Apply_InpaintSketch_ColorLayer:** when layer count > 1, always run `ApplyColorLayer_To_UV_Textures` (UsualView still required); single-layer still gated on `allowed_to_showBrushMask()` so inpaint ribbon modes behave as before.

### Notes for testers

- After resolution changes, click in viewport or reload model once so brush `InitTextures` / `EnsureResolution` refresh layer buffers if sizes changed.
- If `PaintLayer_CompositeBlend` fails to load, mesh display no longer depends on it; collapse / mask helpers that still call `CompositeTo` may log warnings.

### Documentation / continual learning

- **`Context_Ref/REVIEW_Codebase_Summary.md`** (sibling folder under `Stable_Projectoz_Dev_Build`) — §4–5, §9–10 updated to match this paint/layer pipeline.
- **`Context_Ref/CONTINUAL_LEARNING.md`** — pointer to `continual-learning/` in this repo.
- **`continual-learning/paint-layers-sd-bridge.md`** — agent-oriented patterns (`→ Pattern:`) for layer/display/SD issues (folder is gitignored).
- **`continual-learning/build-stability.md`** — stub for batch/build patterns per `.cursor` rule.

### Multi-layer paint → SD capture (2026-03-21)

- **`EntireColorLayer_BlitApply.shader`:** `Blend One OneMinusSrcAlpha, One One` — RGB stays premultiplied-over; **alpha is additive** so stacking layers in `CompositeVisibleLayersIntoTemp` matches `PaintLayer_CompositeBlend` (fixes under-opaque composite bleeding accumulation into the content camera / SD init image).
- **`Inpaint_MaskPainter.ApplyColorLayer_To_UV_Textures`:** `forStableDiffusionCapture` bypasses the save-in-progress early return so generation does not capture paint-free accumulation.
- **`Objects_Renderer_MGR.EnsureInpaintColorLayerAppliedForCapture`:** passes `forStableDiffusionCapture: true` and calls **`GL.Flush()`** after refreshing the final material so GPU blits finish before capture.
- **`SD_GenRequests_Helper`:** `EnsureInpaintColorLayerAppliedForCapture` + **`WaitForEndOfFrame`** before img2img payload (non-background); same ordering when upscaling from live view (`fromGen_canBeNull == null`).
- **`SD_Generate_PayloadMaker`:** `EnsureInpaintColorLayerAppliedForCapture` before reading content/accumulation for payloads where applicable.

**Unity package manifests:** `Packages/manifest.json` and `Packages/packages-lock.json` were verified with no working-tree changes at this update (dependencies already in sync with `main`).
