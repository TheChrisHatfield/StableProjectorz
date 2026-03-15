# ABR (Adobe Brush) usage in Stable Projectorz

## How brushes are used today

- **Brush alpha (stamp)**  
  The app uses a single **grayscale alpha texture** per brush: the brush tip shape. This is passed to the paint shader as `_BrushStamp` and used for opacity/shape when projecting the stroke from screen space to UV space. So we only need **one image per brush** (the tip).

- **Brush size and spacing (universal)**  
  **BrushRibbon_UI_Size** is the single source of truth: one instance in the app, registered as `BrushRibbon_UI_Size.instance`. All painters and UI read size/spacing via `BrushRibbon_UI_Size.GetBrushSize01()` and `GetBrushSpacing01()`; changes (e.g. in the Paint tab) are written to the canonical instance and **persist everywhere** until the user changes them again. Size is 0–1, scaled by `_brushSizeScale` (AnimationCurve) and optionally by tablet pressure. When the user selects a brush that came from an ABR, we set the canonical size/spacing to the ABR’s suggested values.

- **Hardness**  
  The app has **built-in soft/medium/hard** (three round brushes) and **custom alphas** from ABR/PNG/TGA. Custom alphas use their **own texture** as the stamp (so “hardness” is baked into the tip image). There is no separate hardness **parameter** applied at stroke time for ABR brushes; the tip image is the shape.

- **Other paint parameters**  
  Opacity, color, erase/add, depth falloff, and tablet pressure are **global** (workflow ribbon / options). They are not read from the ABR.

---

## What we read from ABR files

### ABR v1 / v2 (old format)

| ABR data | Read? | How we use it |
|----------|-------|----------------|
| Version, brush count | Yes | Dispatch to v1/v2 parser. |
| Brush type (1 = round, 2 = sampled) | Yes | We only extract type 2 (sampled tip image). |
| Brush block size | Yes | Bounds for parsing. |
| **Spacing** (2 bytes, 1–1000%) | **Yes** | Stored in `BrushAlphaEntry.spacingPercent`; when user selects the brush we set the ribbon’s brush spacing (0–1). Pipeline use of spacing (stamps along path) is separate. |
| Brush name (v2, UTF-16) | **Yes** | Decoded and used as the entry’s display name when present. |
| Antialiased flag | Skipped | Not stored or applied. |
| **Tip image** (rect, depth, compression, pixels) | **Yes** | Becomes the brush stamp texture; dimensions used for `suggestedSize01`. |

So from v1/v2 we use: tip image, **spacing**, **brush name**, and suggested size. We do not use: antialiased flag.

### ABR v6+ (tagged blocks)

- We look for **"8BIM"** blocks of type **"samp"** (sample) and **"desc"** (descriptor).
- Inside `samp` we scan for the **brush image pattern**: top, left, bottom, right, depth, compression, then pixel data (uncompressed or RLE). We extract that image and create one `BrushAlphaEntry` per sample.
- When a **"desc"** block appears (often before or after `samp`), we do a **best-effort parse** for spacing, hardness, angle, roundness (OSType key + `doub` type + 8-byte double). Parsed values are applied to brushes from the following `samp` block. Descriptor layout varies; if keys don’t match we get defaults.
- We do **not** parse **"patt"** blocks (patterns).
- So for v6+ we use: **tip image** from `samp`, **suggested size** from tip dimensions, and when present **desc** settings (spacing, hardness, angle, roundness) for entries created from the next `samp`.

---

## ABR format capabilities (reference)

- **Old format (v1–2)**  
  Per-brush: type, **spacing**, optional name, antialiased, **tip bitmap**. No separate hardness/angle/roundness in the old spec; those are often implied by the tip or by Photoshop’s global settings.

- **New format (v6+)**  
  Tagged blocks. **'samp'** = brush tip image(s). **'desc'** = descriptor that can contain brush behavior (spacing, hardness, angle, roundness, scatter, texture, etc.). Adobe’s exact descriptor layout is not fully public, but many ABR presets store meaningful settings there.

So in principle ABR can carry **spacing**, **hardness**, **angle**, **roundness**, and other behavior; we currently use **none** of that, only the tip image (and tip size for suggested size).

---

## Current “headroom” in the paint pipeline

- **Single global size**  
  One slider for all brushes. We can **suggest** a size when selecting an ABR brush (`suggestedSize01`) but we don’t store or restore a “per-brush default size” from the ABR beyond that.

- **Spacing value stored, not yet used in stroke**  
  We **read** spacing from ABR (v1/v2 and best-effort from v6+ desc), store it in the entry, and **set the ribbon’s brush spacing** (0–1) when the user selects that brush. The value is saved/loaded with the project. **Using** it in the stroke (placing stamps at intervals along the path) is a pipeline change still to do.

- **No per-brush hardness parameter**  
  Hardness is either “built-in soft/medium/hard” or “whatever the custom alpha looks like.” To use ABR hardness we’d need either (a) a **per-brush hardness** that modulates the stamp (e.g. blur or falloff), or (b) continue baking it into the tip and ignore ABR hardness. (a) needs a new parameter and shader/CPU logic.

- **No angle/roundness**  
  The brush is applied as a single stamp; there’s no rotation or elliptical stretch from ABR. Adding that would mean passing **angle** and **roundness** (or aspect) into the brush render and the compute shader that projects to UV.

So: **the current pipeline has limited headroom** for “true” ABR features. We can **read** more from the ABR (spacing, hardness, angle, roundness if we reverse-engineer or document the descriptor), but **using** them requires adding parameters and changing how the brush is applied (spacing along path, hardness modulation, angle/roundness in the stamp).

---

## Recommendations

1. **Default brush size 32**  
   Implemented: when brushes are loaded (RebuildEntries) we apply size 32 (0.32) next frame; when loading the ribbon from save we default to 32 if the saved value is invalid or zero.

2. **ABR spacing (done for data + UI)**  
   - **Done:** Parse spacing from ABR v1/v2 and store in `BrushAlphaEntry.spacingPercent`; parse v6+ `desc` when present.  
   - **Done:** Ribbon has `brushSpacing01` / `SetBrushSpacing`; when user selects a brush with suggested spacing we set it; value is saved/loaded.  
   - **Later:** Use spacing in the paint pipeline (place stamps at intervals along the stroke).

3. **ABR descriptor (v6+) (done best-effort)**  
   - **Done:** We parse **"desc"** for 4-byte keys + `doub` + double (spacing, hardness, angle, roundness) and store in `BrushAlphaEntry`. Values apply to brushes from the following `samp` block.  
   - **Done:** Suggested spacing is applied when selecting a brush. Hardness/angle/roundness are stored for future pipeline use.

4. **Per-brush vs global parameters**  
   To “take full advantage” of ABR we’d need to either:  
   - Introduce **per-brush overrides** (e.g. “when this brush is selected, use this spacing/hardness/angle”), or  
   - Keep one set of global sliders and only **suggest** values when switching to a brush that has ABR data.  
   The latter is already the pattern for size; we can extend it to spacing/hardness/angle/roundness once the pipeline has those knobs.

---

## Litmus test: Charcoal Photoshop Brushes 4.abr

Use this file to verify:

- All brush tips load (v1/v2 or v6+ `samp` extraction).  
- After load, brush size is **32** (0.32).  
- Selecting a brush applies **suggested size** from tip dimensions when present.  
- Any future ABR attributes (spacing, hardness, etc.) can be tested by adding parser support and UI/pipeline support as above.
