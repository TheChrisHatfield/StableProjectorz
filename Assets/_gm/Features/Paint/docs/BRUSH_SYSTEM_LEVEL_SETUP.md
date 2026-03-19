# Brush system-level setup

The brush stamp is provided by a **single canonical source** so all painters and the cursor preview use the same texture. Mixed sources (e.g. sometimes BrushAlphas_MGR, sometimes the ribbon’s hardness texture) can cause crosshairs or other artifacts.

## Canonical source

- **BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback()** is the only API painters and cursor code should use for `_BrushStamp`.
- It returns:
  1. **BrushAlphas_MGR.CurrentBrushStampTex** when the manager exists and has a current entry (built-in or ABR/custom).
  2. **A shared fallback stamp** (one small round R8 texture created once) when there is no manager or no entries.
- No painter should use `GetCurrentBrushStampTex() ?? SD_WorkflowOptionsRibbon_UI.instance?._brushHardnessTex` or any other fallback. That mixed setup is removed; everyone uses `GetCurrentBrushStampTexOrFallback()`.

## Who uses the canonical stamp

| Component | Usage |
|----------|--------|
| **Projections_MaskPainter** | `GetCurrentBrushStampTexOrFallback()` → `_brushMaterial.SetTexture("_BrushStamp", stamp)` every paint frame. |
| **Inpaint_MaskPainter** | Same. |
| **ProjectorCameras_RenderHelper** | `GetCurrentBrushStampTexOrFallback()` for brush cursor preview when in edit mode. |
| **Background_Painter** | Intentionally uses `readSpecificHardnessTex(2)` (always hard round) for background masks; not the canonical stamp. |

## Stamp format (system-level)

- Stamps are created as **R8** by default (single channel, shader samples `.r`).
- **BrushAlphas_MGR.UseRgba32ForBrushStamp**: set to `true` to create stamps as **RGBA32** (R=G=B=gray, A=255). Use this to test if R8 causes pipeline/crosshair artifacts on your GPU; the shader still samples `.r`.
- Fallback stamp is always R8.

## If crosshairs persist

1. **Single source:** All 3D projection and inpaint painting now use `GetCurrentBrushStampTexOrFallback()` only. No mixed ribbon/MGR sources.
2. **Format:** Try `BrushAlphas_MGR.UseRgba32ForBrushStamp = true` and reload ABR; if crosshairs disappear, the issue is R8 handling in the pipeline.
3. **Decode:** If the exported `abr_stamp_debug.png` has crosshairs, the bug is in ABR parsing; if the PNG is clean, the bug is in shader/sampling or format.
