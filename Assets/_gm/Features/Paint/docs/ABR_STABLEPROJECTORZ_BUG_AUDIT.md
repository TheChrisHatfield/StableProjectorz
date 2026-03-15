# StableProjectorz bug audit: brush stamp artifact

**Conclusion: No bug found in StableProjectorz that would introduce horizontal/vertical lines (crosshairs) in the brush stamp.** The pipeline from decoded texture to paint is consistent; if the artifact appears in the **exported decoded PNG**, the cause is **decode**. If the PNG is clean, the only remaining suspect is **R8 texture format** on some GPUs (try `UseRgba32ForBrushStamp = true`, which is already the default).

---

## What was checked

### 1. Stamp source (single source, no mix)

- **Projections_MaskPainter** and **Inpaint_MaskPainter** both use `BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback()` and `SetTexture("_BrushStamp", stamp)` every paint frame. No other texture is used for that path.
- **Background_Painter** uses `readSpecificHardnessTex(2)` (ribbon hard brush), not the ABR stamp — by design; not used for projection/inpaint.
- **ProjectorCameras_RenderHelper** (cursor preview) uses `GetCurrentBrushStampTexOrFallback()` — same source.
- **BrushRibbon_UI_Hardness._brushHardnessTex** returns `CurrentBrushStampTex` for display only; it does not set the paint material. No mixed source.

### 2. Texture creation (CreateStampFromGrayscaleBytes)

- **Row order:** We fill a buffer (with optional flipY), then either `SetPixels32(colors)` (RGBA32) or `SetPixelData(src, 0)` (R8). Unity expects row-major; we supply row-major. No stride or alignment mismatch.
- **FlipY:** Only reorders rows for ABR orientation; does not introduce new lines.
- **Format:** RGBA32 path fills Color32(g,g,g,255); R8 path passes the same byte array. Shader samples `.r` only, so both are equivalent in content. No scale/offset is set on the texture.

### 3. Material / shader use

- No `SetTextureScale` or `SetTextureOffset` for `_BrushStamp` anywhere in the project. Stamp is always sampled with default (1,1) scale and (0,0) offset.
- **brushStampUV** (BrushEffects.cginc): Maps screen-space brush rectangle to UV [0,1]². Formula is linear: `uv = (d + 0.5*size) / size` then clamp. No repeating, no stride, no integer alignment that could cause lines.
- **sampleBrushStamp**: `tex2D(i.BrushStamp, uv).r` — single sample, no derivatives or ddx/ddy that could alias.
- **Brush size:** Passed as `_BrushSize_andFirstFrameFlag` (one scalar for radius, then aspect/roundness in shader). Size is applied in screen space; it does not change the stamp texture content or sampling stride.

### 4. Pipeline flow

- Load: ABR → decode → `CreateStampFromGrayscaleBytes` → `_allEntries[i].texture`.
- Current stamp: `CurrentBrushStampTex` = `_allEntries[_currentIndex].texture` (same reference, no copy).
- Paint: `GetCurrentBrushStampTexOrFallback()` → same texture or procedural fallback (never null) → `SetTexture("_BrushStamp", stamp)`.

No resize, no re-encode, no second conversion. The same Texture2D created at load is the one sampled in the shader.

### 5. Other

- **MakeGrayscalePreview** is used only for UI thumbnails; it is not the texture set as `_BrushStamp`.
- **ToBrushStampTexture** (PNG/TGA load) uses `CreateStampFromGrayscaleBytes(..., flipY: false)`; same creation path, no extra code that could add lines.

---

## If the artifact persists

1. **Confirm where it appears**
   - Run **StableProjectorz → Analyze ABR (test: Resource Boy Stipple)** and open the exported PNG.  
   - If the lines are **in the PNG** → bug is in **ABR decode** (header/offset/stride).  
   - If the PNG is **clean** → decode is fine; only remaining app-side possibility is **R8 format** on the GPU.

2. **Force RGBA32**
   - `BrushAlphas_MGR.UseRgba32ForBrushStamp` is already `true` by default. If it was set to `false`, set it back to `true`, reload the ABR, and test again.

3. **Driver / Unity**
   - On some drivers, R8 textures can be interpreted with wrong stride when bound as a shader resource. Using RGBA32 avoids that. No code change in StableProjectorz is required for that beyond keeping the default `UseRgba32ForBrushStamp = true`.

---

## Summary

| Area                    | Checked | Result |
|-------------------------|--------|--------|
| Single stamp source     | Yes    | OK; no mix with ribbon or other texture for projection/inpaint |
| CreateStampFromGrayscaleBytes | Yes | OK; row-major, no stride/alignment introduced |
| SetTexture scale/offset | Yes    | None used |
| brushStampUV / sampling | Yes    | Linear map to [0,1]²; no repeating or stride |
| Pipeline (load → paint) | Yes    | Same Texture2D reference; no resize or re-encode |

**Verdict: The bug is not in StableProjectorz’s use of the brush stamp.** Fix decode (or confirm R8 vs RGBA32) if the artifact is still present.
